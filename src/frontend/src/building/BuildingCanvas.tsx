import { useEffect, useRef } from 'react'
import { Application, Graphics } from 'pixi.js'

const TILE = 32
const LIGHT_TILE = 0x2a2d38
const DARK_TILE = 0x252833

/**
 * Hosts the PixiJS stage that draws the building.
 * For now it only lays an empty checkered floor: rooms and desks come next.
 */
export function BuildingCanvas() {
  const host = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const app = new Application()
    let disposed = false

    void app
      .init({ resizeTo: window, background: DARK_TILE, antialias: false, roundPixels: true })
      .then(() => {
        if (disposed) {
          app.destroy(true)
          return
        }
        host.current?.appendChild(app.canvas)
        app.stage.addChild(drawFloor(app.screen.width, app.screen.height))
      })

    return () => {
      disposed = true
      if (app.renderer) app.destroy(true)
    }
  }, [])

  return <div ref={host} style={{ width: '100%', height: '100%' }} />
}

function drawFloor(width: number, height: number): Graphics {
  const floor = new Graphics()
  for (let y = 0; y * TILE < height; y++) {
    for (let x = 0; x * TILE < width; x++) {
      floor.rect(x * TILE, y * TILE, TILE, TILE).fill((x + y) % 2 === 0 ? LIGHT_TILE : DARK_TILE)
    }
  }
  return floor
}
