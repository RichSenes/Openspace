import { describe, expect, it } from 'vitest'
import { boardName, boardOf, jobsProblems, readJobs, writeJobs, yen } from './jobs'

const settings = JSON.stringify({
  title: 'Offers',
  boards: ['japan-dev', 'linkedin:full stack developer', { board: 'greenhouse:acme', allInJapan: true }, 'tokyodev', 'daijob:engineer'],
  roles: ['developer'],
  avoid: ['Go'],
  japaneseUpTo: 'Conversational',
  minSalary: 5000000,
  most: 10,
  extra: 'kept',
})

describe('the job offers editor model', () => {
  it('reads every kind of board and the rules, with defaults for what is left out', () => {
    const jobs = readJobs(settings)!
    expect(jobs.boards.map((b) => b.kind)).toEqual(['japan-dev', 'linkedin', 'company', 'tokyodev', 'daijob'])
    expect(jobs.boards[1].value).toBe('full stack developer')
    expect(jobs.boards[2]).toEqual({ kind: 'company', value: 'greenhouse:acme', allInJapan: true })
    expect(jobs).toMatchObject({ japaneseUpTo: 'conversational', minSalary: 5000000, most: 10, fit: 3, visa: true, avoid: ['Go'] })
  })

  it('writes the boards back as names, a company that is all in Japan as an object, and keeps unknown keys', () => {
    const written = JSON.parse(writeJobs(readJobs(settings)!))
    expect(written.boards).toEqual(['japan-dev', 'linkedin:full stack developer', { board: 'greenhouse:acme', allInJapan: true }, 'tokyodev', 'daijob:engineer'])
    expect(written.extra).toBe('kept')
  })

  it('names a board as the settings do', () => {
    expect(boardName(boardOf('LinkedIn:react@Osaka'))).toBe('linkedin:react@Osaka')
    expect(boardName({ kind: 'tokyodev', value: 'ignored', allInJapan: false })).toBe('tokyodev')
  })

  it('says what keeps the settings from being saved', () => {
    const jobs = readJobs(settings)!
    expect(jobsProblems(jobs)).toEqual([])
    expect(
      jobsProblems({
        ...jobs,
        profile: ' ',
        boards: [
          { kind: 'linkedin', value: ' ', allInJapan: false },
          { kind: 'company', value: 'acme', allInJapan: false },
          { kind: 'tokyodev', value: '', allInJapan: false },
          { kind: 'tokyodev', value: '', allInJapan: false },
        ],
      }),
    ).toEqual([
      'Search 1 needs words to look for.',
      'Company page 2 is written as service and company, such as lever:acme.',
      'A place to look is there twice.',
      'Say where your profile is: the judge reads it.',
    ])
    expect(jobsProblems({ ...jobs, boards: [] })).toEqual(['Add at least one place to look for offers.'])
  })

  it('shows yen a year in millions', () => {
    expect(yen(5_000_000)).toBe('¥5M')
    expect(yen(6_500_000)).toBe('¥6.5M')
    expect(yen(0)).toBe('none')
  })

  it('is undefined while the settings are not a JSON object', () => {
    expect(readJobs('[1]')).toBeUndefined()
    expect(readJobs('{ broken')).toBeUndefined()
  })
})
