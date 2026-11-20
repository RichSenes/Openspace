import { describe, expect, it, vi } from 'vitest'
import { isSection, onSectionRequest, requestSettingsSection, SECTIONS } from './sections'

describe('settings sections', () => {
  it('starts from the engines and ends with the system', () => {
    expect(SECTIONS.map((s) => s.id)).toEqual(['engines', 'agents', 'connections', 'look', 'system'])
    expect(isSection('look')).toBe(true)
    expect(isSection('permissions')).toBe(false)
  })

  it('tells an open Settings which section is asked for, until it stops listening', () => {
    const heard = vi.fn()
    const stop = onSectionRequest(heard)
    requestSettingsSection('engines')
    stop()
    requestSettingsSection('system')
    expect(heard.mock.calls).toEqual([['engines']])
  })
})
