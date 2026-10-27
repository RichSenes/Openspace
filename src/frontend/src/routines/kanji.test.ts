import { describe, expect, it } from 'vitest'
import { asksForKanji, gradeLabel } from './kanji'

describe('asksForKanji', () => {
  it('is on only for a paper whose settings turn it on', () => {
    expect(asksForKanji('morning-paper', '{"kanji":{"enabled":true,"fromGrade":2}}')).toBe(true)
    expect(asksForKanji('morning-paper', '{"kanji":{"enabled":false}}')).toBe(false)
    expect(asksForKanji('morning-paper', '{}')).toBe(false)
    expect(asksForKanji('job-offers', '{"kanji":{"enabled":true}}')).toBe(false)
  })

  it('stays off while the settings are not valid JSON', () => {
    expect(asksForKanji('morning-paper', '{"kanji":')).toBe(false)
    expect(asksForKanji('morning-paper', 'null')).toBe(false)
  })
})

describe('gradeLabel', () => {
  it('names the school year, or the secondary school for the rest of the list', () => {
    expect(gradeLabel(1)).toBe('school year 1')
    expect(gradeLabel(8)).toBe('secondary school')
  })
})
