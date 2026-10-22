/**
 * A sprite written as rows of characters, one per pixel. '.' is transparent; every
 * other character is looked up in the legend, which names a colour of a palette.
 */
export interface SpriteDef {
  name: string
  width: number
  height: number
  legend: Readonly<Record<string, string>>
  /** Animation frames; each is `height` strings of `width` characters. */
  frames: readonly (readonly string[])[]
}

export interface Pixel {
  x: number
  y: number
  color: string
}

/** Turns a palette name into a colour; null leaves the pixel transparent. */
export type Resolve = (name: string) => string | null

/** Everything wrong with a sprite's shape; empty when it is well formed. */
export function problemsOf(sprite: SpriteDef): string[] {
  const problems: string[] = []
  if (sprite.frames.length === 0) problems.push(`${sprite.name}: no frames`)
  sprite.frames.forEach((frame, f) => {
    if (frame.length !== sprite.height) problems.push(`${sprite.name} frame ${f}: ${frame.length} rows, expected ${sprite.height}`)
    frame.forEach((row, y) => {
      if (row.length !== sprite.width) problems.push(`${sprite.name} frame ${f} row ${y}: ${row.length} pixels, expected ${sprite.width}`)
      for (const ch of row) {
        if (ch !== '.' && !(ch in sprite.legend)) problems.push(`${sprite.name} frame ${f} row ${y}: '${ch}' is not in the legend`)
      }
    })
  })
  return problems
}

/** The coloured pixels of one frame, placed at (dx, dy). */
export function pixelsOf(sprite: SpriteDef, frame: number, resolve: Resolve, dx = 0, dy = 0): Pixel[] {
  const rows = sprite.frames[frame % sprite.frames.length]
  const pixels: Pixel[] = []
  rows.forEach((row, y) => {
    for (let x = 0; x < row.length; x++) {
      const ch = row[x]
      if (ch === '.') continue
      const color = resolve(sprite.legend[ch])
      if (color) pixels.push({ x: dx + x, y: dy + y, color })
    }
  })
  return pixels
}
