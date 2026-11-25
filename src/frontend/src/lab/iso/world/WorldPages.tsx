import { Earth, PencilRuler } from 'lucide-react'

/**
 * The world's own pages, there before what they hold: editing the lab, and choosing the kind of
 * world it lives in. Each says what it will do and how the lab works meanwhile.
 */

export function LabEditPage() {
  return (
    <div className="empty world-soon">
      <PencilRuler size={40} aria-hidden className="world-soon-icon" />
      <h2>Edit the lab</h2>
      <p>Here you will add, remove and move things in the lab, and give each office a look of its own.</p>
      <p>For now the lab lays itself out from your offices: rooms grow with their desks, and each office has its own comfort corner.</p>
    </div>
  )
}

export function WorldsPage() {
  return (
    <div className="empty world-soon">
      <Earth size={40} aria-hidden className="world-soon-icon" />
      <h2>Worlds</h2>
      <ul className="world-kinds" aria-label="Kinds of world">
        <li aria-current="true">
          <strong>The lab in the city</strong>
          <span>In use</span>
        </li>
      </ul>
      <p>More kinds of world will come, each a different place for the same offices and the same people. You will pick one here.</p>
    </div>
  )
}
