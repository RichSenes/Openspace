import type { EnginesView, OfficeView } from '../api/types'

/** Something missing that keeps desks from working, said once for the top bar. */
export interface SetupProblem {
  key: 'no-engine' | 'opencode' | 'claude-code'
  text: string
  title: string
}

const desksOn = (offices: OfficeView[], engine: 'OpenCode' | 'ClaudeCode') =>
  offices.flatMap((o) => o.rooms.flatMap((r) => r.desks)).filter((d) => (d.engine ?? 'OpenCode') === engine).length

const wait = (count: number) => `${count} ${count === 1 ? 'desk waits' : 'desks wait'}`

/**
 * What is missing for the desks there are: an engine that is not reachable while desks use it,
 * or no engine at all. Nothing while the engines are still being asked.
 */
export function setupProblems(engines: EnginesView | undefined, offices: OfficeView[]): SetupProblem[] {
  if (!engines) return []
  if (!engines.openCode && !engines.claudeCode) {
    return [
      {
        key: 'no-engine',
        text: 'No engine runs agents',
        title: 'Neither OpenCode nor Claude Code answers: start opencode serve, or install Claude Code. Open Agents to see how.',
      },
    ]
  }

  const problems: SetupProblem[] = []
  const onOpenCode = desksOn(offices, 'OpenCode')
  if (!engines.openCode && onOpenCode > 0) {
    problems.push({
      key: 'opencode',
      text: `OpenCode is not reachable: ${wait(onOpenCode)}`,
      title: 'Start opencode serve on this machine; the desks on OpenCode take work again by themselves.',
    })
  }

  const onClaude = desksOn(offices, 'ClaudeCode')
  if (!engines.claudeCode && onClaude > 0) {
    problems.push({
      key: 'claude-code',
      text: `Claude Code is not on this machine: ${wait(onClaude)}`,
      title: 'Install Claude Code and sign in with your account, or move these desks to OpenCode in Rooms.',
    })
  }

  return problems
}

export type StepAction = 'agents' | 'office' | 'rooms' | 'repository' | 'board'

export interface FirstStep {
  action: StepAction
  title: string
  detail: string
  done: boolean
}

/** Where a new office stands, from an engine to its first task. */
export interface OfficeProgress {
  office: OfficeView | undefined
  tasks: number
}

/**
 * The path to a first office at work: an engine, the office, its rooms and desks, the
 * repository the agents change, and a first task. Each step says what it is for.
 */
export function firstSteps(engines: EnginesView | undefined, { office, tasks }: OfficeProgress): FirstStep[] {
  const desks = office?.rooms.reduce((n, r) => n + r.desks.length, 0) ?? 0
  return [
    {
      action: 'agents',
      title: 'An engine for the agents',
      detail: engines?.openCode
        ? engines.claudeCode
          ? 'OpenCode and Claude Code are both ready.'
          : 'OpenCode is ready. Claude Code is optional: install it to use your Claude account.'
        : engines?.claudeCode
          ? 'Claude Code is ready. OpenCode is optional: start opencode serve for its models.'
          : 'Start opencode serve, or install Claude Code and sign in: the agents run on one of them.',
      done: Boolean(engines?.openCode || engines?.claudeCode),
    },
    {
      action: 'office',
      title: 'Open an office',
      detail: 'An office is one project, with the agents who work on it.',
      done: office !== undefined,
    },
    {
      action: 'rooms',
      title: 'Rooms and desks',
      detail: 'A room for each area (server, interface, tests…) and a desk for each agent, with its role and model.',
      done: desks > 0,
    },
    {
      action: 'repository',
      title: 'The repository',
      detail: 'The folder of the project on this machine: each task works on a copy of it and comes back as a branch.',
      done: Boolean(office?.repositoryPath),
    },
    {
      action: 'board',
      title: 'A first task',
      detail: 'Write what you want on the Board: a desk picks it up, and you accept its work or send it back.',
      done: tasks > 0,
    },
  ]
}
