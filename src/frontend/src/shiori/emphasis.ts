import type { Token } from '../lab/shiori/script'

/** A word as shown: its text without markdown marks, and whether it is bold or italic. */
export interface Styled {
  token: Token
  text: string
  bold: boolean
  italic: boolean
  /** A list item starts here ("- ", "* ", "1. " at the start of a line). */
  bullet: boolean
}

const OPEN = /^([("“'«「]*)(\*\*|__|\*|_)(?=\S)/
const CLOSE = /(\S)(\*\*|__|\*|_)([)"”'».,;:!?。、」]*)$/

/**
 * Her answers are written in markdown, and the subtitles show words, not marks: **bold** and
 * *italic* (or __ and _) become styles that may run over several words, and "- " at the start of
 * a line becomes a bullet. Marks that never close are left as they are.
 */
export function styled(tokens: Token[]): Styled[] {
  let bold = false
  let italic = false
  return tokens.map((token, i) => {
    if (token.type !== 'word') return { token, text: '', bold, italic, bullet: false }
    const starts = i === 0 || !!token.line
    if (starts && /^([-•*]|\d+\.)$/.test(token.text)) return { token, text: '', bold, italic, bullet: true }
    let text = token.text
    let wordBold = bold
    let wordItalic = italic
    for (let open = text.match(OPEN); open; open = text.match(OPEN)) {
      const strong = open[2].length === 2
      if (strong ? bold : italic) break
      text = open[1] + text.slice(open[0].length)
      if (strong) wordBold = bold = true
      else wordItalic = italic = true
    }
    for (let close = text.match(CLOSE); close; close = text.match(CLOSE)) {
      const strong = close[2].length === 2
      if (!(strong ? bold : italic)) break
      text = text.slice(0, close.index! + 1) + close[3]
      if (strong) bold = false
      else italic = false
    }
    return { token, text, bold: wordBold, italic: wordItalic, bullet: false }
  })
}
