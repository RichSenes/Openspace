import { useEffect, useRef } from 'react'
import { drawCity, drawSky, makeCity, type City } from './city'

/** The city behind Shiori, drawn at the art's size and enlarged with square pixels. */
export function CityBackdrop({ pixel, calm }: { pixel: number; calm: boolean }) {
  const ref = useRef<HTMLCanvasElement>(null)
  const live = useRef({ pixel, calm })
  useEffect(() => {
    live.current = { pixel, calm }
  })

  useEffect(() => {
    const c = ref.current!
    const g = c.getContext('2d')!
    let city: City | null = null
    let sky: HTMLCanvasElement | null = null
    let raf = 0
    let lastDraw = 0
    const start = performance.now()
    const frame = (now: number) => {
      raf = requestAnimationFrame(frame)
      // Pixel art moves at its own pace: 12 frames a second.
      if (now - lastDraw < 1000 / 12) return
      lastDraw = now
      const px = live.current.pixel || 3
      const w = Math.max(40, Math.ceil(c.clientWidth / px))
      const h = Math.max(40, Math.ceil(c.clientHeight / px))
      if (!city || city.w !== w || city.h !== h) {
        c.width = w
        c.height = h
        city = makeCity(w, h)
        sky = document.createElement('canvas')
        sky.width = w
        sky.height = city.horizon
        drawSky(sky.getContext('2d')!, city)
      }
      g.imageSmoothingEnabled = false
      drawCity(g, city, sky!, (now - start) / 1000, live.current.calm)
    }
    raf = requestAnimationFrame(frame)
    return () => cancelAnimationFrame(raf)
  }, [])

  return <canvas ref={ref} className="pr-city" aria-hidden="true" />
}
