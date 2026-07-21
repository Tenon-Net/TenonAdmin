import { ApiSelect, type ApiFetch, type ApiSelectOption, type ApiSelectProps } from './ApiSelect'
import { userApi } from '@/api'
import type { UserItem } from '@/types/api'

/** 用户 → 选项:label「姓名(账号)」,value=id。纯逻辑,变异钉。 */
export function toUserOption(u: UserItem): ApiSelectOption {
  return { label: `${u.name}(${u.account})`, value: u.id }
}

export interface UserSelectProps extends Omit<ApiSelectProps, 'fetch' | 'reloadOn'> {
  /** 按部门过滤(变化即重拉);不传 = 全员 */
  orgId?: number | null
  pageSize?: number
}

/**
 * 人员选择器:基于 ApiSelect,fetch = userApi.page(name 搜索 + 可选 orgId 部门过滤)。
 * 单/多选、placeholder 等一切经 rest 透传给底层 Select。
 */
export function UserSelect({ orgId, pageSize = 50, ...rest }: UserSelectProps) {
  const fetch: ApiFetch = async (keyword) => {
    const { items } = await userApi.page({ page: 1, pageSize, name: keyword || undefined, orgId: orgId ?? undefined })
    return items.map(toUserOption)
  }
  return <ApiSelect fetch={fetch} reloadOn={orgId} {...rest} />
}
