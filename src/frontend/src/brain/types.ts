/** The brain's notes as the API shows them (see WikiEndpoints.cs and BrainEndpoints.cs). */

export interface TreeNote {
  path: string
  /** The file's name, `.md` included. */
  name: string
  title: string
  kind: string | null
  status: string | null
  modified: string
}

/** A folder of the vault; the root has an empty name and path. */
export interface TreeFolder {
  name: string
  path: string
  folders: TreeFolder[]
  notes: TreeNote[]
}

export interface Note {
  path: string
  kind: string | null
  status: string | null
  title: string
  offices: string[]
  rooms: string[]
  paths: string[]
  /** "yyyy-mm-dd" */
  review: string | null
  supersedes: string | null
  from: string | null
  author: string | null
  links: string[]
  modified: string
  body: string
}

export interface Backlink {
  path: string
  title: string
  kind: string | null
  status: string | null
  modified: string
}

export interface NoteCommit {
  commit: string
  author: string
  date: string
  summary: string
}

export interface NoteVersion {
  commit: NoteCommit
  path: string
  text: string | null
  diff: string
  previous: string | null
  truncated: boolean
}

export interface SearchHit {
  path: string
  title: string
  kind: string | null
  status: string | null
  offices: string[]
  rooms: string[]
  paths: string[]
  review: string | null
  modified: string
  excerpt: string
}

/** An accepted decision explaining a file, from "why?path=". */
export interface WhyReason {
  path: string
  title: string
  decided: string
  offices: string[]
  rooms: string[]
  covering: string[]
  from: string | null
  review: string | null
  modified: string
  general: boolean
}

export interface GraphNote {
  path: string
  title: string
  kind: string | null
  status: string | null
  offices: string[]
  author: string | null
}

export interface GraphLink {
  from: string
  to: string
}

export interface Graph {
  notes: GraphNote[]
  links: GraphLink[]
}
