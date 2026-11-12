import { describe, expect, it } from 'vitest'
import { linesOf, move, newSection, paperProblems, readPaper, removeSection, renameSection, tidyPaper, wordsOf, writePaper } from './paper'

const settings = JSON.stringify({
  title: 'Daily Bugle',
  kanji: { enabled: true, fromGrade: 2 },
  goodNews: ['City'],
  sections: [
    {
      name: 'City',
      about: 'The town.',
      feeds: [{ name: 'Gazette', url: 'https://gazette.example/feed' }],
      extra: 'kept',
    },
    {
      name: 'Sport',
      about: '',
      width: 'third',
      columns: 3,
      picture: 'huge',
      articles: 2,
      length: 'long',
      feeds: [{ name: '', url: 'https://sport.example/rss' }],
    },
  ],
})

describe('the paper editor model', () => {
  it('reads the settings with defaults for what they leave out', () => {
    const paper = readPaper(settings)!
    expect(paper.title).toBe('Daily Bugle')
    expect(paper.layout).toEqual({
      columns: 2,
      frontPicture: 'medium',
      pictures: 'medium',
    })
    expect(paper.sections[0]).toMatchObject({
      width: 'full',
      columns: null,
      picture: null,
      articles: null,
      length: 'normal',
      pictureAt: 'beside',
    })
    expect(paper.sections[1]).toMatchObject({
      width: 'third',
      columns: 3,
      picture: null,
      articles: 2,
      length: 'long',
    })
  })

  it('is undefined while the settings are not a JSON object', () => {
    expect(readPaper('{ broken')).toBeUndefined()
    expect(readPaper('[1, 2]')).toBeUndefined()
  })

  it('writes back every key it does not know, and leaves out what follows the paper', () => {
    const written = JSON.parse(writePaper(readPaper(settings)!))
    expect(written.kanji).toEqual({ enabled: true, fromGrade: 2 })
    expect(written.sections[0].extra).toBe('kept')
    expect(written.sections[0]).not.toHaveProperty('columns')
    expect(written.sections[1].columns).toBe(3)
    expect(written.layout.columns).toBe(2)
  })

  it('renames and removes a section with its good news', () => {
    const paper = readPaper(settings)!
    expect(renameSection(paper, 0, 'Town').goodNews).toEqual(['Town'])
    expect(removeSection(paper, 0).goodNews).toEqual([])
    expect(removeSection(paper, 0).sections.map((s) => s.name)).toEqual(['Sport'])
  })

  it('moves sections up and down, never past the ends', () => {
    expect(move(['a', 'b', 'c'], 0, 1)).toEqual(['b', 'a', 'c'])
    expect(move(['a', 'b', 'c'], 0, -1)).toEqual(['a', 'b', 'c'])
    expect(move(['a', 'b', 'c'], 2, 1)).toEqual(['a', 'b', 'c'])
  })

  it('names a new section so it never clashes', () => {
    expect(newSection(['New section']).name).toBe('New section 2')
    expect(newSection([]).feeds).toEqual([{ name: '', url: '' }])
  })

  it('says what keeps the paper from being saved', () => {
    const paper = readPaper(settings)!
    expect(paperProblems(paper)).toEqual([])
    const broken = {
      ...paper,
      sections: [
        {
          ...paper.sections[0],
          name: ' ',
          feeds: [{ name: 'x', url: 'ftp://nope' }],
        },
        { ...paper.sections[1], feeds: [] },
      ],
    }
    expect(paperProblems(broken)).toEqual(['Section 1 needs a name.', 'Section 1 has a source that is not a web address.', 'Sport needs at least one source.'])
    expect(paperProblems({ ...paper, sections: [] })).toEqual(['The paper needs at least one section.'])
  })

  it('drops empty sources and names the nameless after their site before saving', () => {
    const paper = readPaper(settings)!
    const tidy = tidyPaper({
      ...paper,
      sections: [
        {
          ...paper.sections[1],
          feeds: [...paper.sections[1].feeds, { name: 'Empty', url: ' ' }],
        },
      ],
    })
    expect(tidy.sections[0].feeds).toEqual([{ name: 'sport.example', url: 'https://sport.example/rss' }])
  })

  it('turns text boxes into lists', () => {
    expect(linesOf(' one\n\ntwo \n')).toEqual(['one', 'two'])
    expect(wordsOf('strike, rent , ')).toEqual(['strike', 'rent'])
  })
})
