import { describe, expect, it } from 'vitest'
import { readError } from './client'

describe('readError', () => {
  it('uses the problem detail when the API explains itself', async () => {
    const response = new Response(JSON.stringify({ title: 'Not allowed', detail: 'Room already exists.' }), { status: 422 })
    const error = await readError(response)
    expect(error.status).toBe(422)
    expect(error.message).toBe('Room already exists.')
  })

  it('falls back to the title, then to the status text', async () => {
    expect((await readError(new Response(JSON.stringify({ title: 'Not found' }), { status: 404 }))).message).toBe('Not found')
    expect((await readError(new Response('oops', { status: 500, statusText: 'Server Error' }))).message).toBe('Server Error')
  })
})
