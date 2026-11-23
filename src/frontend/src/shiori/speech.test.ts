import { describe, expect, it } from 'vitest'
import { languageOf, nextSentences, spokenText } from './speech'

describe('what of her answer is said aloud', () => {
  it('drops note links, markup and addresses', () => {
    expect(spokenText('Ho letto **due** note [[Decisions/2026-11-20-merge-order]]: vedi https://x.y/z.')).toBe('Ho letto due note: vedi.')
    expect(spokenText('Come dice [[Plans/logout|il piano]], prima i test.')).toBe('Come dice il piano, prima i test.')
    expect(spokenText('- uno\n- due')).toBe('uno due')
  })
})

describe('speaking while she writes', () => {
  it('gives the finished sentences and waits for the rest', () => {
    const text = 'Oggi ci sono tre compiti in coda per te. Il più urgente è la revisione della pagina di acc'
    const first = nextSentences(text, 0, false)
    expect(first.sentences).toEqual(['Oggi ci sono tre compiti in coda per te.'])
    const done = nextSentences(text + 'esso.', first.next, true)
    expect(done.sentences).toEqual(['Il più urgente è la revisione della pagina di accesso.'])
  })

  it('joins short sentences, so she does not stop after every "Sì."', () => {
    expect(nextSentences('Sì. Fatto. Ho spostato la riunione a domani alle dieci, come volevi.', 0, true).sentences).toEqual([
      'Sì. Fatto. Ho spostato la riunione a domani alle dieci, come volevi.',
    ])
  })

  it('cuts Japanese at its full stops', () => {
    const s = nextSentences('今日はタスクが三つあります。一番急ぎはログインページのレビューです。まとめましょうか？', 0, true).sentences
    expect(s.join('')).toBe('今日はタスクが三つあります。一番急ぎはログインページのレビューです。まとめましょうか？')
  })
})

describe('her language', () => {
  it('is Japanese by its script, else the one she was spoken to in', () => {
    expect(languageOf('今日は', 'it')).toBe('ja')
    expect(languageOf('Sure, here it is.', 'en')).toBe('en')
    expect(languageOf('Certo.', 'xx')).toBe('it')
  })
})
