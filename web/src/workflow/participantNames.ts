import { reactive } from 'vue'

/** 指定成员 Id → 姓名。设计器卡片和选人器共用，不写入流程模型。 */
export const participantNames = reactive<Record<string, string>>({})

export function rememberParticipantName(id: number | string | null | undefined, name: string | null | undefined) {
  const text = name?.trim()
  if (id == null || id === '' || !text) return
  participantNames[String(id)] = text
}

export function rememberParticipantNames(
  entries: Record<string, string | null | undefined> | null | undefined,
) {
  if (!entries) return
  for (const [id, name] of Object.entries(entries)) rememberParticipantName(id, name)
}

export function clearParticipantNames() {
  for (const key of Object.keys(participantNames)) delete participantNames[key]
}
