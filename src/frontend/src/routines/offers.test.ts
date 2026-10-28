import { describe, expect, it } from 'vitest'
import { LEAST_OFFER, offerProblem } from './offers'

const offer = 'Frontend Engineer at Made-up KK, Kyoto. React and TypeScript, visa sponsored, English at work.'

describe('a pasted offer', () => {
  it('goes when it is whole and its link, if any, is a web address', () => {
    expect(offer.length).toBeGreaterThanOrEqual(LEAST_OFFER)
    expect(offerProblem(offer, '')).toBeUndefined()
    expect(offerProblem(offer, ' https://jobs.example/made-up ')).toBeUndefined()
  })

  it('says how much is missing when it is too short', () => {
    expect(offerProblem('  Hello  ', '')).toBe(`Paste the whole offer: at least ${LEAST_OFFER} characters, 5 so far.`)
  })

  it('refuses a link that is not a web page', () => {
    expect(offerProblem(offer, 'ftp://jobs.example/x')).toBe('The link must be a web address, https://…')
    expect(offerProblem(offer, 'jobs.example')).toBe('The link must be a web address, https://…')
  })

  it('refuses a paste far longer than any offer', () => {
    expect(offerProblem('x'.repeat(40_001), '')).toMatch(/at most 40,000 characters/)
  })
})
