import { describe, expect, it } from 'vitest'
import { READY_ROLES } from '../agents/readyRoles'
import { describeSetup, readSetupFile, setupFileName } from './setupFile'
import { TEMPLATES, templateSetup } from './templates'

describe('office templates', () => {
  it('offers a web app, a library, a personal assistant and documentation', () => {
    expect(TEMPLATES.map((t) => t.id)).toEqual(['web-app', 'library', 'assistant', 'documentation'])
  })

  it('makes an office file whose desks use ready-made roles, each role once', () => {
    for (const template of TEMPLATES) {
      const setup = templateSetup(template, '  Allsafe  ')
      expect(setup.name).toBe('Allsafe')
      expect(setup.format).toBe('openspace-office')
      const roles = setup.roles.map((r) => r.name)
      expect(new Set(roles).size).toBe(roles.length)
      for (const d of setup.rooms.flatMap((r) => r.desks)) {
        expect(roles).toContain(d.role)
        expect(d.agentName).toMatch(/^[a-z0-9-]+$/)
      }
      for (const r of setup.roles) expect(READY_ROLES.map((x) => x.name)).toContain(r.name)
    }
  })

  it('starts the assistant kept on this machine with its account routines turned off', () => {
    const setup = templateSetup(TEMPLATES.find((t) => t.id === 'assistant')!, 'Me')
    expect(setup.localOnly).toBe(true)
    expect(setup.routines.length).toBeGreaterThan(0)
    expect(setup.routines.every((r) => !r.enabled)).toBe(true)
  })
})

describe('office files', () => {
  const setup = templateSetup(TEMPLATES[0], 'E Corp')

  it('reads back what was exported and refuses what is not an office file', () => {
    expect(readSetupFile(JSON.stringify(setup))).toEqual({ setup })
    expect(readSetupFile('nope')).toEqual({ problem: expect.stringMatching(/not valid JSON/) })
    expect(readSetupFile('{"format":"zip"}')).toEqual({ problem: expect.stringMatching(/not an office file/) })
    expect(readSetupFile(JSON.stringify({ ...setup, version: 2 }))).toEqual({ problem: expect.stringMatching(/newer Openspace/) })
  })

  it('says what a file holds and names it after the office', () => {
    expect(describeSetup(setup)).toBe('3 rooms, 4 desks, 4 roles, 1 routine')
    expect(setupFileName('Café Crumb / v2')).toBe('cafe-crumb-v2.openspace.json')
    expect(setupFileName('???')).toBe('office.openspace.json')
  })
})
