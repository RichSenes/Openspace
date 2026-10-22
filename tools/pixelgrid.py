#!/usr/bin/env python3
"""Turns upscaled pixel-art pictures into a sprite of the local fan pack.

A picture of pixel art blown up (each pixel drawn as a square of, say, 12 screen
pixels, maybe saved as JPEG) is sampled back into its pixel grid: the size of the
squares is found from the colour edges, every square gives one pixel, the background
becomes transparent and near colours are merged. The result replaces the drawing of
an existing prop, only on this machine: it is written to the fan pack folder, which
git ignores (see docs/art-style.md).

    python3 tools/pixelgrid.py dragonLamp picture.jpg
    python3 tools/pixelgrid.py blackCat frame1.png frame2.png --colors 6
    python3 tools/pixelgrid.py blackCat animation.gif
    python3 tools/pixelgrid.py dragonLamp picture.jpg --light 52,14,30,flame.mid

Requires Pillow. Several pictures, or the frames of an animated GIF, become animation
frames and must share the grid. Transparent pixels, or the colour of the top left
corner in pictures without transparency, are the background.
"""
import argparse
import math
import os
import sys

from PIL import Image, ImageSequence

FAN_PACK = os.path.join(os.path.dirname(__file__), '..', 'src', 'frontend', 'src', 'art', 'fanpack')
LETTERS = 'abcdefghijklmnpqrstuvwxyzABCDEFGHIJKLMNPQRSTUVWXYZ'


def distance(a, b):
    return sum(abs(x - y) for x, y in zip(a, b))


def edges(px, w, h, axis):
    found = []
    if axis == 0:
        for y in range(0, h, max(1, h // 120)):
            found += [x for x in range(1, w) if distance(px[x, y], px[x - 1, y]) > 80]
    else:
        for x in range(0, w, max(1, w // 120)):
            found += [y for y in range(1, h) if distance(px[x, y], px[x, y - 1]) > 80]
    return found


def grid_of(found, low=3.0, high=40.0):
    """
    The square size and offset that put the colour edges on grid lines. For each size,
    edges become angles around a circle of that period: they line up when the size is
    right. Halves of the right size line up too, so the largest well aligned size wins.
    """
    found = found[:: max(1, len(found) // 1500)]

    def score(size):
        c = sum(math.cos(2 * math.pi * e / size) for e in found) / len(found)
        s = sum(math.sin(2 * math.pi * e / size) for e in found) / len(found)
        return math.hypot(c, s), size, (math.atan2(s, c) / (2 * math.pi)) * size % size

    scores = []
    size = low
    while size <= high:
        scores.append(score(size))
        size += 0.01 if size < 16 else 0.05
    top = max(entry[0] for entry in scores)
    rough = max((entry for entry in scores if entry[0] >= 0.9 * top), key=lambda entry: entry[1])[1]
    # Then a finer look around it: a small error adds up across a wide picture.
    _, size, offset = max(score(rough + step / 1000) for step in range(-60, 61))
    return size, offset


def sample(image, size_x, size_y, offset_x, offset_y):
    px = image.load()
    w, h = image.size
    background = px[0, 0]
    rows = []
    for r in range(int((h - offset_y) // size_y)):
        line = []
        for c in range(int((w - offset_x) // size_x)):
            cx, cy = offset_x + (c + 0.5) * size_x, offset_y + (r + 0.5) * size_y
            spots = [px[min(w - 1, int(cx + dx)), min(h - 1, int(cy + dy))] for dx in (-1, 0, 1) for dy in (-1, 0, 1)]
            if sum(s[3] < 128 for s in spots) > len(spots) // 2:
                line.append(None)
                continue
            color = tuple(sum(s[i] for s in spots) // len(spots) for i in range(3))
            line.append(BACKGROUND if background[3] >= 128 and distance(color, background[:3]) < 30 else color)
        rows.append(line)
    return outside_cleared(rows, background[:3])


BACKGROUND = 'background'


def outside_cleared(rows, background):
    """
    Background around the figure becomes transparent; background enclosed by it (the
    black eyes of a white dog on a black picture) keeps its colour.
    """
    height, width = len(rows), len(rows[0])
    stack = [(x, y) for x in range(width) for y in (0, height - 1)] + [(x, y) for y in range(height) for x in (0, width - 1)]
    while stack:
        x, y = stack.pop()
        if 0 <= x < width and 0 <= y < height and rows[y][x] == BACKGROUND:
            rows[y][x] = None
            stack += [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
    return [[background if c == BACKGROUND else c for c in line] for line in rows]


def crop(frames):
    height, width = len(frames[0]), len(frames[0][0])
    used_rows = [r for r in range(height) if any(f[r][c] for f in frames for c in range(width))]
    used_cols = [c for c in range(width) if any(f[r][c] for f in frames for r in range(height))]
    return [[line[used_cols[0]:used_cols[-1] + 1] for line in f[used_rows[0]:used_rows[-1] + 1]] for f in frames]


def palette_of(frames, count):
    cells = [c[:3] for f in frames for line in f for c in line if c]
    strip = Image.new('RGB', (len(cells), 1))
    strip.putdata(cells)
    quantized = strip.quantize(colors=count, method=Image.Quantize.MEDIANCUT)
    flat = quantized.getpalette()[:count * 3]
    colors = [tuple(flat[i * 3:i * 3 + 3]) for i in range(count)]
    # Merge colours too close to tell apart (JPEG noise around black, mostly).
    merged = []
    for color in colors:
        if not any(distance(color, kept) < 24 for kept in merged):
            merged.append(color)
    return merged


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('prop', help='name of the prop to redraw, as in PROPS (e.g. dragonLamp)')
    parser.add_argument('pictures', nargs='+')
    parser.add_argument('--colors', type=int, default=24, help='most colours to keep (default 24)')
    parser.add_argument('--cell', type=float, help='size of one pixel in the picture, if guessing fails')
    parser.add_argument('--light', action='append', default=[], help='x,y,radius,palette name of a light it gives off')
    parser.add_argument('--hint', help='its line on hover; the original prop line is kept otherwise')
    args = parser.parse_args()

    images = [frame.convert('RGBA') for p in args.pictures for frame in ImageSequence.Iterator(Image.open(p))]
    first = images[0].convert('RGB').load()
    w, h = images[0].size
    size_x, offset_x = grid_of(edges(first, w, h, 0))
    size_y, offset_y = grid_of(edges(first, w, h, 1))
    if args.cell:
        size_x = size_y = args.cell
        offset_x = offset_y = 0
    frames = crop([sample(image, size_x, size_y, offset_x, offset_y) for image in images])
    colors = palette_of(frames, args.colors)
    nearest = lambda c: min(range(len(colors)), key=lambda i: distance(c, colors[i]))
    rows = [[''.join(LETTERS[nearest(c)] if c else '.' for c in line) for line in f] for f in frames]

    key = lambda i: f'fan.{args.prop}.{LETTERS[i]}'
    lights = []
    for light in args.light:
        x, y, radius, color = light.split(',')
        lights.append(f"{{ x: {int(x)}, y: {int(y)}, radius: {int(radius)}, color: '{color}' }}")
    lines = [
        '// Local fan pack, written by tools/pixelgrid.py. Never committed: see docs/art-style.md.',
        "import type { FanModule } from '../fanPack'",
        '',
        'const fan: FanModule = {',
        '  colors: {',
        *[f"    '{key(i)}': '#{c[0]:02x}{c[1]:02x}{c[2]:02x}'," for i, c in enumerate(colors)],
        '  },',
        '  props: {',
        f'    {args.prop}: {{',
        '      sprite: {',
        f"        name: '{args.prop}',",
        f'        width: {len(rows[0][0])},',
        f'        height: {len(rows[0])},',
        '        legend: { ' + ', '.join(f"{LETTERS[i]}: '{key(i)}'" for i in range(len(colors))) + ' },',
        '        frames: [',
        *[line for f in rows for line in (['          ['] + [f"            '{r}'," for r in f] + ['          ],'])],
        '        ],',
        '      },',
        *([f"      hint: '{args.hint}'," ] if args.hint else []),
        *([f"      lights: [{', '.join(lights)}],"] if lights else []),
        '    },',
        '  },',
        '}',
        '',
        'export default fan',
        '',
    ]
    os.makedirs(FAN_PACK, exist_ok=True)
    target = os.path.join(FAN_PACK, f'{args.prop}.ts')
    with open(target, 'w') as out:
        out.write('\n'.join(lines))
    print(f'{target}: {len(rows[0][0])}x{len(rows[0])} pixels, {len(colors)} colours, {len(rows)} frame(s), one pixel = {size_x:.3f} x {size_y:.3f}')


if __name__ == '__main__':
    sys.exit(main())
