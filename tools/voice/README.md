# Shiori's voice

## Listening (speech to text)

`listen-server.py` turns what you say to Shiori into text, on this machine, with
[faster-whisper](https://github.com/SYSTRAN/faster-whisper) (MIT). Italian, English and Japanese
are detected on their own. Nothing you say leaves the computer.

```bash
python3 -m venv ~/.local/share/openspace/voice-venv
~/.local/share/openspace/voice-venv/bin/pip install faster-whisper
# ffmpeg must be on the PATH (sudo apt install ffmpeg)
~/.local/share/openspace/voice-venv/bin/python tools/voice/listen-server.py --model medium
```

- `--model`: `small` is quick on a processor; `medium` (default) understands better;
  `large-v3` is best and wants a graphics card.
- `--download-root DIR`: where the model is kept (downloaded once, about 1.5 GB for medium).
- `--device auto|cuda|cpu`: `auto` tries the graphics card and falls back to the processor when
  it is busy or missing.
- `--port 8178`: Openspace looks there; change it with `Voice:ListenUrl` in the settings.

In the Shiori view, **hold the microphone button** (or the **space bar** when no field is
focused) and talk; let go and what you said is sent as your message. If something is already
typed, the spoken words are added to it instead, for you to send. **Esc** while holding drops it.

## Speaking (text to speech)

Not yet: the voice that answers is being chosen by listening tests in the three languages.
