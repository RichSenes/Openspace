import { useEffect, useRef } from 'react'
import { Application, Container, Graphics, Text } from 'pixi.js'
import type { OfficeView } from '../api/types'
import { bestLayout, TILE, type BuildingLayout } from './layout'

const COLORS = {
  ground: 0x14161c,
  floorLight: 0x2a2d38,
  floorDark: 0x252833,
  wall: 0x4a5163,
  wallTop: 0x5d6579,
  desk: 0x8a6a4a,
  deskEdge: 0x6b5139,
  screenOff: 0x1b1e26,
  screenOn: 0x8fd282,
  label: 0xece8df,
  mute: 0xa3a8b6,
}

interface Props {
  office: OfficeView
  /** Desks with a running task: their screens are lit. */
  busyDeskIds: ReadonlySet<string>
}

/** Draws the selected office as a pixel-art floor plan. Sprites will replace the placeholder shapes. */
export function BuildingCanvas({ office, busyDeskIds }: Props) {
  const host = useRef<HTMLDivElement>(null)
  const appRef = useRef<Application | null>(null)
  const draw = useRef<() => void>(() => {})

  // Refreshed after every render so the canvas callbacks always see the latest props.
  useEffect(() => {
    draw.current = () => {
      const app = appRef.current
      if (!app) return
      app.stage.removeChildren().forEach((child) => child.destroy({ children: true }))
      const margin = 32
      const { layout, scale } = bestLayout(office, {
        width: Math.max(1, app.screen.width - margin),
        height: Math.max(1, app.screen.height - margin),
      })
      const world = drawBuilding(layout, busyDeskIds)
      world.scale.set(scale)
      world.position.set(
        Math.round((app.screen.width - layout.width * TILE * scale) / 2),
        Math.round((app.screen.height - layout.height * TILE * scale) / 2),
      )
      app.stage.addChild(world)
    }
  })

  useEffect(() => {
    const element = host.current
    if (!element) return
    const app = new Application()
    let disposed = false

    void Promise.all([
      app.init({ resizeTo: element, background: COLORS.ground, antialias: false, roundPixels: true }),
      document.fonts.load('16px Silkscreen'),
    ]).then(() => {
      if (disposed) {
        app.destroy(true, { children: true })
        return
      }
      element.appendChild(app.canvas)
      appRef.current = app
      app.renderer.on('resize', () => draw.current())
      draw.current()
    })

    return () => {
      disposed = true
      if (appRef.current) {
        appRef.current.destroy(true, { children: true })
        appRef.current = null
      }
    }
  }, [])

  useEffect(() => draw.current(), [office, busyDeskIds])

  return <div ref={host} className="building-canvas" />
}

function drawBuilding(layout: BuildingLayout, busy: ReadonlySet<string>): Container {
  const world = new Container()

  for (const room of layout.rooms) {
    const g = new Graphics()
    const px = room.x * TILE
    const py = room.y * TILE

    // Walls, then a checkered floor inside them.
    g.rect(px, py, room.width * TILE, room.height * TILE).fill(COLORS.wall)
    g.rect(px, py, room.width * TILE, 4).fill(COLORS.wallTop)
    for (let ty = 2; ty < room.height - 1; ty++) {
      for (let tx = 1; tx < room.width - 1; tx++) {
        g.rect(px + tx * TILE, py + ty * TILE, TILE, TILE).fill((tx + ty) % 2 === 0 ? COLORS.floorLight : COLORS.floorDark)
      }
    }

    for (const desk of room.desks) {
      const dx = desk.x * TILE
      const dy = desk.y * TILE
      g.rect(dx - 4, dy + 10, 2 * TILE + 8, TILE - 6).fill(COLORS.deskEdge)
      g.rect(dx - 4, dy + 8, 2 * TILE + 8, TILE - 8).fill(COLORS.desk)
      g.rect(dx + 6, dy - 6, 20, 14).fill(COLORS.deskEdge)
      g.rect(dx + 8, dy - 4, 16, 10).fill(busy.has(desk.id) ? COLORS.screenOn : COLORS.screenOff)
    }
    world.addChild(g)

    world.addChild(label(room.name, px + TILE, py + TILE + 2, COLORS.label, 8))
    for (const desk of room.desks) {
      world.addChild(label(desk.name, desk.x * TILE + TILE, desk.y * TILE + TILE + 8, COLORS.mute, 8, true))
    }
  }

  return world
}

function label(text: string, x: number, y: number, fill: number, size: number, centered = false): Text {
  const t = new Text({ text, style: { fontFamily: 'Silkscreen', fontSize: size, fill } })
  t.resolution = 4
  if (centered) t.anchor.set(0.5, 0)
  t.position.set(x, y)
  return t
}
