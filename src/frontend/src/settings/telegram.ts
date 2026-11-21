import type { TelegramChatView, TelegramStatusView } from '../api/types'

/** A bot's name in Openspace as routines write it: upper case, no spaces around. */
export const botName = (text: string): string => text.trim().toUpperCase()

/** What is wrong with a bot's name, or null: one word of letters, digits and _, starting with a letter, at most 32. */
export function botNameProblem(text: string, taken: string[] = []): string | null {
  const name = botName(text)
  if (name === '') return 'Give the bot a name, e.g. PAPER.'
  if (!/^[A-Z][A-Z0-9_]{0,31}$/.test(name)) return 'One word of letters, digits and _, starting with a letter, at most 32.'
  if (taken.includes(name)) return `${name} is already here: remove it first to give it another token.`
  return null
}

/** What is wrong with a token, or null: BotFather gives a number, a colon and a long code. */
export function tokenProblem(text: string): string | null {
  const token = text.trim()
  if (token === '') return 'Paste the token BotFather gave you.'
  if (!/^[0-9]{3,}:[A-Za-z0-9_-]{20,}$/.test(token)) return 'That is not a bot token: it is a number, a colon and a long code, with no spaces.'
  return null
}

/** A topic typed by hand: empty is the group's main chat, otherwise a whole positive number. */
export function topicOf(text: string): number | null | undefined {
  const value = text.trim()
  if (value === '') return null
  return /^[0-9]{1,12}$/.test(value) && Number(value) > 0 ? Number(value) : undefined
}

/** A group's number typed by hand: groups have negative numbers, people positive ones; never 0. */
export function chatOf(text: string): number | undefined {
  const value = text.trim()
  return /^-?[0-9]{1,16}$/.test(value) && Number(value) !== 0 ? Number(value) : undefined
}

export type TelegramStep = 'bot' | 'group' | 'test'

/** Where the person is in the setup: a first bot, then the group, then testing each bot. */
export function telegramStep(status: TelegramStatusView | undefined): TelegramStep {
  if (!status || status.bots.length === 0) return 'bot'
  if (status.chat === null) return 'group'
  return 'test'
}

/** One line on how far Telegram is set up, for the top of the card. */
export function telegramSummary(status: TelegramStatusView | undefined): string {
  if (!status) return 'Reading the setup…'
  const bots = status.bots.length
  if (bots === 0) return 'Not set up: routines keep what they make in their folders.'
  const named = `${bots} ${bots === 1 ? 'bot' : 'bots'}`
  return status.chat === null ? `${named}, no group yet: routines cannot write until you choose one.` : `${named} writing in group ${status.chat}.`
}

/** Groups first (that is where routines write), then the rest; people's own chats last. */
export function groupsFirst(chats: TelegramChatView[]): TelegramChatView[] {
  const rank = (kind: string) => (kind === 'supergroup' || kind === 'group' ? 0 : kind === 'channel' ? 1 : 2)
  return [...chats].sort((a, b) => rank(a.kind) - rank(b.kind))
}

/** How a bot's place reads in the list: its topic by name when known. */
export function placeOf(topic: number | null, chat: TelegramChatView | undefined): string {
  if (topic === null) return 'the main chat of the group'
  const name = chat?.topics.find((t) => t.id === topic)?.name
  return name ? `the topic “${name}”` : `topic ${topic}`
}
