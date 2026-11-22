import { describe, expect, it } from 'vitest'
import { diffStats, parseDiff, shortCommit, withoutHeaders } from './diff'

const diff = `diff --git a/Plans/x.md b/Plans/x.md
index 1111111..2222222 100644
--- a/Plans/x.md
+++ b/Plans/x.md
@@ -1,3 +1,4 @@
 # Checkout
-Pay with a card.
+Pay with a card
+or by bank transfer.
 End
\\ No newline at end of file
`

describe('the history diff', () => {
  it('numbers each line in the version before and in this one', () => {
    const lines = parseDiff(diff)
    expect(lines.slice(0, 4).every((l) => l.kind === 'meta')).toBe(true)
    expect(lines.slice(4).map((l) => [l.kind, l.old, l.new, l.text])).toEqual([
      ['hunk', null, null, '@@ -1,3 +1,4 @@'],
      ['same', 1, 1, '# Checkout'],
      ['del', 2, null, 'Pay with a card.'],
      ['add', null, 2, 'Pay with a card'],
      ['add', null, 3, 'or by bank transfer.'],
      ['same', 3, 4, 'End'],
      ['meta', null, null, '\\ No newline at end of file'],
    ])
  })

  it('counts what changed and hides the file headers', () => {
    const lines = parseDiff(diff)
    expect(diffStats(lines)).toEqual({ added: 2, removed: 1 })
    expect(withoutHeaders(lines)[0].kind).toBe('hunk')
    expect(withoutHeaders(lines).at(-1)?.text).toMatch(/No newline/)
  })

  it('reads a first version and an empty diff', () => {
    const first = parseDiff('@@ -0,0 +1,2 @@\n+# New\n+Body\n')
    expect(first.map((l) => l.new)).toEqual([null, 1, 2])
    expect(parseDiff('')).toEqual([])
    expect(parseDiff('@@ -1 +1 @@\r\n-a\r\n+b\r\n').map((l) => l.text)).toEqual(['@@ -1 +1 @@', 'a', 'b'])
  })

  it('shortens commits like git', () => {
    expect(shortCommit('0123456789abcdef')).toBe('0123456')
  })
})
