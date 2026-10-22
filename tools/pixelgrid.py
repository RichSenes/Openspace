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
    python3 tools/pixelgrid.py blackCat animation.gif --height 12
    python3 tools/pixelgrid.py dragonLamp picture.jpg --light 52,14,30,flame.mid

Requires Pillow. Several pictures, or the frames of an animated GIF, become animation
frames and must share the grid. Transparent pixels and whatever surrounds the figure,
even a gradient, are the background.
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
    # Sizes that fit well come in runs around each true multiple: take the run of the
    # largest size, and its best fit.
    good = [entry for entry in scores if entry[0] >= 0.9 * top]
    runs = [[good[0]]]
    for entry in good[1:]:
        if entry[1] - runs[-1][-1][1] <= 0.06:
            runs[-1].append(entry)
        else:
            runs.append([entry])
    rough = max(runs[-1], key=lambda entry: entry[0])[1]
    # Then a finer look around it: a small error adds up across a wide picture.
    _, size, offset = max(score(rough + step / 1000) for step in range(-60, 61))
    return size, offset


def sample(image, size_x, size_y, offset_x, offset_y):
    px = image.load()
    w, h = image.size
    rows = []
    for r in range(int((h - offset_y) // size_y)):
        line = []
        for c in range(int((w - offset_x) // size_x)):
            cx, cy = offset_x + (c + 0.5) * size_x, offset_y + (r + 0.5) * size_y
            spots = [px[min(w - 1, int(cx + dx)), min(h - 1, int(cy + dy))] for dx in (-1, 0, 1) for dy in (-1, 0, 1)]
            if sum(s[3] < 128 for s in spots) > len(spots) // 2:
                line.append(None)
            else:
                line.append(tuple(sum(s[i] for s in spots) // len(spots) for i in range(3)))
        rows.append(line)
    return background_cleared(rows)


def background_cleared(rows):
    """
    Clears the background around the figure: a flood from the borders that spreads
    while neighbours look alike, so it follows gradients and stops at outlines.
    Background-coloured pixels enclosed by the figure (the black eyes of a white dog on
    a black picture) keep their colour.
    """
    height, width = len(rows), len(rows[0])
    edge = [rows[y][x] for x in range(width) for y in (0, height - 1)] + [rows[y][x] for y in range(height) for x in (0, width - 1)]
    if any(c is None for c in edge):
        # A transparent picture: transparency is the background, and the figure may touch the borders.
        return rows
    # The background is the colour most of the border has (a figure may touch the border too),
    # with the shades of it a gradient brings.
    rounded = {}
    for c in edge:
        key = tuple(v // 16 for v in c)
        rounded[key] = rounded.get(key, 0) + 1
    common = max(rounded, key=rounded.get)
    mode = next(c for c in edge if tuple(v // 16 for v in c) == common)
    border = [c for c in edge[:: max(1, len(edge) // 80)] if distance(c, mode) < 120]
    seen = [[False] * width for _ in range(height)]
    stack = [(x, y, None) for x in range(width) for y in (0, height - 1)] + [(x, y, None) for y in range(height) for x in (0, width - 1)]
    while stack:
        x, y, came_from = stack.pop()
        if not (0 <= x < width and 0 <= y < height) or seen[y][x]:
            continue
        here = rows[y][x]
        # Spread to alike neighbours only, and never far from what the borders look like:
        # a gentle gradient is background, a figure shading away from it is not.
        if here is not None and (min(distance(here, b) for b in border) > 90 or (came_from is not None and distance(here, came_from) > 24)):
            continue
        seen[y][x] = True
        rows[y][x] = None
        step = here if here is not None else came_from
        stack += [(x + 1, y, step), (x - 1, y, step), (x, y + 1, step), (x, y - 1, step)]
    return holes_cleared(rows, border)


def holes_cleared(rows, border):
    """
    Clears holes in the figure that show the background (inside a ring, say), but keeps
    small ones: two black eyes on a black picture are part of the face.
    """
    height, width = len(rows), len(rows[0])
    looks_like = lambda c: c is not None and min(distance(c, b) for b in border) < 40
    seen = [[False] * width for _ in range(height)]
    for y in range(height):
        for x in range(width):
            if seen[y][x] or not looks_like(rows[y][x]):
                continue
            region, stack = [], [(x, y)]
            while stack:
                cx, cy = stack.pop()
                if 0 <= cx < width and 0 <= cy < height and not seen[cy][cx] and looks_like(rows[cy][cx]):
                    seen[cy][cx] = True
                    region.append((cx, cy))
                    stack += [(cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)]
            if len(region) > 6:
                for cx, cy in region:
                    rows[cy][cx] = None
    return rows


def emptied(rows, x, y):
    """Empties the area around (x, y) that looks alike: a hole the background shows through."""
    height, width = len(rows), len(rows[0])
    stack, start = [(x, y)], rows[y][x]
    while stack and start is not None:
        cx, cy = stack.pop()
        if 0 <= cx < width and 0 <= cy < height and rows[cy][cx] is not None and distance(rows[cy][cx], start) < 48:
            rows[cy][cx] = None
            stack += [(cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)]
    return rows


def largest_only(frames):
    """Keeps the biggest connected figure of each frame: drops signatures and captions."""
    result = []
    for rows in frames:
        height, width = len(rows), len(rows[0])
        label = [[0] * width for _ in range(height)]
        sizes = {}
        for y in range(height):
            for x in range(width):
                if rows[y][x] is None or label[y][x]:
                    continue
                n = len(sizes) + 1
                stack, count = [(x, y)], 0
                while stack:
                    cx, cy = stack.pop()
                    if 0 <= cx < width and 0 <= cy < height and rows[cy][cx] is not None and not label[cy][cx]:
                        label[cy][cx] = n
                        count += 1
                        stack += [(cx + dx, cy + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)]
                sizes[n] = count
        keep = max(sizes, key=sizes.get)
        result.append([[c if label[y][x] == keep else None for x, c in enumerate(line)] for y, line in enumerate(rows)])
    return result


def outlined(frames):
    """Adds a black line around the figure, for art drawn on a black background."""
    result = []
    for rows in frames:
        height, width = len(rows) + 2, len(rows[0]) + 2
        padded = [[None] * width] + [[None] + line + [None] for line in rows] + [[None] * width]
        out = [line[:] for line in padded]
        for y in range(height):
            for x in range(width):
                if padded[y][x] is None and any(
                    0 <= x + dx < width and 0 <= y + dy < height and padded[y + dy][x + dx] is not None
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))
                ):
                    out[y][x] = (0, 0, 0)
        result.append(out)
    return result


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


def shrink(frames, colors, height, thin=False):
    """
    Brings sprites down to a height in pixels, keeping them crisp: each new pixel takes
    the most common colour of the area it covers (outline counts a bit more, so lines
    survive), and the silhouette gets its outline back afterwards.
    """
    source_h, source_w = len(frames[0]), len(frames[0][0])
    factor = height / source_h
    width = max(1, round(source_w * factor))
    outline = min(range(len(colors)), key=lambda i: sum(colors[i]))
    result = []
    for frame in frames:
        rows = []
        for r in range(height):
            line = []
            y0, y1 = r / factor, (r + 1) / factor
            for c in range(width):
                x0, x1 = c / factor, (c + 1) / factor
                votes = {}
                for y in range(int(y0), min(source_h, math.ceil(y1))):
                    for x in range(int(x0), min(source_w, math.ceil(x1))):
                        weight = (min(x1, x + 1) - max(x0, x)) * (min(y1, y + 1) - max(y0, y))
                        key = frame[y][x]
                        if key is not None and key == outline:
                            weight *= 1.3
                        # Thin art (a blade, a ring) would vanish into the background otherwise.
                        if key is None and thin:
                            weight *= 0.35
                        votes[key] = votes.get(key, 0) + weight
                line.append(max(votes, key=votes.get))
            rows.append(line)
        for r in range(height):
            for c in range(width):
                if rows[r][c] is not None and any(
                    not (0 <= c + dx < width and 0 <= r + dy < height) or rows[r + dy][c + dx] is None
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))
                ):
                    rows[r][c] = outline
        result.append(rows)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('prop', help='name of the prop to redraw, as in PROPS (e.g. dragonLamp)')
    parser.add_argument('pictures', nargs='+')
    parser.add_argument('--colors', type=int, default=24, help='most colours to keep (default 24)')
    parser.add_argument('--cell', help='size of one pixel in the picture if guessing fails: 12.5, or 30.8x27.9 when stretched')
    parser.add_argument('--offset', default='0,0', help='with --cell: where the first column and row start, as x,y')
    parser.add_argument('--outline', action='store_true', help='draw a black outline around the figure')
    parser.add_argument('--light', action='append', default=[], help='x,y,radius,palette name of a light it gives off')
    parser.add_argument('--hole', action='append', default=[], help='x,y of a hole to empty (inside a ring, say), in the full-size grid')
    parser.add_argument('--largest', action='store_true', help='keep only the biggest figure (drops signatures and captions)')
    parser.add_argument('--height', type=int, help='shrink to this height in pixels, to match the other sprites')
    parser.add_argument('--thin', action='store_true', help='with --height: keep thin lines (blades, rings) from breaking')
    parser.add_argument('--hint', help='its line on hover; the original prop line is kept otherwise')
    args = parser.parse_args()

    images = [frame.convert('RGBA') for p in args.pictures for frame in ImageSequence.Iterator(Image.open(p))]
    first = images[0].convert('RGB').load()
    w, h = images[0].size
    size_x, offset_x = grid_of(edges(first, w, h, 0))
    size_y, offset_y = grid_of(edges(first, w, h, 1))
    if args.cell:
        size_x, size_y = (float(v) for v in (args.cell.split('x') * 2)[:2])
        offset_x, offset_y = (float(v) for v in args.offset.split(','))
    frames = [sample(image, size_x, size_y, offset_x, offset_y) for image in images]
    for hole in args.hole:
        hx, hy = (int(v) for v in hole.split(','))
        frames = [emptied(rows, hx, hy) for rows in frames]
    if args.largest:
        frames = largest_only(frames)
    if args.outline:
        frames = outlined(frames)
    frames = crop(frames)
    colors = palette_of(frames, args.colors)
    nearest = lambda c: min(range(len(colors)), key=lambda i: distance(c, colors[i]))
    indexed = [[[nearest(c) if c else None for c in line] for line in f] for f in frames]
    if args.height and args.height < len(indexed[0]):
        indexed = shrink(indexed, colors, args.height, args.thin)
    rows = [[''.join(LETTERS[i] if i is not None else '.' for i in line) for line in f] for f in indexed]

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
