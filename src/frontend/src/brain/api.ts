import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { request } from '../api/client'
import type { Backlink, Graph, Note, NoteCommit, NoteVersion, SearchHit, TreeFolder, WhyReason } from './types'

/** A note's path in a URL: each folder encoded on its own, the slashes kept. */
const at = (path: string) => path.split('/').map(encodeURIComponent).join('/')

export function useBrainTree() {
  return useQuery({ queryKey: ['brain', 'tree'], queryFn: () => request<TreeFolder>('GET', '/api/brain/tree'), refetchInterval: 30_000 })
}

export function useBrainGraph() {
  return useQuery({ queryKey: ['brain', 'graph'], queryFn: () => request<Graph>('GET', '/api/brain/graph'), refetchInterval: 60_000 })
}

export function useNote(path: string | undefined) {
  return useQuery({
    queryKey: ['brain', 'note', path],
    queryFn: () => request<Note>('GET', `/api/brain/notes/${at(path!)}`),
    enabled: !!path,
    // Now.md changes under the reader's eyes.
    refetchInterval: path === 'Now.md' ? 10_000 : false,
  })
}

/** The whole file, front matter included, for the editor. */
export function useNoteText(path: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: ['brain', 'text', path],
    queryFn: () => request<{ text: string }>('GET', `/api/brain/notes/${at(path!)}/text`),
    enabled: !!path && enabled,
    staleTime: 0,
  })
}

export function useBacklinks(path: string | undefined) {
  return useQuery({
    queryKey: ['brain', 'backlinks', path],
    queryFn: () => request<Backlink[]>('GET', `/api/brain/notes/${at(path!)}/backlinks`),
    enabled: !!path,
  })
}

export function useHistory(path: string | undefined) {
  return useQuery({
    queryKey: ['brain', 'history', path],
    queryFn: () => request<NoteCommit[]>('GET', `/api/brain/notes/${at(path!)}/history?limit=50`),
    enabled: !!path,
  })
}

export function useVersion(path: string | undefined, commit: string | undefined) {
  return useQuery({
    queryKey: ['brain', 'version', path, commit],
    queryFn: () => request<NoteVersion>('GET', `/api/brain/notes/${at(path!)}/history/${commit}`),
    enabled: !!path && !!commit,
    staleTime: Infinity,
  })
}

export function useBrainSearch(text: string) {
  const q = text.trim()
  return useQuery({
    queryKey: ['brain', 'search', q],
    queryFn: () => request<SearchHit[]>('GET', `/api/brain/search?q=${encodeURIComponent(q)}&limit=50`),
    enabled: q.length > 0,
    placeholderData: keepPreviousData,
  })
}

export function useWhy(file: string) {
  const path = file.trim()
  return useQuery({
    queryKey: ['brain', 'why', path],
    queryFn: () => request<WhyReason[]>('GET', `/api/brain/why?path=${encodeURIComponent(path)}`),
    enabled: path.length > 0,
  })
}

/** Writes the whole note as the person; the tree, the map and the note are read again. */
export function useSaveNote(path: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (edit: { text: string; summary?: string }) => request<Note>('PUT', `/api/brain/notes/${at(path)}`, edit),
    onSuccess: async (note) => {
      client.setQueryData(['brain', 'note', path], note)
      await client.invalidateQueries({ queryKey: ['brain'] })
    },
  })
}
