/** Shorter than this, a pasted offer says too little to judge; the server holds the same limits. */
export const LEAST_OFFER = 80
export const MOST_OFFER = 40_000

/** What is wrong with an offer about to be sent, said plainly; undefined when it can go. */
export function offerProblem(text: string, link: string): string | undefined {
  const length = text.trim().length
  if (length < LEAST_OFFER) return `Paste the whole offer: at least ${LEAST_OFFER} characters, ${length} so far.`
  if (length > MOST_OFFER) return `That is more than an offer: at most ${MOST_OFFER.toLocaleString('en')} characters.`
  const page = link.trim()
  if (page && !/^https?:\/\/\S+$/i.test(page)) return 'The link must be a web address, https://…'
  return undefined
}
