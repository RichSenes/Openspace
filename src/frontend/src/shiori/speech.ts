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
    .replace(/^\s*[-•]\s+/gm, '')
    .replace(/\(\s*\)/g, '')
    .replace(/\s+([,.;:!?])/g, '$1')
    .replace(/\s+/g, ' ')
    .trim()
}

/** Sentences of at least this length are spoken alone; shorter ones join the next. */
const SHORTEST = 40
const END = /[.!?。！？…]+["'”」)]*(\s+|$)/g

/**
 * Splits off the sentences that are finished in `text` from `from` on, for speaking while she
 * is still writing; `final` takes the rest too. Returns them and where the next one starts.
 */
export function nextSentences(text: string, from: number, final: boolean): { sentences: string[]; next: number } {
  const sentences: string[] = []
  let start = from
  let cut = from
  END.lastIndex = from
  for (let m = END.exec(text); m; m = END.exec(text)) {
    cut = m.index + m[0].length
    if (cut - start >= SHORTEST) {
      sentences.push(text.slice(start, cut))
      start = cut
    }
  }
  if (final && start < text.length) {
    sentences.push(text.slice(start))
    start = text.length
  }
  return { sentences: sentences.map(spokenText).filter(Boolean), next: start }
}

/** Her language for this answer: Japanese by its script, else the one she was spoken to in. */
export function languageOf(text: string, spokenIn: string): 'it' | 'en' | 'ja' {
  if (/[぀-ヿ一-鿿]/.test(text)) return 'ja'
  return spokenIn === 'en' ? 'en' : spokenIn === 'ja' ? 'ja' : 'it'
}
