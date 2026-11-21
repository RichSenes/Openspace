import { useEffect, useRef } from 'react'
import type { Scene, Weather, WorldId } from './types'
import { WORLDS } from './worlds'

/** The world behind Shiori, drawn at the art's size and enlarged with square pixels. */
export function WorldBackdrop({ world, pixel, calm, weather }: { world: WorldId; pixel: number; calm: boolean; weather: Weather }) {
  const ref = useRef<HTMLCanvasElement>(null)
  const live = useRef({ world, pixel, calm, weather })
  useEffect(() => {
    live.current = { world, pixel, calm, weather }
  })

  useEffect(() => {
    const c = ref.current!
    const g = c.getContext('2d')!
    let scene: Scene | null = null
    let sceneWorld: WorldId | null = null
    let raf = 0
    let lastDraw = 0
    const start = performance.now()
    const frame = (now: number) => {
      raf = requestAnimationFrame(frame)
      // Pixel art moves at its own pace: 12 frames a second.
      if (now - lastDraw < 1000 / 12) return
      lastDraw = now
      const { world: id, pixel: px0, calm: still, weather: wx } = live.current
      const px = px0 || 3
      const w = Math.max(40, Math.ceil(c.clientWidth / px))
      const h = Math.max(40, Math.ceil(c.clientHeight / px))
      if (!scene || scene.w !== w || scene.h !== h || sceneWorld !== id) {
        c.width = w
        c.height = h
        scene = WORLDS[id].create(w, h)
        sceneWorld = id
      }
      g.imageSmoothingEnabled = false
      scene.draw(g, (now - start) / 1000, still, wx)
    }
    raf = requestAnimationFrame(frame)
    return () => cancelAnimationFrame(raf)
  }, [])

  return <canvas ref={ref} className="pr-city" aria-hidden="true" />
}
