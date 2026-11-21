import { useEffect, useRef } from 'react'
import { NEUTRAL } from './expression'
import { drawPortrait, PORTRAIT_H, PORTRAIT_W } from './portrait'

/** A hologram preview: the portrait's brightness, tinted, with scan lines. Good enough to judge. */
function hologram(src: HTMLCanvasElement, dst: HTMLCanvasElement) {
  const g = dst.getContext('2d')!
  dst.width = src.width
  dst.height = src.height
  const img = src.getContext('2d')!.getImageData(0, 0, src.width, src.height)
  const d = img.data
  for (let i = 0; i < d.length; i += 4) {
    if (!d[i + 3]) continue
    const y = Math.floor(i / 4 / src.width)
    const l = (0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2]) / 255
    const scan = y % 2 ? 0.55 : 1
    const v = (0.18 + 0.95 * l) * scan
    d[i] = Math.min(255, 110 * v + 40 * v * v)
    d[i + 1] = Math.min(255, 130 * v + 90 * v * v)
    d[i + 2] = Math.min(255, 255 * v)
  }
  g.putImageData(img, 0, 0)
}

/** `?lab=portrait`: Shiori's portrait, still, in colour and as a hologram. */
export function PortraitLab() {
  const colour = useRef<HTMLCanvasElement>(null)
  const holo = useRef<HTMLCanvasElement>(null)
  useEffect(() => {
    const c = colour.current!
    c.width = PORTRAIT_W
    c.height = PORTRAIT_H
    drawPortrait(c.getContext('2d', { willReadFrequently: true })!, NEUTRAL, 0)
    hologram(c, holo.current!)
  }, [])
  const style = { width: PORTRAIT_W * 4, height: PORTRAIT_H * 4, imageRendering: 'pixelated' as const }
  return (
    <div style={{ minHeight: '100dvh', background: '#07060d', color: '#ecebff', display: 'flex', flexWrap: 'wrap', gap: 32, padding: 32, alignItems: 'flex-start', justifyContent: 'center' }}>
      <figure style={{ margin: 0 }}>
        <canvas ref={colour} style={style} />
        <figcaption style={{ textAlign: 'center', fontFamily: 'var(--mono)', fontSize: 12 }}>Colour · ×4</figcaption>
      </figure>
      <figure style={{ margin: 0 }}>
        <canvas ref={holo} style={style} />
        <figcaption style={{ textAlign: 'center', fontFamily: 'var(--mono)', fontSize: 12 }}>Hologram preview · ×4</figcaption>
      </figure>
    </div>
  )
}
