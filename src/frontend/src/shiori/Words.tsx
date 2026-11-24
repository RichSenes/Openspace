import type { Token } from '../lab/shiori/script'
import { styled } from './emphasis'
import { noteName } from './talk'

/**
 * An answer word by word; a cited note is a chip that names it. Markdown marks become styles:
 * bold and italic words, list items on their own lines with a bullet.
 */
export function Words({ tokens, streaming }: { tokens: Token[]; streaming: boolean }) {
  const words = styled(tokens)
  return (
    <>
      {words.map((w, i) => {
        const t = w.token
        const word =
          t.type === 'cite' ? (
            <span className="pr-cite sp-cite" title={t.path}>
              {noteName(t.path)}
            </span>
          ) : w.bold && w.italic ? (
            <strong>
              <em>{w.text}</em>
            </strong>
          ) : w.bold ? (
            <strong>{w.text}</strong>
          ) : w.italic ? (
            <em>{w.text}</em>
          ) : (
            w.text
          )
        return (
          <span key={i} className={i === words.length - 1 && streaming ? 'pr-word is-new' : 'pr-word'}>
            {i > 0 && t.line ? <br /> : i > 0 && !t.glued && !words[i - 1].bullet ? ' ' : ''}
            {w.bullet ? <span aria-hidden="true">•&nbsp;</span> : word}
          </span>
        )
      })}
      {streaming && <span className="pr-caret" aria-hidden="true" />}
    </>
  )
}
