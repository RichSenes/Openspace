import { describe, expect, it } from 'vitest'
import { asksForKanji, gradeLabel, readKanji, withKanji } from './kanji'

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

describe('readKanji', () => {
  it('reads the school year to start from, the first one when it makes no sense', () => {
    expect(readKanji('{"kanji":{"enabled":true,"fromGrade":3}}')).toEqual({ enabled: true, fromGrade: 3 })
    expect(readKanji('{"kanji":{"enabled":true,"fromGrade":12}}')).toEqual({ enabled: true, fromGrade: 1 })
    expect(readKanji('{}')).toEqual({ enabled: false, fromGrade: 1 })
  })
})

describe('withKanji', () => {
  it('changes the kanji of the day and keeps every other setting', () => {
    const next = withKanji('{"sources":["a"],"kanji":{"enabled":false,"fromGrade":1,"extra":1}}', { enabled: true, fromGrade: 2 })
    expect(JSON.parse(next)).toEqual({ sources: ['a'], kanji: { enabled: true, fromGrade: 2, extra: 1 } })
  })

  it('starts from nothing when the settings are empty, and leaves broken JSON alone', () => {
    expect(JSON.parse(withKanji('', { enabled: true, fromGrade: 1 }))).toEqual({ kanji: { enabled: true, fromGrade: 1 } })
    expect(withKanji('{"kanji":', { enabled: true, fromGrade: 1 })).toBe('{"kanji":')
  })
})

describe('gradeLabel', () => {
  it('names the school year, or the secondary school for the rest of the list', () => {
    expect(gradeLabel(1)).toBe('primary school, year 1')
    expect(gradeLabel(8)).toBe('secondary school')
  })
})
