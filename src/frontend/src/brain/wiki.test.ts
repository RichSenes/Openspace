import { describe, expect, it } from 'vitest'
import type { TreeFolder } from './types'
import {
  countNotes,
  editRefusal,
  filterTree,
  flattenTree,
  foldersOf,
  isConstitution,
  kindsOf,
  linksIn,
  parseWikiLink,
  renderNote,
  resolveLink,
  reviewState,
  statusClass,
  topFolder,
  withoutTitle,
} from './wiki'

const note = (path: string, kind: string | null = null, status: string | null = null) => ({
  path,
  name: path.split('/').pop()!,
  title: path,
  kind,
  status,
  modified: '2026-11-20T10:00:00Z',
})

const tree: TreeFolder = {
  name: '',
  path: '',
  folders: [
    {
      name: 'Decisions',
      path: 'Decisions',
      folders: [],
      notes: [note('Decisions/a.md', 'decision', 'accepted'), note('Decisions/b.md', 'decision', 'proposed')],
    },
    {
      name: 'Offices',
      path: 'Offices',
      folders: [{ name: 'Allsafe', path: 'Offices/Allsafe', folders: [], notes: [note('Offices/Allsafe/overview.md', 'note')] }],
      notes: [],
    },
  ],
  notes: [note('Constitution.md', 'rule'), note('Now.md')],
}

describe('the tree', () => {
  it('flattens folders first, in order', () => {
    expect(flattenTree(tree).map((n) => n.path)).toEqual([
      'Decisions/a.md',
      'Decisions/b.md',
      'Offices/Allsafe/overview.md',
      'Constitution.md',
      'Now.md',
    ])
    expect(countNotes(tree)).toBe(5)
  })

  it('lists the kinds once, sorted', () => {
    expect(kindsOf(flattenTree(tree))).toEqual(['decision', 'note', 'rule'])
  })

  it('keeps only the notes of a kind and drops folders left empty', () => {
    const decisions = filterTree(tree, 'decision')
    expect(decisions.folders.map((f) => f.name)).toEqual(['Decisions'])
    expect(decisions.notes).toEqual([])
    expect(countNotes(decisions)).toBe(2)
    const notes = filterTree(tree, 'note')
    expect(notes.folders[0].folders[0].notes.map((n) => n.path)).toEqual(['Offices/Allsafe/overview.md'])
    expect(filterTree(tree, undefined)).toBe(tree)
  })

  it('knows the folders a note sits in', () => {
    expect(foldersOf('Offices/Allsafe/overview.md')).toEqual(['Offices', 'Offices/Allsafe'])
    expect(foldersOf('Now.md')).toEqual([])
    expect(topFolder('Offices/Allsafe/overview.md')).toBe('Offices')
    expect(topFolder('Now.md')).toBe('')
  })
})

describe('links', () => {
  const paths = ['Constitution.md', 'Decisions/2026-11-20-merge-order.md', 'Plans/release.md', 'Offices/Allsafe/overview.md', 'Offices/E Corp/overview.md']

  it.each([
    ['Plans/release', 'Plans/release.md'],
    ['release', 'Plans/release.md'],
    ['RELEASE', 'Plans/release.md'],
    ['release#Costs', 'Plans/release.md'],
    ['Allsafe/overview', 'Offices/Allsafe/overview.md'],
    ['E Corp/overview.md', 'Offices/E Corp/overview.md'],
  ])('resolves %s like the server does', (link, expected) => {
    expect(resolveLink(link, 'Studies/anything.md', paths)).toBe(expected)
  })

  it('resolves nothing for unknown notes or name fragments', () => {
    expect(resolveLink('nowhere', 'Studies/x.md', paths)).toBeNull()
    expect(resolveLink('lease', 'Studies/x.md', paths)).toBeNull()
    expect(resolveLink('#Costs', 'Studies/x.md', paths)).toBeNull()
  })

  it('prefers the note beside the link, then the shortest path', () => {
    expect(resolveLink('overview', 'Offices/Allsafe/glossary.md', paths)).toBe('Offices/Allsafe/overview.md')
    expect(resolveLink('overview', 'Studies/x.md', paths)).toBe('Offices/E Corp/overview.md')
  })

  it('takes a link apart: target, heading and label', () => {
    expect(parseWikiLink('[[Plans/release|the plan]] rest')).toEqual({ raw: '[[Plans/release|the plan]]', target: 'Plans/release', label: 'the plan' })
    expect(parseWikiLink('[[Plans/release.md#Costs]]')?.label).toBe('release')
    expect(parseWikiLink('[not a link]]')).toBeNull()
  })

  it('finds the links of a text, but not those in code', () => {
    expect(linksIn('See [[a]] and [[b|B]].\n\n`[[c]]`\n\n```\n[[d]]\n```\n')).toEqual(['a', 'b'])
  })

  it('renders resolved links with their path and unknown ones as muted words', () => {
    const html = renderNote('Read [[release|the plan]] and [[ghost]].', (t) => resolveLink(t, 'x.md', paths))
    expect(html).toContain('<a href="#" class="wikilink" data-path="Plans/release.md">the plan</a>')
    expect(html).toContain('<span class="wikilink wikilink-missing" title="No note called ghost">ghost</span>')
  })

  it('escapes what a link label carries', () => {
    expect(renderNote('[[a|<b>x</b>]]', () => 'a.md')).toContain('&lt;b&gt;x&lt;/b&gt;')
  })
})

describe('decisions', () => {
  const today = new Date(2026, 10, 21)
  it('says when a review is due', () => {
    expect(reviewState('2026-11-21', today)).toBe('overdue')
    expect(reviewState('2026-10-01', today)).toBe('overdue')
    expect(reviewState('2026-12-01', today)).toBe('soon')
    expect(reviewState('2027-02-01', today)).toBe('later')
  })

  it('gives known statuses their own class', () => {
    expect(statusClass('Accepted')).toBe('brain-status-accepted')
    expect(statusClass('superseded')).toBe('brain-status-superseded')
    expect(statusClass('draft')).toBe('brain-status-other')
    expect(statusClass(null)).toBe('brain-status-other')
  })

  it("refuses edits to Now only; the constitution is the person's to edit", () => {
    expect(editRefusal('Now.md')).toMatch(/read only/)
    expect(editRefusal('Constitution.md')).toBeNull()
    expect(isConstitution('constitution.md')).toBe(true)
  })
})

describe('the note body', () => {
  it('drops a first heading that repeats the title, and nothing else', () => {
    expect(withoutTitle('# Checkout\n\nPay.\n', 'Checkout')).toBe('Pay.\n')
    expect(withoutTitle('\n# Checkout\nPay.', 'Checkout')).toBe('Pay.')
    expect(withoutTitle('# Other\n\nPay.', 'Checkout')).toBe('# Other\n\nPay.')
    expect(withoutTitle('Intro\n# Checkout', 'Checkout')).toBe('Intro\n# Checkout')
    expect(withoutTitle('## Checkout\nPay.', 'Checkout')).toBe('## Checkout\nPay.')
  })
})
