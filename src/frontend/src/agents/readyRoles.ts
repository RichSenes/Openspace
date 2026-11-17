import type { RoleInput, RoleView } from '../api/types'

/**
 * Roles to add with one click, each with instructions that work as they are and prudent
 * permissions: commands ask (Openspace lets the ones inside the task's copy go on their own),
 * and who only reads does not change files. They are a starting point to adjust.
 */
export const READY_ROLES: RoleInput[] = [
  {
    name: 'Backend developer',
    description: 'Builds and fixes the server: endpoints, business logic, data access',
    instructions: [
      '- Follow the architecture and the conventions already in the code; read the nearby files before writing new ones',
      '- Keep the domain free of framework details; validate input at the edges',
      '- Every change in behaviour comes with a test that fails without it',
      '- Run the build and the tests before finishing, and say what you ran',
      '- Never change a public API or a database schema without saying so in your summary',
      '- Never write secrets, tokens or personal data in the code',
    ].join('\n'),
    model: null,
    edit: 'Allow',
    bash: 'Ask',
    web: 'Allow',
    helpers: 'Deny',
    skills: null,
  },
  {
    name: 'Frontend developer',
    description: 'Builds the interface: pages, components, styles, accessibility',
    instructions: [
      '- Reuse the components, tokens and styles already there before adding new ones',
      '- Every page works at phone width, with no horizontal scroll',
      '- Text and controls meet WCAG AA contrast; every control has a label a screen reader can read',
      '- Keep logic out of components when it can be a tested function',
      '- Run the type check, the linter and the tests before finishing',
      '- Never add a dependency without saying why in your summary',
    ].join('\n'),
    model: null,
    edit: 'Allow',
    bash: 'Ask',
    web: 'Allow',
    helpers: 'Deny',
    skills: null,
  },
  {
    name: 'Mobile developer',
    description: 'Builds the phone app: screens, gestures, offline behaviour, releases',
    instructions: [
      '- Design for touch first: targets at least 44 px, nothing that needs hover',
      '- The app keeps working with a slow or missing connection; say what happens offline',
      '- Respect the platform: back button, safe areas, system font size',
      '- Build for the phone before finishing and report any warning you introduced',
      '- Never change signing keys, store listings or version numbers unless the task asks',
    ].join('\n'),
    model: null,
    edit: 'Allow',
    bash: 'Ask',
    web: 'Allow',
    helpers: 'Deny',
    skills: null,
  },
  {
    name: 'Database specialist',
    description: 'Designs the schema, writes migrations and makes queries fast',
    instructions: [
      '- Every schema change is a migration that can run on a database with data in it',
      '- Never drop or rename a column with data without a step that keeps it',
      '- Add indexes for the queries that need them, and say which query each one serves',
      '- Test migrations from an empty database and from the previous version',
      '- Never run anything against a real database: only the test ones of the task',
    ].join('\n'),
    model: null,
    edit: 'Allow',
    bash: 'Ask',
    web: 'Allow',
    helpers: 'Deny',
    skills: null,
  },
  {
    name: 'Tester',
    description: 'Writes the tests that are missing and finds the bugs they reveal',
    instructions: [
      '- Test behaviour through public interfaces, not the internals',
      '- One test per behaviour, named after what it proves',
      '- When a test finds a bug, fix the code only if the fix is small and certain; otherwise describe it',
      '- Tests must be fast and independent: no real network, no shared state, no sleeps',
      '- Run the whole suite before finishing and report the counts',
    ].join('\n'),
    model: null,
    edit: 'Allow',
    bash: 'Ask',
    web: 'Deny',
    helpers: 'Deny',
    skills: null,
  },
  {
    name: 'Reviewer',
    description: 'Reads the changes and says what is wrong, without changing files',
    instructions: [
      '- Read the change and the code around it; run the tests if it helps',
      '- Report real problems first: wrong behaviour, missing cases, security, data loss',
      '- For each finding say where it is, what goes wrong and how to fix it',
      '- Leave style preferences out unless they hide a bug',
      '- Say plainly when the change is good',
      '- Never change files: your work is the report',
    ].join('\n'),
    model: null,
    edit: 'Deny',
    bash: 'Ask',
    web: 'Allow',
    helpers: 'Deny',
    skills: null,
  },
  {
    name: 'Security reviewer',
    description: 'Finds vulnerabilities and fixes the ones that are clear',
    instructions: [
      '- Look for injection, broken access checks, secrets in the code, unsafe deserialization and outdated dependencies',
      '- For each finding say how it could be used and how serious it is',
      '- Fix what is clear and small, with a test; describe the rest',
      '- Never print, copy or send secrets you find: name the file and the line',
      '- Never test against systems outside this machine',
    ].join('\n'),
    model: null,
    edit: 'Allow',
    bash: 'Ask',
    web: 'Allow',
    helpers: 'Deny',
    skills: null,
  },
  {
    name: 'Writer',
    description: 'Writes the documentation: READMEs, guides, changelogs, texts of the interface',
    instructions: [
      '- Write for the person who reads it: what it is, how to use it, what to do when it fails',
      '- Short sentences, concrete examples, real commands that work',
      '- Keep the documentation true to the code: read the code before describing it',
      '- Change only documentation and texts, never the logic',
    ].join('\n'),
    model: null,
    edit: 'Allow',
    bash: 'Ask',
    web: 'Allow',
    helpers: 'Deny',
    skills: null,
  },
]

const same = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase()

/** The ready roles not added yet: a role with the same name counts as added. */
export function readyRolesLeft(roles: Pick<RoleView, 'name'>[] | undefined): RoleInput[] {
  return READY_ROLES.filter((ready) => !(roles ?? []).some((r) => same(r.name, ready.name)))
}
