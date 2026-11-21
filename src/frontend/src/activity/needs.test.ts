import { describe, expect, it } from 'vitest'
import type { NeedsYouItem } from '../api/types'
import { decisionAnswers, grouped, isDecision, notePath, targetOf, waited, withoutItem } from './needs'

const item = (kind: NeedsYouItem['kind'], title: string): NeedsYouItem => ({
  kind,
  officeId: 'o1',
  officeName: 'E Corp',
  id: `id-${title}`,
  title,
  detail: '',
  since: '2026-11-17T10:00:00Z',
})

describe('what needs the person', () => {
  it('groups the items by kind, in a fixed order, leaving out empty kinds', () => {
    const groups = grouped([item('Proposal', 'Offer'), item('TaskFailed', 'Login'), item('TaskToApprove', 'Privacy'), item('TaskToApprove', 'Logout')])
    expect(groups.map((g) => [g.heading, g.items.map((i) => i.title)])).toEqual([
      ['Tasks to approve', ['Privacy', 'Logout']],
      ['Failed tasks', ['Login']],
      ['Routines ask', ['Offer']],
    ])
  })

  it('opens tasks on the Board and routines in Routines, in their office', () => {
    expect(targetOf(item('TaskToReview', 'Logout'))).toEqual({ officeId: 'o1', view: 'board', taskId: 'id-Logout' })
    expect(targetOf(item('RoutineFailed', 'Paper'))).toEqual({ officeId: 'o1', view: 'routines' })
  })

  it('says how long an item waited', () => {
    const since = Date.parse('2026-11-17T10:00:00Z')
    expect(waited('2026-11-17T10:00:00Z', since + 20_000)).toBe('just now')
    expect(waited('2026-11-17T10:00:00Z', since + 12 * 60_000)).toBe('12 min')
    expect(waited('2026-11-17T10:00:00Z', since + 5 * 3_600_000)).toBe('5 h')
    expect(waited('2026-11-17T10:00:00Z', since + 72 * 3_600_000)).toBe('3 days')
  })

  it('drops a settled item at once, whatever the case of its id', () => {
    const list = [item('TaskToApprove', 'Privacy'), item('Proposal', 'Offer')]
    expect(withoutItem(list, 'ID-PRIVACY')?.map((i) => i.title)).toEqual(['Offer'])
    expect(withoutItem(undefined, 'id-Offer')).toBeUndefined()
  })

  it('puts the brain\'s decisions after the rest, in their own groups', () => {
    const groups = grouped([item('DecisionDue', 'Merge order'), item('DecisionToAccept', 'Tabs'), item('Proposal', 'Offer'), item('DecisionToReview', 'Queue')])
    expect(groups.map((g) => g.heading)).toEqual(['Routines ask', 'Shiori accepted, to review', 'Decisions to accept', 'Decisions to look at again'])
  })

  it('gives each kind of decision its own answers, and none to the rest', () => {
    expect(decisionAnswers('DecisionToReview').map((a) => [a.verb, a.label])).toEqual([['fine', 'Fine'], ['undo', 'Undo']])
    expect(decisionAnswers('DecisionToAccept').map((a) => a.verb)).toEqual(['accept', 'reject'])
    expect(decisionAnswers('DecisionDue').map((a) => a.verb)).toEqual(['keep', 'supersede'])
    expect(decisionAnswers('Proposal')).toEqual([])
    expect(isDecision('DecisionDue')).toBe(true)
    expect(isDecision('TaskFailed')).toBe(false)
  })

  it('keeps the slashes of a note\'s path and escapes the rest', () => {
    expect(notePath('Decisions/2026-11-20 merge #2.md')).toBe('Decisions/2026-11-20%20merge%20%232.md')
  })
})
