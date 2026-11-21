import type { Token } from '../lab/shiori/script'
import { noteName } from './talk'

/** An answer word by word; a cited note is a chip that names it. */
export function Words({ tokens, streaming }: { tokens: Token[]; streaming: boolean }) {
  return (
    <>
      {tokens.map((t, i) => (
        <span key={i} className={i === tokens.length - 1 && streaming ? 'pr-word is-new' : 'pr-word'}>
          {i > 0 && !t.glued ? ' ' : ''}
          {t.type === 'cite' ? (
            <span className="pr-cite sp-cite" title={t.path}>
              {noteName(t.path)}
            </span>
          ) : (
            t.text
          )}
        </span>
      ))}
      {streaming && <span className="pr-caret" aria-hidden="true" />}
    </>
  )
}
