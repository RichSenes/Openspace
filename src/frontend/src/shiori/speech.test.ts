import { describe, expect, it } from 'vitest'
import { byScript, languageOf, nextSentences, spokenText } from './speech'

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

describe('words in two languages', () => {
  it('says the Japanese, not its reading in Latin letters', () => {
    expect(spokenText('- **おはようございます。** (*Ohayō gozaimasu.*): "Buongiorno."')).toBe('おはようございます。: "Buongiorno."')
    expect(spokenText('Si dice 痛い (itai), cioè "fa male".')).toBe('Si dice 痛い, cioè "fa male".')
    expect(spokenText('Il piano (quello nuovo) va bene.')).toBe('Il piano (quello nuovo) va bene.')
  })

  it('splits a sentence where the script changes, sharing out its written stretch', () => {
    const parts = byScript({ text: 'Per oggi te ne consiglio una: 痛い, che vuol dire "fa male!"', from: 100, to: 160 })
    expect(parts.map((p) => p.text)).toEqual(['Per oggi te ne consiglio una:', '痛い,', 'che vuol dire "fa male!"'])
    expect(parts[0].from).toBe(100)
    expect(parts.at(-1)!.to).toBe(160)
    expect(parts.every((p, i) => i === 0 || p.from === parts[i - 1].to)).toBe(true)
  })

  it('keeps a sentence in one script whole', () => {
    expect(byScript({ text: '今日はタスクが三つあります。', from: 0, to: 14 })).toEqual([{ text: '今日はタスクが三つあります。', from: 0, to: 14 }])
  })
})

describe('where each sentence is written', () => {
  it('gives the stretch of the text each sentence stands for', () => {
    const text = 'Oggi ci sono tre compiti in coda per te. Il più urgente è la revisione della pagina.'
    const { parts } = nextSentences(text, 0, true)
    expect(parts.map((p) => text.slice(p.from, p.to).trim())).toEqual(['Oggi ci sono tre compiti in coda per te.', 'Il più urgente è la revisione della pagina.'])
  })
})
