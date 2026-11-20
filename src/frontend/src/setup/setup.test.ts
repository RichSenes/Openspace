import { describe, expect, it } from 'vitest'
import type { EnginesView, OfficeView } from '../api/types'
import { firstSteps, setupProblems } from './setup'

const engines = (openCode: boolean, claudeCode: boolean): EnginesView => ({
  openCode,
  openCodeModels: [],
  claudeCode,
  claudeCodeModels: [],
  claudeUsage: null,
  supervisor: 'OpenCode',
  reviewer: null,
})

const office = (desks: ('OpenCode' | 'ClaudeCode' | undefined)[], repositoryPath: string | null = null): OfficeView =>
  ({
    id: 'o1',
    name: 'Shop',
    repositoryPath,
    theme: 'cozy',
    rooms: [{ id: 'r1', name: 'Server', desks: desks.map((engine, i) => ({ id: `d${i}`, name: `Desk ${i}`, agentName: 'build', model: null, roleId: null, engine })) }],
  }) as unknown as OfficeView

describe('setupProblems', () => {
  it('says nothing while the engines are asked, or when the desks have what they need', () => {
    expect(setupProblems(undefined, [office(['OpenCode'])])).toEqual([])
    expect(setupProblems(engines(true, false), [office(['OpenCode', undefined])])).toEqual([])
  })

  it('counts the desks that wait for a missing engine', () => {
    const problems = setupProblems(engines(false, true), [office(['OpenCode', undefined, 'ClaudeCode'])])
    expect(problems.map((p) => p.text)).toEqual(['OpenCode is not reachable: 2 desks wait'])
    expect(setupProblems(engines(true, false), [office(['ClaudeCode'])]).map((p) => p.text)).toEqual([
      'Claude Code is not on this machine: 1 desk waits',
    ])
  })

  it('says so when no engine at all can run agents, even without desks', () => {
    expect(setupProblems(engines(false, false), []).map((p) => p.key)).toEqual(['no-engine'])
  })
})

describe('firstSteps', () => {
  it('starts from the engine and the office', () => {
    const steps = firstSteps(engines(false, false), { office: undefined, tasks: 0 })
    expect(steps.map((s) => s.action)).toEqual(['engine', 'office', 'rooms', 'repository', 'board'])
    expect(steps.every((s) => !s.done)).toBe(true)
  })

  it('ticks what a new office already has', () => {
    const steps = firstSteps(engines(true, false), { office: office(['OpenCode'], '/repos/shop'), tasks: 0 })
    expect(steps.filter((s) => s.done).map((s) => s.action)).toEqual(['engine', 'office', 'rooms', 'repository'])
    expect(firstSteps(engines(true, true), { office: office([]), tasks: 1 }).find((s) => s.action === 'rooms')?.done).toBe(false)
  })
})
