#!/usr/bin/env python3
"""Shiori's voice: a small local text-to-speech server.

Openspace sends a sentence or two and the language, and gets back her voice, whole (WAV) or as it
is made (raw 16-bit PCM, so the first words play almost at once). Nothing leaves the machine.

Engines:

  voxcpm     VoxCPM2 (Apache-2.0): clones the voice in --voice (a folder with voice.wav and
             voice.txt, made by design-voice.py) in Italian, English and Japanese, with the most
             natural intonation of the ones tried. On an RTX 3060 a little faster than speech,
             about 6 GB of graphics memory.
  qwen       Qwen3-TTS Base (Apache-2.0): the same kind of cloning; slower than speech.
  qwen-fast  the same voice through faster-qwen3-tts (MIT), which replays CUDA graphs: about twice
             as fast as speech. Wants transformers 5.15 in its own environment.
  kokoro     Kokoro-82M (Apache-2.0): tiny and quick, even on a processor, but each language has
             its own preset voice.

The model lives in a child process that is started when she is about to speak and stopped when
she has been quiet for --idle seconds, so the graphics card is all free again for other work
(image generation, training, a large language model). When the card has less free memory than
the engine needs, she does not start: she answers 503 with "busy", and Openspace shows her words
in writing only until there is room. When she is quiet and someone else fills the card (the free
memory falls under --leave-gb), she makes room without waiting for --idle.

    python3 speak-server.py --engine voxcpm --model DIR --voice DIR [--port 8177] [--idle 300]
    python3 speak-server.py --engine kokoro --model DIR [--voices it=if_sara,en=af_heart,ja=jf_alpha]

POST /speak    {"text": "Ciao!", "language": "it"}                 ->  audio/wav
POST /speak    {"text": "Ciao!", "language": "it", "stream": true} ->  audio/pcm (16-bit, mono), X-Sample-Rate
POST /wake     loads the model now, so the first sentence does not wait for it
POST /sleep    frees the graphics card now; {"quietFor": 60} only after a minute of quiet
GET  /health   ->  {"ok": true, "engine": "voxcpm", "voice": "morbida", "state": "asleep", ...}
"""
import argparse
import io
import json
import multiprocessing as mp
import os
import shutil
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import numpy as np
import soundfile as sf

NAMES = {"it": "Italian", "en": "English", "ja": "Japanese"}
MAX_CHARS = 1200
# Graphics memory each engine needs to start, in GB, measured on an RTX 3060 with some headroom.
NEEDS = {"voxcpm": 6.5, "qwen": 5.0, "qwen-fast": 5.0, "kokoro": 1.0}


class Engine:
    """Says a sentence, whole or a piece at a time. Engines that cannot stream yield it whole."""

    def speak(self, text, language):
        raise NotImplementedError

    def stream(self, text, language):
        yield self.speak(text, language)


class VoxCPM(Engine):
    """VoxCPM2, cloning one voice for every language.

    The voice folder holds what to clone, any of:
      reference.wav             the timbre only: who she is, without copying an accent or pace
      voice.wav + voice.txt     a clip she continues: timbre, accent and pace (Italian, say)
      voice-ja.wav + voice-ja.txt  a clip for her Japanese, so each language sounds native
    A clip in one language makes the others sound foreign-accented: an English clip gives her
    Italian an English accent. A timbre reference with a clip per language avoids it.
    """

    def __init__(self, args):
        import torch
        from voxcpm import VoxCPM as Model

        voice = Path(args.voice)
        self.voice = voice.name
        self.device = "cuda" if args.device in ("auto", "cuda") and torch.cuda.is_available() else "cpu"
        # No torch.compile (optimize): on an RTX 3060 it made speech slower and loading longer.
        # No denoiser: it is fetched from the internet, and a designed clip is clean anyway.
        model = Model.from_pretrained(args.model, load_denoiser=False, optimize=False)
        self.tts = model.tts_model
        self.rate = self.tts.sample_rate
        reference = voice / "reference.wav"
        reference = str(reference) if reference.exists() else None

        def cache(clip):
            wav, txt = voice / f"{clip}.wav", voice / f"{clip}.txt"
            if not wav.exists():
                return None
            return self.tts.build_prompt_cache(prompt_text=txt.read_text(encoding="utf-8").strip(), prompt_wav_path=str(wav),
                                               reference_wav_path=reference)

        # Built once: every sentence reuses the same voice, so it never drifts (and is quicker).
        default = cache("voice") or (self.tts.build_prompt_cache(reference_wav_path=reference) if reference else None)
        if default is None:
            raise SystemExit(f"speak-server: {voice} has neither voice.wav nor reference.wav")
        self.caches = {"default": default}
        for language in NAMES:
            if (found := cache(f"voice-{language}")) is not None:
                self.caches[language] = found

    def _make(self, text, language, streaming):
        return self.tts._generate_with_prompt_cache(  # noqa: SLF001 - the public call rebuilds the voice every time
            target_text=" ".join(text.split()), prompt_cache=self.caches.get(language, self.caches["default"]), min_len=2, max_len=4096,
            inference_timesteps=10, cfg_value=2.0, retry_badcase=not streaming, retry_badcase_max_times=3,
            retry_badcase_ratio_threshold=6.0, streaming=streaming,
        )

    def speak(self, text, language):
        made = self._make(text, language, False)
        try:
            wav, _, _ = next(made)
        finally:
            made.close()
        return wav.squeeze(0).cpu().numpy(), self.rate

    def stream(self, text, language):
        made = self._make(text, language, True)
        try:
            for wav, _, _ in made:
                yield wav.squeeze(0).cpu().numpy(), self.rate
        finally:
            made.close()


class Qwen(Engine):
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
        self.prompt = self.model.create_voice_clone_prompt(
            ref_audio=(wav, sr), ref_text=(voice / "voice.txt").read_text(encoding="utf-8").strip()
        )

    def speak(self, text, language):
        wavs, sr = self.model.generate_voice_clone(text=text, language=NAMES[language], voice_clone_prompt=self.prompt)
        return wavs[0], sr


class QwenFast(Engine):
    """Qwen3-TTS Base through faster-qwen3-tts: the same cloned voice, generated with CUDA graphs."""

    def __init__(self, args):
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


class Kokoro(Engine):
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

    def stream(self, text, language):
        for _, _, audio in self.pipes[language](text, voice=self.paths[language]):
            if audio is not None:
                yield audio.numpy(), 24000


ENGINES = {"voxcpm": VoxCPM, "qwen": Qwen, "qwen-fast": QwenFast, "kokoro": Kokoro}


def pcm16(wav):
    return (np.clip(np.asarray(wav, dtype=np.float32), -1, 1) * 32767).astype("<i2").tobytes()


def worker(args, conn, cancel):
    """The child process: loads the engine, then says what it is sent until told to stop."""
    os.environ.setdefault("TQDM_DISABLE", "1")  # no progress bar per sentence in the log
    if args.device == "cpu":
        os.environ["CUDA_VISIBLE_DEVICES"] = ""  # some engines (VoxCPM) pick the card by themselves
    try:
        engine = ENGINES[args.engine](args)
        for _ in engine.stream("Ciao.", "it"):  # warms up, so the first real sentence is not the slow one
            pass
    except BaseException as e:  # noqa: BLE001 - told to the parent, which tells the page
        conn.send(("failed", f"{type(e).__name__}: {e}"))
        return
    conn.send(("ready", engine.voice, engine.device))
    while True:
        asked = conn.recv()
        if asked is None:
            return
        text, language, streaming = asked
        try:
            if streaming:
                for wav, sr in engine.stream(text, language):
                    if cancel.is_set():
                        break
                    conn.send(("audio", pcm16(wav), sr))
            else:
                wav, sr = engine.speak(text, language)
                conn.send(("audio", pcm16(wav), sr))
            conn.send(("end",))
        except Exception as e:  # noqa: BLE001 - one failed sentence does not stop her
            conn.send(("error", f"{type(e).__name__}: {e}"))


def free_gb():
    """Free graphics memory in GB, from nvidia-smi (the server itself never touches the card)."""
    if not shutil.which("nvidia-smi"):
        return None
    try:
        out = subprocess.run(["nvidia-smi", "--query-gpu=memory.free", "--format=csv,noheader,nounits"],
                             capture_output=True, text=True, timeout=5, check=True).stdout
        return max(int(line) for line in out.split()) / 1024
    except (OSError, ValueError, subprocess.SubprocessError):
        return None


class Busy(Exception):
    """The graphics card has no room for her voice right now."""


class Voice:
    """Starts the engine's process when needed and stops it when she has been quiet for a while."""

    def __init__(self, args):
        self.args = args
        self.need = args.need_gb if args.need_gb is not None else NEEDS[args.engine]
        self.state = "asleep"  # asleep, waking, awake, failed
        self.problem = None
        self.voice = Path(args.voice).name if args.voice else None
        self.device = None
        self.used = time.monotonic()
        self.guard = threading.Lock()  # the state
        self.turn = threading.Lock()  # one sentence at a time
        self.awake = threading.Event()
        self.ctx = mp.get_context("spawn")
        self.process = None
        self.conn = None
        self.cancel = self.ctx.Event()
        threading.Thread(target=self._watch, daemon=True).start()

    def wake(self):
        """Starts loading when there is room; raises Busy when there is not."""
        with self.guard:
            self.used = time.monotonic()
            if self.state in ("waking", "awake"):
                return self.state
            if self.args.device != "cpu":
                free = free_gb()
                if free is not None and free < self.need:
                    raise Busy(f"the graphics card is busy: {free:.1f} GB free, her voice needs {self.need:.1f} GB")
            parent, child = self.ctx.Pipe()
            self.cancel.clear()
            self.process = self.ctx.Process(target=worker, args=(self.args, child, self.cancel), daemon=True)
            self.process.start()
            self.conn = parent
            self.state, self.problem = "waking", None
            self.awake.clear()
            threading.Thread(target=self._wait_ready, args=(parent,), daemon=True).start()
            print(f"speak-server: waking {self.args.engine}", flush=True)
            return self.state

    def _wait_ready(self, conn):
        try:
            said = conn.recv()
        except (EOFError, OSError):
            said = ("failed", "the voice process stopped while loading")
        with self.guard:
            if conn is not self.conn:
                return
            if said[0] == "ready":
                self.state, self.voice, self.device = "awake", said[1], said[2]
                print(f"speak-server: awake ({self.voice} on {self.device})", flush=True)
            else:
                self.state, self.problem = "failed", said[1]
                print(f"speak-server: could not wake: {said[1]}", file=sys.stderr, flush=True)
                self._stop()
            self.awake.set()

    def sleep(self, why="asked"):
        with self.guard:
            if self.state in ("waking", "awake"):
                print(f"speak-server: going to sleep ({why})", flush=True)
            self._stop()
            if self.state != "failed":
                self.state = "asleep"

    def _stop(self):
        process, conn = self.process, self.conn
        self.process = self.conn = None
        self.awake.set()
        if conn is not None:
            try:
                conn.send(None)
            except (OSError, ValueError):
                pass
        if process is not None:
            process.join(5)
            if process.is_alive():
                process.kill()
                process.join(5)
        if conn is not None:
            conn.close()

    def _watch(self):
        while True:
            time.sleep(10)
            quiet = time.monotonic() - self.used
            if self.state != "awake" or self.turn.locked():
                continue
            if quiet > self.args.idle:
                self.sleep(f"quiet for {int(quiet)} s")
            elif quiet > 20 and self.args.device != "cpu":
                free = free_gb()
                if free is not None and free < self.args.leave_gb:
                    self.sleep(f"someone else needs the graphics card ({free:.1f} GB free)")

    def say(self, text, language, streaming):
        """Yields (16-bit PCM bytes, sample rate); the first item may take the loading time."""
        if self.state == "failed":
            with self.guard:
                self.state = "asleep"  # tried again: the reason may be gone
        self.wake()
        self.awake.wait(self.args.load_timeout)
        with self.turn:
            with self.guard:
                if self.state != "awake":
                    raise RuntimeError(self.problem or "her voice is still loading")
                conn = self.conn
                self.used = time.monotonic()
                self.cancel.clear()
            conn.send((text, language, streaming))
            finished = False
            try:
                while True:
                    said = conn.recv()
                    if said[0] == "audio":
                        yield said[1], said[2]
                    elif said[0] == "end":
                        finished = True
                        return
                    else:
                        finished = True
                        raise RuntimeError(said[1])
            finally:
                if not finished:  # the page stopped listening: tell the engine, and wait for it to stop
                    self.cancel.set()
                    try:
                        while conn.recv()[0] == "audio":
                            pass
                    except (EOFError, OSError):
                        pass
                self.used = time.monotonic()

    def health(self):
        return {"ok": True, "engine": self.args.engine, "voice": self.voice, "device": self.device, "state": self.state,
                "problem": self.problem, "needGb": self.need, "idleSeconds": self.args.idle}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--engine", default="voxcpm", choices=sorted(ENGINES))
    p.add_argument("--model", required=True, help="the model folder")
    p.add_argument("--voice", help="voxcpm, qwen: the folder with voice.wav and voice.txt (design-voice.py)")
    p.add_argument("--voices", default="it=if_sara,en=af_heart,ja=jf_alpha", help="kokoro: a preset voice per language")
    p.add_argument("--device", default="auto", choices=["auto", "cuda", "cpu"])
    p.add_argument("--idle", type=int, default=300, help="seconds of quiet before she frees the graphics card")
    p.add_argument("--need-gb", type=float, help="free graphics memory needed to start (the engine's own by default)")
    p.add_argument("--leave-gb", type=float, default=1.0, help="free memory under which a quiet voice makes room")
    p.add_argument("--load-timeout", type=int, default=180, help="seconds to wait for the model to load")
    p.add_argument("--wake", action="store_true", help="load the model at start instead of when first needed")
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=8177)
    args = p.parse_args()
    if args.engine != "kokoro" and not args.voice:
        raise SystemExit(f"speak-server: --voice is needed with {args.engine} (make one with design-voice.py)")

    voice = Voice(args)
    if args.wake:
        try:
            voice.wake()
        except Busy as e:
            print(f"speak-server: not loading now, {e}", flush=True)
    print(f"speak-server: {args.engine} ({voice.voice or 'preset voices'}), http://{args.host}:{args.port}, "
          f"loads when needed and sleeps after {args.idle} s of quiet", flush=True)

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"  # chunked replies, for streaming

        def reply(self, code, body):
            data = json.dumps(body, ensure_ascii=False).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            if self.path.split("?")[0] == "/health":
                return self.reply(200, voice.health())
            self.reply(404, {"error": "not found"})

        def do_POST(self):
            path = self.path.split("?")[0]
            try:
                body = json.loads(self.rfile.read(int(self.headers.get("Content-Length") or 0)) or b"{}")
            except ValueError:
                return self.reply(400, {"error": "send {\"text\": ..., \"language\": ...} as JSON"})
            if path == "/wake":
                try:
                    return self.reply(200, {"state": voice.wake()})
                except Busy as e:
                    return self.reply(503, {"error": str(e), "busy": True})
            if path == "/sleep":
                # {"quietFor": 60}: only when she has said nothing for a minute (a model wants the card).
                quiet = float(body.get("quietFor") or 0)
                if voice.turn.locked() or time.monotonic() - voice.used < quiet:
                    return self.reply(200, {"state": voice.state})
                voice.sleep("a model needs the graphics card" if quiet else "asked")
                return self.reply(200, {"state": voice.state})
            if path != "/speak":
                return self.reply(404, {"error": "not found"})
            text = str(body.get("text") or "").strip()
            language = body.get("language") if body.get("language") in NAMES else "it"
            if not text or len(text) > MAX_CHARS:
                return self.reply(400, {"error": f"give her between 1 and {MAX_CHARS} characters to say"})
            started = time.monotonic()
            pieces = voice.say(text, language, bool(body.get("stream")))
            try:
                first = next(pieces, None)
            except Busy as e:
                return self.reply(503, {"error": str(e), "busy": True})
            except Exception as e:  # noqa: BLE001 - told to the page, which stays quiet
                print(f"speak-server: failed on {len(text)} characters: {e}", file=sys.stderr, flush=True)
                return self.reply(500, {"error": f"her voice failed: {e}"})
            if first is None:
                return self.reply(500, {"error": "her voice said nothing"})
            if body.get("stream"):
                return self.stream(first, pieces)
            pcm, sr = first
            out = io.BytesIO()
            sf.write(out, np.frombuffer(pcm, dtype="<i2"), sr, format="WAV", subtype="PCM_16")
            data = out.getvalue()
            self.send_response(200)
            self.send_header("Content-Type", "audio/wav")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("X-Seconds", f"{time.monotonic() - started:.2f}")
            self.end_headers()
            self.wfile.write(data)

        def stream(self, first, pieces):
            self.send_response(200)
            self.send_header("Content-Type", "audio/pcm")
            self.send_header("X-Sample-Rate", str(first[1]))
            self.send_header("Transfer-Encoding", "chunked")
            self.end_headers()
            try:
                piece = first
                while piece is not None:
                    if piece[0]:
                        self.wfile.write(f"{len(piece[0]):x}\r\n".encode() + piece[0] + b"\r\n")
                        self.wfile.flush()
                    piece = next(pieces, None)
                self.wfile.write(b"0\r\n\r\n")
            except (BrokenPipeError, ConnectionResetError):
                pass  # stopped: closing the generator tells the engine
            except Exception as e:  # noqa: BLE001 - the headers are gone: end the reply short
                print(f"speak-server: failed while speaking: {e}", file=sys.stderr, flush=True)
                self.close_connection = True
            finally:
                pieces.close()

        def log_message(self, *_):
            pass

    ThreadingHTTPServer((args.host, args.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
