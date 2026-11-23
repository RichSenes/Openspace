#!/usr/bin/env python3
"""Designs Shiori's voice from a description, once, with Qwen3-TTS VoiceDesign (Apache-2.0).

The result is a folder with a short clip of the voice (voice.wav), what it says (voice.txt) and
the description (about.txt). speak-server.py --engine qwen --voice FOLDER clones it for every
sentence and every language, so she always sounds the same.

    python3 design-voice.py --model DIR --out voices/shiori \\
        --describe "A young woman in her late twenties, warm and calm, soft mid-low pitch..." \\
        [--language English] [--text "The sentence the clip says."] [--seed 7]

Run it a few times with different seeds or words and keep the one you like.
"""
import argparse
from pathlib import Path

import soundfile as sf
import torch

SAMPLE = {
    "English": "Hi, I'm Shiori. I keep track of everything we decide, and I'm happy to think it through with you.",
    "Italian": "Ciao, sono Shiori. Tengo il filo di tutto quello che decidiamo, e mi piace ragionarci insieme a te.",
    "Japanese": "こんにちは、栞です。決めたことはぜんぶ覚えています。一緒にゆっくり考えましょう。",
}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--model", required=True, help="the Qwen3-TTS VoiceDesign folder")
    p.add_argument("--out", required=True)
    p.add_argument("--describe", required=True, help="the voice, in words")
    p.add_argument("--language", default="English", choices=sorted(SAMPLE))
    p.add_argument("--text", help="what the clip says (a sample sentence by default)")
    p.add_argument("--seed", type=int, default=7)
    args = p.parse_args()

    from qwen_tts import Qwen3TTSModel

    torch.manual_seed(args.seed)
    device = "cuda:0" if torch.cuda.is_available() else "cpu"
    model = Qwen3TTSModel.from_pretrained(args.model, device_map=device, dtype=torch.bfloat16 if device != "cpu" else torch.float32)
    text = args.text or SAMPLE[args.language]
    wavs, sr = model.generate_voice_design(text=text, language=args.language, instruct=args.describe)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    sf.write(out / "voice.wav", wavs[0], sr)
    (out / "voice.txt").write_text(text + "\n", encoding="utf-8")
    (out / "about.txt").write_text(f"{args.describe}\n\nlanguage: {args.language}, seed: {args.seed}\n", encoding="utf-8")
    print(f"design-voice: {out} ({len(wavs[0]) / sr:.1f} s)")


if __name__ == "__main__":
    main()
