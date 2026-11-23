# Shiori's voice

## Listening (speech to text)

`listen-server.py` turns what you say to Shiori into text, on this machine, with
[faster-whisper](https://github.com/SYSTRAN/faster-whisper) (MIT). Italian, English and Japanese
are detected on their own. Nothing you say leaves the computer.

```bash
python3 -m venv ~/.local/share/openspace/voice-venv
~/.local/share/openspace/voice-venv/bin/pip install faster-whisper
# ffmpeg must be on the PATH (sudo apt install ffmpeg)
```

`scripts/openspace start` starts it with the rest when its Python has faster-whisper, and skips
it otherwise. Tell it which Python and models in `~/.config/openspace/openspace.env`:

```bash
OPENSPACE_EARS_PYTHON=~/.local/share/openspace/voice-venv/bin/python
OPENSPACE_EARS_MODEL=small          # the model that hears requests (default medium)
OPENSPACE_EARS_WAKE_MODEL=base      # the light one that listens for her name (default base)
OPENSPACE_EARS_DEVICE=auto          # auto, cuda or cpu
OPENSPACE_EARS_MODELS=~/.local/share/openspace/whisper   # where models are downloaded
```

Or by hand: `python tools/voice/listen-server.py --model medium --wake-model base`.

- `--model`: `small` is quick; `medium` (default) understands better; `large-v3` is best and
  wants a graphics card.
- `--wake-model`: always listening runs every sentence said near the mic through this one, so it
  stays light; only a sentence that starts with "Shiori" goes on to `--model`. On a graphics card
  give it the same name as `--model` (`small`): one model, one pass, and her name is heard far
  more often than by `base`.
- `--log-heard`: prints how each sentence was heard, to see how your voice gets her name written.
- Graphics memory, int8 weights: `base` + `small` together take about 0.6 GB; `medium` about
  1.5 GB more. On a processor `small` takes a few seconds a sentence and `medium` several times
  that.
- `--download-root DIR`: where the model is kept (downloaded once, about 1.5 GB for medium).
- `--device auto|cuda|cpu`: `auto` tries the graphics card and falls back to the processor when
  it is busy or missing.
- `--port 8178`: Openspace looks there; change it with `Voice:ListenUrl` in the settings.

In the Shiori view, **hold the microphone button** (or the **space bar** when no field is
focused) and talk; let go and what you said is sent as your message. If something is already
typed, the spoken words are added to it instead, for you to send. **Esc** while holding drops it.

Or turn on the **ear** beside the mic and call her: "Shiori" (or "Shiori, cosa c'è oggi?")
opens a conversation, with a soft rising chime. From then on you just talk, without her name: what
you say goes to her as one message once you have been quiet for about two seconds, so a pause to
think does not cut you off. Her answers to spoken messages are short and spoken, and appear at
reading pace (**Show it all** skips ahead). A goodbye closes the conversation, with a falling
chime: "basta così, grazie", "no grazie, per ora va bene così", "apposto così", "ho finito",
"that's all", "以上です", "お疲れ様" — alone, or as the last sentence of what you said; a request
that only contains the words ("è tutto pronto?") does not. So do a minute of quiet and **Stop
talking**. Her name
is matched by `wake.py` (also "hey Shiori", "ねえ、しおり"); only Italian, English and Japanese
are considered, so a short sentence is not taken for another language.

## Speaking (text to speech)

`speak-server.py` gives Shiori her voice, on this machine. Her answers to what you said aloud are
spoken back a sentence at a time while she writes them; meanwhile she does not listen, so she
never hears herself. Holding the mic, **Esc** or **Quiet** under her words stops her.

Two engines, both Apache-2.0:

- `qwen`, [Qwen3-TTS](https://github.com/QwenLM/Qwen3-TTS) Base 1.7B: clones one voice and makes
  it speak Italian, English and Japanese, so she sounds the same in all three. Wants a graphics
  card (about 4–5 GB).
- `kokoro`, [Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M): tiny and quick, also on a
  processor, but each language has its own preset voice.

Her voice is designed once from a description with `design-voice.py` (Qwen3-TTS VoiceDesign),
which writes a folder with a clip of it; the server clones that clip for every sentence:

```bash
python3 -m venv ~/.local/share/openspace/tts-venv
~/.local/share/openspace/tts-venv/bin/pip install qwen-tts soundfile   # or: kokoro misaki[ja]
~/.local/share/openspace/tts-venv/bin/python tools/voice/design-voice.py \
  --model DIR/Qwen3-TTS-12Hz-1.7B-VoiceDesign --out ~/.local/share/openspace/voices/shiori \
  --describe "A young woman in her late twenties, warm and calm, soft mid-low pitch, gentle"
```

Then in `~/.config/openspace/openspace.env`, and `scripts/openspace start` starts it with the rest
(it is skipped while `OPENSPACE_VOICE_MODEL` is empty):

```bash
OPENSPACE_VOICE_PYTHON=~/.local/share/openspace/tts-venv/bin/python
OPENSPACE_VOICE_ENGINE=qwen                       # or kokoro
OPENSPACE_VOICE_MODEL=DIR/Qwen3-TTS-12Hz-1.7B-Base # kokoro: the Kokoro-82M folder
OPENSPACE_VOICE_DIR=~/.local/share/openspace/voices/shiori   # qwen only
```

`POST /speak {"text": "...", "language": "it"}` returns WAV; Openspace reaches it through
`/api/voice/speak` (`Voice:SpeakUrl`, port 8177).
