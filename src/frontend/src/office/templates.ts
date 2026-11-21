import { READY_ROLES } from '../agents/readyRoles'
import type { DeskSetup, OfficeSetup, RoleSetup, RoomSetup, RoutineSetup, Weekday } from '../api/types'

/** A starting point for a new office: rooms with desks, the roles they use, and routines. */
export interface OfficeTemplate {
  id: string
  label: string
  description: string
  rooms: RoomSetup[]
  routines: RoutineSetup[]
  localOnly?: boolean
}

const desk = (name: string, agentName: string, role: string): DeskSetup => ({ name, agentName, model: null, engine: 'OpenCode', role })

const room = (name: string, context: string, procedure: string[], desks: DeskSetup[]): RoomSetup => ({ name, context, procedure, check: null, mood: null, desks })

const routine = (name: string, kind: string, days: Weekday[], at: string, enabled = true): RoutineSetup => ({
  name,
  kind,
  schedule: { days, at, everyWeeks: 1 },
  settings: null,
  enabled,
  askFirst: false,
})

const CODE_STEPS = ['Read the code around the change before writing', 'Add or update the tests for what changed', 'Run the build and the tests, and say what you ran']

/** The offices a person can start from; each one is adjusted afterwards like any other. */
export const TEMPLATES: OfficeTemplate[] = [
  {
    id: 'web-app',
    label: 'Web app',
    description: 'A server, an interface and a quality room: backend and frontend developers, a tester and a reviewer, with a weekly maintenance check.',
    rooms: [
      room('Backend', 'The server: endpoints, business logic and data. Write here the stack, the folders and the rules of this project.', CODE_STEPS, [
        desk('Backend', 'backend-dev', 'Backend developer'),
      ]),
      room('Frontend', 'The interface: pages, components and styles. Write here the framework, the design rules and where components live.', CODE_STEPS, [
        desk('Frontend', 'frontend-dev', 'Frontend developer'),
      ]),
      room('Quality', 'Tests and reviews of the work of the other rooms.', [], [desk('Tester', 'tester', 'Tester'), desk('Reviewer', 'reviewer', 'Reviewer')]),
    ],
    routines: [routine('Maintenance', 'maintenance', ['Monday'], '09:00')],
  },
  {
    id: 'library',
    label: 'Library',
    description: 'A package others use: one room for the code, one for its documentation, one for reviews and security, with a weekly maintenance check.',
    rooms: [
      room('Code', 'The library itself. Its public API is a promise: say in the summary when a change touches it.', CODE_STEPS, [
        desk('Developer', 'library-dev', 'Backend developer'),
        desk('Tester', 'tester', 'Tester'),
      ]),
      room('Docs', 'The README, the guides and the changelog. Every example must run as written.', [], [desk('Writer', 'writer', 'Writer')]),
      room('Review', 'Reviews of every change before it is merged.', [], [desk('Reviewer', 'reviewer', 'Reviewer'), desk('Security', 'security-reviewer', 'Security reviewer')]),
    ],
    routines: [routine('Maintenance', 'maintenance', ['Monday'], '09:00')],
  },
  {
    id: 'assistant',
    label: 'Personal assistant',
    description:
      'No code: routines that sort your mail, follow up on it and plan your days, and a writer for drafts. Kept on this machine. The routines start turned off: choose your Google account in each, then turn it on.',
    localOnly: true,
    rooms: [room('Desk', 'Notes, drafts and letters for the person. Plain words, no jargon.', [], [desk('Writer', 'writer', 'Writer')])],
    routines: [
      routine('Mail', 'mail', [], '08:00', false),
      routine('Follow-up', 'followup', ['Monday', 'Thursday'], '09:00', false),
      routine('Tomorrow morning', 'tomorrow', [], '20:00', false),
      routine('Weekly recap', 'weekly', ['Sunday'], '18:00', false),
    ],
  },
  {
    id: 'documentation',
    label: 'Documentation',
    description: 'A site or a handbook made of pages: two writers and a reviewer, and the office’s decisions brought back when they are due.',
    rooms: [
      room('Writing', 'The pages. Write here the audience, the tone and where the pages live.', ['Read the pages around the one you change', 'Check every link and example'], [
        desk('Writer', 'writer', 'Writer'),
        desk('Editor', 'editor', 'Writer'),
      ]),
      room('Review', 'Reviews of the pages before they are published.', [], [desk('Reviewer', 'reviewer', 'Reviewer')]),
    ],
    routines: [routine('Decision reviews', 'decision-reviews', [], '09:00')],
  },
]

/** A ready-made role as the office file writes it. */
function roleSetup(name: string): RoleSetup {
  const role = READY_ROLES.find((r) => r.name === name)
  if (!role) throw new Error(`No ready-made role called ${name}`)
  return {
    name: role.name,
    description: role.description,
    instructions: role.instructions,
    model: role.model,
    permissions: { edit: role.edit, bash: role.bash, web: role.web, helpers: role.helpers },
    skills: role.skills,
  }
}

/** The office file a template makes: the roles its desks use come from the ready-made ones. */
export function templateSetup(template: OfficeTemplate, name: string): OfficeSetup {
  const roles = [...new Set(template.rooms.flatMap((r) => r.desks.map((d) => d.role)).filter((r): r is string => r !== null))]
  return {
    format: 'openspace-office',
    version: 1,
    name: name.trim(),
    theme: null,
    localOnly: template.localOnly ?? false,
    holdOverlappingWork: true,
    reserveModels: [],
    roles: roles.map(roleSetup),
    rooms: template.rooms,
    routines: template.routines,
  }
}
