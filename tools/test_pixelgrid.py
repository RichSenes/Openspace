"""Tests of pixelgrid.py on made-up pictures. Run: python3 -m unittest discover tools"""
import unittest

from PIL import Image

import pixelgrid as P

WHITE, BLACK, RED = (255, 255, 255), (0, 0, 0), (200, 40, 40)


def blown_up(rows, cell, colors, background=(248, 248, 248), margin=2):
    """Draws a tiny pixel-art picture with each pixel as a square of `cell` screen pixels."""
    w, h = len(rows[0]), len(rows)
    image = Image.new('RGBA', ((w + 2 * margin) * cell, (h + 2 * margin) * cell), background + (255,))
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch != '.':
                for dy in range(cell):
                    for dx in range(cell):
                        image.putpixel(((x + margin) * cell + dx, (y + margin) * cell + dy), colors[ch] + (255,))
    return image


FACE = [
    'ooooooo',
    'oWWWWWo',
    'oWoWoWo',
    'oWWWWWo',
    'oWoooWo',
    'oWWWWWo',
    'ooooooo',
]


class GridTests(unittest.TestCase):
    def test_finds_the_size_of_a_pixel(self):
        image = blown_up(FACE, 9, {'o': BLACK, 'W': WHITE})
        px = image.convert('RGB').load()
        size, _ = P.grid_of(P.edges(px, *image.size, 0))
        self.assertAlmostEqual(size, 9, delta=0.05)

    def test_samples_the_grid_back_and_keeps_enclosed_eyes(self):
        image = blown_up(FACE, 8, {'o': BLACK, 'W': WHITE}, background=BLACK)
        rows = P.crop([P.sample(image, 8, 8, 0, 0)])[0]
        # The black outline melts into the black background; the face is what is left.
        self.assertEqual(len(rows), 5)
        self.assertEqual(rows[1][1], BLACK, 'the eye inside the face stays')
        self.assertEqual(rows[0][0], WHITE)

    def test_clears_a_big_hole_but_keeps_small_ones(self):
        ring = ['ooooooooo'] + ['o.......o'] * 5 + ['ooooooooo']
        image = blown_up([r.replace('.', 'b') for r in ring], 6, {'o': RED, 'b': (248, 248, 248)})
        rows = P.crop([P.sample(image, 6, 6, 0, 0)])[0]
        self.assertIsNone(rows[3][4], 'the inside of a ring is background')

    def test_keeps_the_biggest_figure_only(self):
        rows = [[RED, None, None, None], [None, None, RED, RED], [None, None, RED, RED]]
        kept = P.largest_only([rows])[0]
        self.assertIsNone(kept[0][0])
        self.assertEqual(kept[2][3], RED)

    def test_outline_surrounds_the_figure(self):
        rows = [[WHITE]]
        out = P.outlined([rows])[0]
        self.assertEqual(out[0][1], BLACK)
        self.assertEqual(out[1][1], WHITE)

    def test_shrinks_and_keeps_the_outline(self):
        big = [[0 if x in (0, 7) or y in (0, 7) else 1 for x in range(8)] for y in range(8)]
        small = P.shrink([big], [BLACK, RED], 4)[0]
        self.assertEqual(len(small), 4)
        self.assertEqual(small[0][0], 0)
        self.assertEqual(small[2][2], 1)


if __name__ == '__main__':
    unittest.main()
