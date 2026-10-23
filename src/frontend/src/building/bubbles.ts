/** A speech bubble to place: its size, and the point above a desk its tail points at. */
export interface BubbleBox {
  deskId: string
  anchorX: number
  anchorY: number
  width: number
  height: number
  /** Waiting agents first: their bubbles stay closest to their desk. */
  urgent: boolean
}

/** A bubble placed in the world: its top left corner. */
export interface PlacedBubble extends BubbleBox {
  left: number
  top: number
}

/** Pixels between a bubble's bottom and the point it points at, when nothing is in the way. */
const TAIL = 4

const overlaps = (a: PlacedBubble, b: PlacedBubble, gap: number) =>
  a.left < b.left + b.width + gap && b.left < a.left + a.width + gap && a.top < b.top + b.height + gap && b.top < a.top + a.height + gap

/**
 * Places bubbles above their desks so none covers another: a bubble that would overlap
 * one already placed climbs above it, its tail growing to still reach its desk.
 */
export function placeBubbles(boxes: readonly BubbleBox[], gap = 2): PlacedBubble[] {
  const order = [...boxes].sort((a, b) => Number(b.urgent) - Number(a.urgent) || b.anchorY - a.anchorY || a.anchorX - b.anchorX)
  const placed: PlacedBubble[] = []
  for (const box of order) {
    const bubble = { ...box, left: Math.round(box.anchorX - box.width / 2), top: box.anchorY - TAIL - box.height }
    for (let tries = 0; tries < boxes.length; tries++) {
      const blocking = placed.filter((p) => overlaps(bubble, p, gap))
      if (blocking.length === 0) break
      bubble.top = Math.min(...blocking.map((p) => p.top)) - box.height - gap
    }
    placed.push(bubble)
  }
  return placed
}

/** A bubble's words, cut at a word with an ellipsis when they would not fit a few lines. */
export function bubbleText(text: string, max = 90): string {
  const line = text.replace(/\s+/g, ' ').trim()
  if (line.length <= max) return line
  const cut = line.slice(0, max - 1)
  const space = cut.lastIndexOf(' ')
  return `${(space > max / 2 ? cut.slice(0, space) : cut).trimEnd()}…`
}
