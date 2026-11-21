import { describe, expect, it } from 'vitest'
import { botName, botNameProblem, chatOf, groupsFirst, placeOf, telegramStep, telegramSummary, tokenProblem, topicOf } from './telegram'

const chat = { id: -100777, title: 'Fsociety', kind: 'supergroup', topics: [{ id: 5, name: 'Morning paper' }] }

describe('telegram setup', () => {
  it('writes bot names as routines do and says what is wrong with one', () => {
    expect(botName('  paper ')).toBe('PAPER')
    expect(botNameProblem('paper')).toBeNull()
    expect(botNameProblem('')).toMatch(/name/)
    expect(botNameProblem('my bot')).toMatch(/One word/)
    expect(botNameProblem('9lives')).toMatch(/starting with a letter/)
    expect(botNameProblem('Paper', ['PAPER'])).toMatch(/already here/)
  })

  it('knows a token by its shape without ever needing a real one', () => {
    expect(tokenProblem('123456:made-up-token-for-the-tests')).toBeNull()
    expect(tokenProblem('')).toMatch(/Paste/)
    expect(tokenProblem('123456 made-up-token-for-the-tests')).toMatch(/not a bot token/)
    expect(tokenProblem('12:short')).toMatch(/not a bot token/)
  })

  it('reads topics and group numbers typed by hand', () => {
    expect(topicOf('')).toBeNull()
    expect(topicOf(' 12 ')).toBe(12)
    expect(topicOf('0')).toBeUndefined()
    expect(topicOf('abc')).toBeUndefined()
    expect(chatOf('-100777')).toBe(-100777)
    expect(chatOf('42')).toBe(42)
    expect(chatOf('0')).toBeUndefined()
    expect(chatOf('@group')).toBeUndefined()
  })

  it('walks from a first bot to the group to the tests', () => {
    expect(telegramStep(undefined)).toBe('bot')
    expect(telegramStep({ folder: '/x', chat: null, bots: [] })).toBe('bot')
    expect(telegramStep({ folder: '/x', chat: null, bots: [{ name: 'PAPER', topic: null }] })).toBe('group')
    expect(telegramStep({ folder: '/x', chat: -1, bots: [{ name: 'PAPER', topic: null }] })).toBe('test')
  })

  it('sums the setup up in one line', () => {
    expect(telegramSummary({ folder: '/x', chat: null, bots: [] })).toMatch(/Not set up/)
    expect(telegramSummary({ folder: '/x', chat: null, bots: [{ name: 'PAPER', topic: null }] })).toMatch(/1 bot, no group yet/)
    expect(telegramSummary({ folder: '/x', chat: -100777, bots: [{ name: 'A', topic: null }, { name: 'B', topic: 2 }] })).toBe('2 bots writing in group -100777.')
  })

  it('puts groups before private chats and names topics when it knows them', () => {
    const own = { id: 42, title: 'Darlene', kind: 'private', topics: [] }
    expect(groupsFirst([own, chat]).map((c) => c.id)).toEqual([-100777, 42])
    expect(placeOf(null, chat)).toBe('the main chat of the group')
    expect(placeOf(5, chat)).toBe('the topic “Morning paper”')
    expect(placeOf(9, chat)).toBe('topic 9')
  })
})
