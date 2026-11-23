#!/usr/bin/env python3
"""Shiori's ears: a small local speech-to-text server on faster-whisper.

Openspace sends what the person said (any format ffmpeg reads: webm/opus from the browser,
wav, ogg) and gets back the text and the language spoken. Nothing leaves the machine.

    python3 listen-server.py --model medium --wake-model base --port 8178 [--download-root DIR] [--device auto]

POST /transcribe   body: the audio bytes; header Content-Type ignored
                   query: ?language=it|en|ja to force one, else the likeliest of the three
                   ->  {"text": "...", "language": "it", "seconds": 2.4, "called": false}
                   query: ?wake=1 when always listening: the small model first checks whether
                   Shiori was called by name; only then the main model hears the request, and
                   "text" is what came after her name ("" when only her name was said)
                   ->  {"text": "che ore sono?", "language": "it", "seconds": 0.6, "called": true}
GET  /health       ->  {"ok": true, "model": "medium", "device": "cpu", "wakeModel": "base", "wakeDevice": "cuda"}

Requires: pip install faster-whisper; ffmpeg on the PATH.
"""
import argparse
import json
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

import numpy as np
from faster_whisper import WhisperModel

from wake import called

LANGUAGES = {"it", "en", "ja"}
MAX_BYTES = 25 * 1024 * 1024


def decode(audio: bytes) -> np.ndarray:
    """Any audio to 16 kHz mono float32, through ffmpeg."""
    out = subprocess.run(
        ["ffmpeg", "-nostdin", "-loglevel", "error", "-i", "pipe:0", "-f", "s16le", "-ac", "1", "-ar", "16000", "pipe:1"],
        input=audio,
        capture_output=True,
        check=True,
    ).stdout
    return np.frombuffer(out, np.int16).astype(np.float32) / 32768.0


def load(name: str, args) -> tuple[WhisperModel, str]:
    devices = ["cuda", "cpu"] if args.device == "auto" else [args.device]
    last = None
    for device in devices:
        try:
            # int8 weights with float16 maths: half the graphics memory of float16, same words.
            compute = "int8_float16" if device == "cuda" else "int8"
            model = WhisperModel(name, device=device, compute_type=compute, download_root=args.download_root)
            # One tiny pass, so a broken GPU setup fails here and not on the first sentence.
            model.transcribe(np.zeros(16000, np.float32), language="en")
            return model, device
        except Exception as e:  # noqa: BLE001 - fall back to the next device
            last = e
            print(f"listen-server: {device} not usable ({e}); trying the next", file=sys.stderr)
    raise SystemExit(f"listen-server: no device could load the model: {last}")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--model", default="medium")
    # Always listening runs everything said near the mic through this one: it must be light.
    p.add_argument("--wake-model", default="base", help='"none" to use the main model for the name too')
    p.add_argument("--download-root", default=None)
    p.add_argument("--device", default="auto", choices=["auto", "cuda", "cpu"])
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=8178)
    args = p.parse_args()

    model, device = load(args.model, args)
    if args.wake_model in ("none", args.model):
        waker, wake_device = model, device
    else:
        waker, wake_device = load(args.wake_model, args)
    lock = threading.Lock()
    print(f"listen-server: {args.model} on {device}, name heard by {args.wake_model} on {wake_device}, http://{args.host}:{args.port}", flush=True)

    def transcribe(which, samples, language, **extra):
        with lock:
            # Shiori speaks three languages: left to itself, whisper hears a short Italian
            # sentence as Portuguese or a name as Hindi. Pick the likeliest of the three.
            if language is None:
                _, _, likely = which.detect_language(samples, vad_filter=True)
                language = max((p, l) for l, p in likely if l in LANGUAGES)[1]
            segments, info = which.transcribe(samples, language=language, vad_filter=True, beam_size=5, **extra)
            return " ".join(s.text.strip() for s in segments).strip(), info.language

    class Handler(BaseHTTPRequestHandler):
        def reply(self, code, body):
            data = json.dumps(body, ensure_ascii=False).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            if urlparse(self.path).path == "/health":
                return self.reply(200, {"ok": True, "model": args.model, "device": device, "wakeModel": args.wake_model, "wakeDevice": wake_device})
            self.reply(404, {"error": "not found"})

        def do_POST(self):
            url = urlparse(self.path)
            if url.path != "/transcribe":
                return self.reply(404, {"error": "not found"})
            size = int(self.headers.get("Content-Length") or 0)
            if size <= 0 or size > MAX_BYTES:
                return self.reply(400, {"error": "send the audio as the body, up to 25 MB"})
            audio = self.rfile.read(size)
            language = (parse_qs(url.query).get("language") or [None])[0]
            if language not in LANGUAGES:
                language = None
            started = time.monotonic()
            try:
                samples = decode(audio)
            except subprocess.CalledProcessError:
                return self.reply(400, {"error": "the audio could not be read"})
            took = lambda: round(time.monotonic() - started, 2)  # noqa: E731
            if (parse_qs(url.query).get("wake") or ["0"])[0] != "1":
                text, spoken = transcribe(model, samples, language)
                return self.reply(200, {"text": text, "language": spoken, "seconds": took(), "called": False})
            # Her name, written the way the small model is nudged to write it.
            text, spoken = transcribe(waker, samples, language, hotwords="Shiori")
            yes, rest = called(text)
            if not yes or not rest or waker is model:
                return self.reply(200, {"text": rest, "language": spoken, "seconds": took(), "called": yes})
            # Called with a request: the main model hears it properly.
            text, spoken = transcribe(model, samples, language, hotwords="Shiori")
            again, better = called(text)
            self.reply(200, {"text": better if again else text, "language": spoken, "seconds": took(), "called": True})

        def log_message(self, *_):
            pass

    ThreadingHTTPServer((args.host, args.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
