import { useSyncExternalStore } from 'react'

/** 指定成员 Id → 姓名。设计器卡片和选人器共用，不写入流程模型。 */
const names: Record<string, string> = {}
let snapshot: Record<string, string> = {}
const listeners = new Set<() => void>()

function publish() {
  snapshot = { ...names }
  listeners.forEach((listener) => listener())
}

export function rememberParticipantName(id: number | string | null | undefined, name: string | null | undefined) {
  const text = name?.trim()
  if (id == null || id === '' || !text || names[String(id)] === text) return
  names[String(id)] = text
  publish()
}

export function rememberParticipantNames(
  entries: Record<string, string | null | undefined> | null | undefined,
) {
  if (!entries) return
  for (const [id, name] of Object.entries(entries)) rememberParticipantName(id, name)
}

export function clearParticipantNames() {
  for (const key of Object.keys(names)) delete names[key]
  publish()
}

export function useParticipantNames(): Record<string, string> {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => snapshot,
    () => snapshot,
  )
}
