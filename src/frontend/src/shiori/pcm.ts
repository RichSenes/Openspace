/**
 * Her voice arrives as raw 16-bit little-endian mono PCM, in network pieces that can split a
 * sample in two: `push` turns each piece into samples between -1 and 1, keeping a half sample
 * for the next piece.
 */
export class Pcm {
  private half: number | null = null

  push(bytes: Uint8Array): Float32Array {
    const all = this.half === null ? bytes : new Uint8Array([this.half, ...bytes])
    const whole = all.length - (all.length % 2)
    this.half = whole < all.length ? all[whole] : null
    const out = new Float32Array(whole / 2)
    const view = new DataView(all.buffer, all.byteOffset, whole)
    for (let i = 0; i < out.length; i++) out[i] = view.getInt16(i * 2, true) / 32768
    return out
  }
}

/** Joins sample pieces into one, so a few hundred milliseconds play as one buffer. */
export function join(pieces: Float32Array[]): Float32Array {
  const out = new Float32Array(pieces.reduce((n, p) => n + p.length, 0))
  let at = 0
  for (const p of pieces) {
    out.set(p, at)
    at += p.length
  }
  return out
}
