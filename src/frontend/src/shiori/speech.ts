/**
 * Shiori's voice: her answer to something said aloud is spoken back, a sentence at a time as
 * she writes it, by the speech server on this machine. These are the pure parts: what of the
 * text is said, where it is cut, and in which language.
 */

/** What is said out loud: no note links, markup or addresses, which only make sense written. */
export function spokenText(text: string): string {
  return text
    .replace(/\[\[([^\]|]+)\|([^\]]+)\]\]/g, '$2') // [[path|name]] says its name
    .replace(/\[\[[^\]]+\]\]/g, '') // a bare note link is for reading
    .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1') // [words](url)
    .replace(/https?:\/\/\S*[^\s.,;:!?)]/g, '')
    .replace(/```[\s\S]*?```/g, '')
    .replace(/[*_`#>]+/g, '')
    // おはよう (*Ohayō*): the reading in Latin letters is for the eyes; she says the Japanese.
    .replace(new RegExp(`(${JA.source}[。！？]?)\\s*[(（][^()（）぀-ヿ一-鿿]*[)）]`, 'g'), '$1')
    .replace(/^\s*[-•]\s+/gm, '')
    .replace(/\(\s*\)/g, '')
    .replace(/\s+([,.;:!?])/g, '$1')
    .replace(/\s+/g, ' ')
    .trim()
}

/** Kana and kanji: Japanese written as Japanese. */
const JA = /[぀-ヿ㐀-鿿々〆ー]/

/** A piece of her answer to say, and the stretch of the written text it stands for. */
export interface Spoken {
  text: string
  from: number
  to: number
}

/**
 * Splits a sentence where it changes between Japanese and other scripts, so each piece is said
 * in one language: a voice reading "Si dice おはようございます, buongiorno" in one go stumbles
 * at the switch. Spaces and punctuation stay with the piece before them. The written stretch is
 * shared out by length.
 */
export function byScript(part: Spoken): Spoken[] {
  const runs: string[] = []
  let current = ''
  let ja: boolean | null = null
  for (const ch of part.text) {
    const letter = /[\p{L}\p{N}]/u.test(ch)
    const isJa = JA.test(ch)
    if (letter && ja !== null && isJa !== ja) {
      runs.push(current)
      current = ''
    }
    if (letter) ja = isJa
    current += ch
  }
  if (current) runs.push(current)
  const said = runs.map((r) => r.trim()).filter((r) => /[\p{L}\p{N}]/u.test(r))
  if (said.length <= 1) return said.length ? [{ ...part, text: said[0] }] : []
  const total = said.reduce((n, r) => n + r.length, 0)
  let at = part.from
  return said.map((text, i) => {
    const to = i === said.length - 1 ? part.to : Math.round(at + ((part.to - part.from) * text.length) / total)
    const piece = { text, from: at, to }
    at = to
    return piece
  })
}

/** Sentences of at least this length are spoken alone; shorter ones join the next. */
const SHORTEST = 40
const END = /[.!?。！？…]+["'”」)]*(\s+|$)/g

/**
 * Splits off the sentences that are finished in `text` from `from` on, for speaking while she
 * is still writing; `final` takes the rest too. Returns them and where the next one starts.
 */
export function nextSentences(text: string, from: number, final: boolean): { sentences: string[]; parts: Spoken[]; next: number } {
  const parts: Spoken[] = []
  let start = from
  let cut = from
  END.lastIndex = from
  for (let m = END.exec(text); m; m = END.exec(text)) {
    cut = m.index + m[0].length
    if (cut - start >= SHORTEST) {
      parts.push({ text: spokenText(text.slice(start, cut)), from: start, to: cut })
      start = cut
    }
  }
  if (final && start < text.length) {
    parts.push({ text: spokenText(text.slice(start)), from: start, to: text.length })
    start = text.length
  }
  const said = parts.filter((p) => p.text)
  return { sentences: said.map((p) => p.text), parts: said, next: start }
}

/** Her language for this answer: Japanese by its script, else the one she was spoken to in. */
export function languageOf(text: string, spokenIn: string): 'it' | 'en' | 'ja' {
  if (JA.test(text)) return 'ja'
  return spokenIn === 'en' ? 'en' : spokenIn === 'ja' ? 'ja' : 'it'
}
