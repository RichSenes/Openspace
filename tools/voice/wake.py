"""Hearing Shiori's name at the start of what was said, in the three languages she speaks."""
import re

# How the listener writes her name, Japanese included; small models stumble on the sounds.
NAMES = ["shiori", "shiory", "shori", "siori", "sciori", "shioli", "shiouri", "chiori", "shi ori", "しおり", "シオリ", "栞"]
# What may come before the name: "hey Shiori", "ok Shiori", "senti, Shiori", "ねえ、しおり".
FILLERS = {"hey", "hei", "ehi", "ei", "e", "eh", "ok", "okay", "oh", "oi", "allora", "senti", "scusa", "ciao", "hi", "hello", "ねえ", "ね"}
_NAME = re.compile(r"^(" + "|".join(re.escape(n) for n in sorted(NAMES, key=len, reverse=True)) + r")(?![a-z])(さん|ちゃん)?")
_EDGE = " \t,.!?;:、。！？…-—'\"「」"


def called(text: str) -> tuple[bool, str]:
    """Whether the words start by calling Shiori, and what was asked after her name."""
    said = text.strip()
    rest = said.lower()
    for _ in range(3):
        rest = rest.lstrip(_EDGE)
        match = _NAME.match(rest)
        if match:
            # Only the start was cut, so the tail lines up with the original and keeps its case.
            tail = rest[match.end():]
            return True, said[len(said) - len(tail):].lstrip(_EDGE).rstrip(" ,") if tail.strip(_EDGE) else ""
        word = re.match(r"^[^\s,.!?、。]+", rest)
        if not word or word.group(0) not in FILLERS:
            return False, ""
        rest = rest[word.end():]
    return False, ""
