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
opens a conversation, with a soft rising chime. From then on every sentence is a message, without
her name, until a goodbye said alone ("basta così", "grazie Shiori", "that's all", "ありがとう"),
a minute of quiet, or **Stop talking**; a falling chime says she stopped listening. Her name
is matched by `wake.py` (also "hey Shiori", "ねえ、しおり"); only Italian, English and Japanese
are considered, so a short sentence is not taken for another language.

## Speaking (text to speech)

Not yet: the voice that answers is being chosen by listening tests in the three languages.
