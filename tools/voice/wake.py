"""Hearing Shiori's name at the start of what was said, in the three languages she speaks."""
import re

# How the listener writes her name, Japanese included; small models stumble on the sounds.
NAMES = ["shiori", "shiory", "shori", "siori", "sciori", "shioli", "shiouri", "chiori", "shi ori", "しおり", "シオリ", "栞"]
# What may come before the name: "hey Shiori", "ok Shiori", "senti, Shiori", "ねえ、しおり".
FILLERS = {"hey", "hei", "ehi", "ei", "e", "eh", "ok", "okay", "oh", "oi", "allora", "senti", "scusa", "ciao", "hi", "hello", "ねえ", "ね"}
_NAME = re.compile(r"^(" + "|".join(re.escape(n) for n in sorted(NAMES, key=len, reverse=True)) + r")(?![a-z])(さん|ちゃん)?")
_EDGE = " \t,.!?;:、。！？…-—'\"「」"


def _distance(a: str, b: str) -> int:
    row = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        nxt = [i]
        for j, y in enumerate(b, 1):
            nxt.append(min(row[j] + 1, nxt[j - 1] + 1, row[j - 1] + (x != y)))
        row = nxt
    return row[-1]


def _sounds_like_her(word: str) -> bool:
    """A real voice in a real room gets her name written many ways: one letter off is her,
    two off only for a word of her length ("shuri", not "siri" or "shioriko")."""
    d = _distance(word, "shiori")
    return d <= 1 or (d == 2 and 5 <= len(word) <= 6)


def called(text: str) -> tuple[bool, str]:
    """Whether the words start by calling Shiori, and what was asked after her name."""
    said = text.strip()
    rest = said.lower()
    for _ in range(3):
        rest = rest.lstrip(_EDGE)
        match = _NAME.match(rest)
        word = re.match(r"^[a-zà-ÿ]+", rest)
        end = match.end() if match else word.end() if word and _sounds_like_her(word.group(0)) else None
        if end is not None:
            # Only the start was cut, so the tail lines up with the original and keeps its case.
            tail = rest[end:]
            return True, said[len(said) - len(tail):].lstrip(_EDGE).rstrip(" ,") if tail.strip(_EDGE) else ""
        word = re.match(r"^[^\s,.!?、。]+", rest)
        if not word or word.group(0) not in FILLERS:
            return False, ""
        rest = rest[word.end():]
    return False, ""


def choose_language(likely, prefer="it", languages=("it", "en", "ja"), sure=0.8):
    """Which of her languages a sentence is in, from whisper's (language, probability) guesses.

    Short sentences fool whisper: "come va?" comes out as English ("are you warm?"). The person's
    own language (`prefer`) wins unless another of hers is clearly likelier: at least `sure`
    probable and four times as likely as `prefer`.
    """
    odds = {language: p for language, p in likely if language in languages}
    if not odds:
        return prefer
    best = max(odds, key=odds.get)
    if best == prefer or prefer not in languages:
        return best
    mine = odds.get(prefer, 0.0)
    return best if odds[best] >= sure and odds[best] >= 4 * mine else prefer
