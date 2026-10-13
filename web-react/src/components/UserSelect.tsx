import { ApiSelect, type ApiFetch, type ApiSelectOption, type ApiSelectProps } from './ApiSelect'
import { userApi } from '@/api'
import { wfParticipantApi } from '@/api/workflow'
import type { UserItem } from '@/types/api'

/** 用户 → 选项:label「姓名(账号)」,value=id。纯逻辑,变异钉。 */
export function toUserOption(u: UserItem): ApiSelectOption {
  return { label: `${u.name}(${u.account})`, value: u.id }
}

export interface UserSelectProps extends Omit<ApiSelectProps, 'fetch' | 'reloadOn'> {
  /** 按部门过滤(变化即重拉);不传 = 全员。仅 source=admin 生效。 */
  orgId?: number | null
  pageSize?: number
  /** admin=用户管理分页;workflow=审批选人,普通办理人可用。 */
  source?: 'admin' | 'workflow'
}

/**
 * 人员选择器:基于 ApiSelect。
 * source=admin 时 fetch = userApi.page;source=workflow 时走审批候选项,只含姓名和账号。
 * 单/多选、placeholder 等一切经 rest 透传给底层 Select。
 */
export function UserSelect({ orgId, pageSize = 50, source = 'admin', ...rest }: UserSelectProps) {
  const fetch: ApiFetch = async (keyword) => {
    if (source === 'workflow') {
      const { items } = await wfParticipantApi.page({ page: 1, pageSize, keyword: keyword || undefined })
      return items.flatMap((u) =>
        u.id == null || !u.name || !u.account ? [] : [{ label: `${u.name}(${u.account})`, value: u.id }],
      )
    }
    const { items } = await userApi.page({ page: 1, pageSize, name: keyword || undefined, orgId: orgId ?? undefined })
    return items.map(toUserOption)
  }
  return <ApiSelect fetch={fetch} reloadOn={source === 'workflow' ? 'workflow' : orgId} {...rest} />
}
