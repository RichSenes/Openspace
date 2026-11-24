import { describe, expect, it } from 'vitest'
import { tokenize } from '../lab/shiori/script'
import { styled } from './emphasis'

const shown = (text: string) =>
  styled(tokenize(text)).map((s) => (s.bullet ? '•' : `${s.text}${s.bold ? '[b]' : ''}${s.italic ? '[i]' : ''}`))

describe('markdown in her subtitles', () => {
  it('shows bold and italic as styles, without the asterisks', () => {
    expect(shown('- **おはようございます。** (*Ohayō gozaimasu.*): "Buongiorno."')).toEqual([
      '•',
      'おはようございます。[b]',
      '(Ohayō[i]',
      'gozaimasu.):[i]',
      '"Buongiorno."',
    ])
  })

  it('carries a style over several words', () => {
    expect(shown('Te ne consiglio **una sola, oggi**.')).toEqual(['Te', 'ne', 'consiglio', 'una[b]', 'sola,[b]', 'oggi.[b]'])
  })

  it('leaves alone marks that are not markdown', () => {
    expect(shown('2 * 3 fa 6, e snake_case resta')).toEqual(['2', '*', '3', 'fa', '6,', 'e', 'snake_case', 'resta'])
  })

  it('starts list items on their own line', () => {
    const tokens = tokenize('Ecco:\n- uno\n- due')
    expect(tokens.filter((t) => t.line).length).toBe(2)
    expect(styled(tokens).filter((s) => s.bullet).length).toBe(2)
  })
})
