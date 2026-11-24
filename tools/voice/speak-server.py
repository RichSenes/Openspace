#!/usr/bin/env python3
"""Shiori's voice: a small local text-to-speech server.

Openspace sends a sentence or two and the language, and gets back WAV audio. Nothing leaves the
machine. Two engines:

  qwen    Qwen3-TTS Base (Apache-2.0): clones the voice in --voice (a folder with voice.wav and
          voice.txt, made by design-voice.py) and makes it speak Italian, English and Japanese,
          so Shiori has one voice in all three.
  qwen-fast  the same voice through faster-qwen3-tts (MIT), which replays CUDA graphs: on an
          RTX 3060 about twice as fast as speech, against 1.7 times slower for qwen. Wants
          transformers 5.15 in its own environment (pip install faster-qwen3-tts transformers==5.15.1).
  kokoro  Kokoro-82M (Apache-2.0): tiny and quick, even on a processor, but each language has
          its own preset voice.

    python3 speak-server.py --engine qwen --model DIR --voice DIR [--port 8177] [--device auto]
    python3 speak-server.py --engine kokoro --model DIR [--voices it=if_sara,en=af_heart,ja=jf_alpha]

POST /speak    {"text": "Ciao!", "language": "it"}  ->  audio/wav
GET  /health   ->  {"ok": true, "engine": "qwen", "voice": "shiori", "device": "cuda"}
"""
import argparse
import io
import json
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import numpy as np
import soundfile as sf

NAMES = {"it": "Italian", "en": "English", "ja": "Japanese"}
MAX_CHARS = 1200


class Qwen:
    """Qwen3-TTS Base, cloning one designed voice for every language."""

    def __init__(self, args):
        import torch
        from qwen_tts import Qwen3TTSModel

        voice = Path(args.voice)
        self.voice = voice.name
        device = "cuda:0" if args.device in ("auto", "cuda") and torch.cuda.is_available() else "cpu"
        self.device = device
        self.model = Qwen3TTSModel.from_pretrained(
            args.model, device_map=device, dtype=torch.bfloat16 if device != "cpu" else torch.float32
        )
        wav, sr = sf.read(voice / "voice.wav", dtype="float32")
        # Built once: every sentence reuses the same voice, so it never drifts.
        self.prompt = self.model.create_voice_clone_prompt(
            ref_audio=(wav, sr), ref_text=(voice / "voice.txt").read_text(encoding="utf-8").strip()
        )

    def speak(self, text, language):
        wavs, sr = self.model.generate_voice_clone(text=text, language=NAMES[language], voice_clone_prompt=self.prompt)
        return wavs[0], sr


class QwenFast(Qwen):
    """Qwen3-TTS Base through faster-qwen3-tts: the same cloned voice, generated with CUDA graphs."""

    def __init__(self, args):  # noqa: D107 - same voice folder as Qwen
        from faster_qwen3_tts import FasterQwen3TTS

        voice = Path(args.voice)
        self.voice = voice.name
        self.device = "cuda"
        self.model = FasterQwen3TTS.from_pretrained(args.model)
        self.ref = str(voice / "voice.wav")
        self.ref_text = (voice / "voice.txt").read_text(encoding="utf-8").strip()

    def speak(self, text, language):
        wavs, sr = self.model.generate_voice_clone(text=text, language=NAMES[language], ref_audio=self.ref, ref_text=self.ref_text)
        return wavs[0], sr


class Kokoro:
    """Kokoro-82M: one preset voice per language."""

    CODES = {"it": "i", "en": "a", "ja": "j"}

    def __init__(self, args):
        import torch
        from kokoro import KModel, KPipeline

        root = Path(args.model)
        self.device = "cuda" if args.device in ("auto", "cuda") and torch.cuda.is_available() else "cpu"
        model = KModel(repo_id="hexgrad/Kokoro-82M", config=str(root / "config.json"), model=str(root / "kokoro-v1_0.pth")).to(self.device).eval()
        self.voices = dict(v.split("=") for v in args.voices.split(","))
        self.voice = ",".join(self.voices.values())
        self.paths = {language: str(root / "voices" / f"{name}.pt") for language, name in self.voices.items()}
        self.pipes = {language: KPipeline(lang_code=code, repo_id="hexgrad/Kokoro-82M", model=model) for language, code in self.CODES.items()}

    def speak(self, text, language):
        parts = [audio.numpy() for _, _, audio in self.pipes[language](text, voice=self.paths[language]) if audio is not None]
        return (np.concatenate(parts) if parts else np.zeros(2400, np.float32)), 24000


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--engine", default="qwen", choices=["qwen", "qwen-fast", "kokoro"])
    p.add_argument("--model", required=True, help="the model folder")
    p.add_argument("--voice", help="qwen: the folder with voice.wav and voice.txt (design-voice.py)")
    p.add_argument("--voices", default="it=if_sara,en=af_heart,ja=jf_alpha", help="kokoro: a preset voice per language")
    p.add_argument("--device", default="auto", choices=["auto", "cuda", "cpu"])
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=8177)
    args = p.parse_args()
    if args.engine.startswith("qwen") and not args.voice:
        raise SystemExit("speak-server: --voice is needed with qwen (make one with design-voice.py)")

    engine = {"qwen": Qwen, "qwen-fast": QwenFast, "kokoro": Kokoro}[args.engine](args)
    lock = threading.Lock()
    with lock:
        engine.speak("Ciao.", "it")  # warms up, so the first real sentence is not the slow one
    print(f"speak-server: {args.engine} ({engine.voice}) on {engine.device}, http://{args.host}:{args.port}", flush=True)

    class Handler(BaseHTTPRequestHandler):
        def reply(self, code, body, kind="application/json; charset=utf-8"):
            data = body if isinstance(body, bytes) else json.dumps(body, ensure_ascii=False).encode()
            self.send_response(code)
            self.send_header("Content-Type", kind)
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            if self.path.split("?")[0] == "/health":
                return self.reply(200, {"ok": True, "engine": args.engine, "voice": engine.voice, "device": engine.device})
            self.reply(404, {"error": "not found"})

        def do_POST(self):
            if self.path.split("?")[0] != "/speak":
                return self.reply(404, {"error": "not found"})
            try:
                body = json.loads(self.rfile.read(int(self.headers.get("Content-Length") or 0)) or b"{}")
            except ValueError:
                return self.reply(400, {"error": "send {\"text\": ..., \"language\": ...} as JSON"})
            text = str(body.get("text") or "").strip()
            language = body.get("language") if body.get("language") in NAMES else "it"
            if not text or len(text) > MAX_CHARS:
                return self.reply(400, {"error": f"give her between 1 and {MAX_CHARS} characters to say"})
            started = time.monotonic()
            try:
                with lock:
                    wav, sr = engine.speak(text, language)
            except Exception as e:  # noqa: BLE001 - told to the page, which stays quiet
                print(f"speak-server: failed on {len(text)} characters: {e}", file=sys.stderr, flush=True)
                return self.reply(500, {"error": f"her voice failed: {e}"})
            out = io.BytesIO()
            sf.write(out, np.asarray(wav, dtype=np.float32), sr, format="WAV", subtype="PCM_16")
            self.send_response(200)
            data = out.getvalue()
            self.send_header("Content-Type", "audio/wav")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("X-Seconds", f"{time.monotonic() - started:.2f}")
            self.end_headers()
            self.wfile.write(data)

        def log_message(self, *_):
            pass

    ThreadingHTTPServer((args.host, args.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
