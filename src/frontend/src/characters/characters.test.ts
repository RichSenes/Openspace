import { describe, expect, it } from 'vitest'
import { agendaDays, agendaTime, agreementOutcome, agreementsInOrder, localDay, type AgreementView, affinityWord, birthdayLabel, daysIn, moodPath, moodWord, signed, tiesOf, trustOf, type RelationshipView } from './characters'

describe('birthdays', () => {
  it('name a day and a month, never a year', () => {
    expect(birthdayLabel({ month: 12, day: 24 })).toBe('24 December')
  })

  it('let 29 February be chosen', () => {
    expect(daysIn(2)).toBe(29)
    expect(daysIn(4)).toBe(30)
  })
})

describe('trust', () => {
  it('is today’s behaviour when never set', () => {
    expect(trustOf({ trust: {} }, 'SafeRequests')).toBe('Alone')
    expect(trustOf({ trust: { SafeRequests: 'AsksFirst' } }, 'SafeRequests')).toBe('AsksFirst')
  })
})

describe('words', () => {
  it('name moods and affinities', () => {
    expect(moodWord(80)).toBe('Cheerful')
    expect(moodWord(50)).toBe('So-so')
    expect(moodWord(10)).toBe('Down')
    expect(affinityWord(9)).toBe('Close')
    expect(affinityWord(0)).toBe('Acquainted')
    expect(affinityWord(-4)).toBe('Strained')
    expect(signed(3)).toBe('+3')
    expect(signed(-2)).toBe('-2')
  })
})

describe('moodPath', () => {
  const now = Date.parse('2026-11-21T12:00:00Z')

  it('is a flat line when nothing happened', () => {
    expect(moodPath({ baseline: 60, value: 60, history: [] }, now, 100, 50)).toBe('0,20 100,20')
  })

  it('steps at each change, from the baseline to the value', () => {
    const at = new Date(now - 3.5 * 86_400_000).toISOString()
    const path = moodPath({ baseline: 60, value: 50, history: [{ at, value: 50, change: -10, reason: 'failed' }] }, now, 100, 50)
    expect(path).toBe('0,20 50,20 50,25 100,25')
  })
})

describe('tiesOf', () => {
  it('names the other desk of each tie', () => {
    const tie: RelationshipView = { firstId: 'a', firstName: 'Frontend', secondId: 'b', secondName: 'Screens', affinity: 2, history: [] }
    expect(tiesOf({ relationships: [tie] }, 'b')).toEqual([{ other: 'Frontend', tie }])
    expect(tiesOf({ relationships: [tie] }, 'c')).toEqual([])
  })
})

describe('agreements', () => {
  const agreement = (id: string, status: AgreementView['status'], openedAt: string): AgreementView => ({
    id, officeId: 'o', status, subject: 'cart.ts', firstDeskId: 'a', firstDeskName: 'Frontend', firstTaskId: 't1', firstTaskTitle: 'Fix the total',
    firstProposal: '', secondDeskId: 'b', secondDeskName: 'Screens', secondTaskId: 't2', secondTaskTitle: 'Restyle the cart', secondProposal: '',
    goesFirst: null, reason: '', turns: 4, minutesPath: '', openedAt, closedAt: null, decidedBy: null,
  })

  it('say which task goes first, once someone said', () => {
    expect(agreementOutcome(agreement('1', 'WaitingForPerson', '2026-11-22'))).toBeNull()
    expect(agreementOutcome({ ...agreement('1', 'Settled', '2026-11-22'), goesFirst: 't2' })).toBe('“Restyle the cart” goes first')
  })

  it('put the questions for the person first, then the newest', () => {
    const list = [agreement('old', 'Settled', '2026-11-20'), agreement('new', 'Settled', '2026-11-22'), agreement('ask', 'WaitingForPerson', '2026-11-19')]
    expect(agreementsInOrder(list).map((a) => a.id)).toEqual(['ask', 'new', 'old'])
  })
})

describe('the agenda', () => {
  const line = (date: string, time: string | null, title: string) => ({ date, time, kind: 'Task' as const, title, detail: '' })

  it('groups lines by day in their order', () => {
    const days = agendaDays({ lines: [line('2026-11-22', '20:00:00', 'A'), line('2026-11-22', null, 'B'), line('2026-11-24', '07:00:00', 'C')] })
    expect(days.map((d) => [d.date, d.lines.map((l) => l.title)])).toEqual([['2026-11-22', ['A', 'B']], ['2026-11-24', ['C']]])
  })

  it('reads days on the local clock and shows hours without seconds', () => {
    const day = localDay('2026-11-22')
    expect([day.getFullYear(), day.getMonth(), day.getDate()]).toEqual([2026, 10, 22])
    expect(agendaTime('20:00:00')).toBe('20:00')
    expect(agendaTime(null)).toBe('')
  })
})
