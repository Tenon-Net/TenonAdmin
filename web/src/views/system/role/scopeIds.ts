import type { ApiId } from '@/types/api'
import { parseQueryApiId } from '@/utils/id'

/** 数据范围存库为逗号字符串；安全整数还原为 number 以匹配树节点，其余十进制 Id 保留字符串。 */
export function parseCustomOrgIds(value: string | null | undefined): ApiId[] {
  if (!value) return []
  return value.split(',').flatMap((id) => {
    const parsed = parseQueryApiId(id)
    return parsed == null ? [] : [parsed]
  })
}
