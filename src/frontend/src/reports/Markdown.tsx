import { useMemo } from 'react'
import DOMPurify from 'dompurify'
import { marked } from 'marked'

/** Renders Markdown written by models or people, sanitised before it reaches the page. */
export function Markdown({ text }: { text: string }) {
  const html = useMemo(() => DOMPurify.sanitize(marked.parse(text, { async: false })), [text])
  return <div className="markdown" dangerouslySetInnerHTML={{ __html: html }} />
}
