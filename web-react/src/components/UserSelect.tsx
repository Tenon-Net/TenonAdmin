import { ApiSelect, type ApiFetch, type ApiSelectOption, type ApiSelectProps } from './ApiSelect'
import { userApi } from '@/api'
import { wfParticipantApi } from '@/api/workflow'
import type { ApiId, UserItem } from '@/types/api'
import { rememberParticipantName } from '@/workflow/participantNames'

/** 用户 → 选项:label「姓名(账号)」,value=id。纯逻辑,变异钉。 */
export function toUserOption(u: UserItem): ApiSelectOption {
  return { label: `${u.name}(${u.account})`, value: u.id }
}

/** 办理人选项。disabledIds 里的人仍列出,但不可选,并在标签后附上原因。 */
export function toParticipantOption(
  user: { id: string | number; name: string; account: string },
  disabledIds?: ReadonlyArray<string | number | null | undefined>,
  disabledSuffix?: string,
): ApiSelectOption & { disabled?: boolean } {
  const blocked = (disabledIds ?? []).some((id) => id != null && id !== '' && String(id) === String(user.id))
  const base = `${user.name}(${user.account})`
  return {
    label: blocked && disabledSuffix ? `${base}（${disabledSuffix}）` : base,
    value: user.id,
    ...(blocked ? { disabled: true } : {}),
  }
}

export interface UserSelectProps extends Omit<ApiSelectProps, 'fetch' | 'reloadOn'> {
  /** 按部门过滤(变化即重拉);不传 = 全员。仅 source=admin 生效。 */
  orgId?: ApiId | null
  pageSize?: number
  /** admin=用户管理分页;workflow=审批选人,普通办理人可用。 */
  source?: 'admin' | 'workflow'
  /** 仍出现在列表里,但不可选。转办/委托用来挡住发起人。 */
  disabledIds?: Array<string | number | null | undefined>
  /** 禁用项标签后缀,通常是已翻译的原因。 */
  disabledSuffix?: string
}

/**
 * 人员选择器:基于 ApiSelect。
 * source=admin 时 fetch = userApi.page;source=workflow 时走审批候选项,只含姓名和账号。
 * 单/多选、placeholder 等一切经 rest 透传给底层 Select。
 */
export function UserSelect({
  orgId,
  pageSize = 50,
  source = 'admin',
  disabledIds,
  disabledSuffix,
  ...rest
}: UserSelectProps) {
  const disabledKey = (disabledIds ?? []).filter((id) => id != null && id !== '').join(',')
  const fetch: ApiFetch = async (keyword) => {
    if (source === 'workflow') {
      const { items } = await wfParticipantApi.page({ page: 1, pageSize, keyword: keyword || undefined })
      return items.flatMap((u) => {
        if (u.id == null || !u.name || !u.account) return []
        rememberParticipantName(u.id, u.name)
        return [toParticipantOption({ id: u.id, name: u.name, account: u.account }, disabledIds, disabledSuffix)]
      })
    }
    const { items } = await userApi.page({ page: 1, pageSize, name: keyword || undefined, orgId: orgId ?? undefined })
    return items.flatMap((u) => {
      if (u.id == null || !u.name) return []
      rememberParticipantName(u.id, u.name)
      return [toUserOption(u)]
    })
  }
  return (
    <ApiSelect
      fetch={fetch}
      reloadOn={source === 'workflow' ? `workflow|${disabledSuffix ?? ''}|${disabledKey}` : orgId}
      {...rest}
    />
  )
}
