<script setup lang="ts">
// 人员选择器:基于 ApiSelect。
// source=admin 走用户管理分页(需权限);source=workflow 走审批选人,只返回启用用户的姓名和账号。
// 选项 label 为「姓名(账号)」,value 为用户 id。单/多选、placeholder 等一切经 $attrs 透传。
import { computed } from 'vue'
import type { SelectOption } from 'naive-ui'
import ApiSelect from '@/components/ApiSelect/index.vue'
import { userApi } from '@/api'
import { wfParticipantApi } from '@/api/workflow'
import { toParticipantOption } from './options'
import { rememberParticipantName } from '@/workflow/participantNames'
import type { ApiId } from '@/types/api'

defineOptions({ inheritAttrs: false })
const props = withDefaults(defineProps<{
  orgId?: ApiId | null
  pageSize?: number
  source?: 'admin' | 'workflow'
  /** 仍出现在列表里,但不可选。转办/委托用来挡住发起人。 */
  disabledIds?: Array<ApiId | null | undefined>
  /** 禁用项标签后缀,通常是已翻译的原因。 */
  disabledSuffix?: string
}>(), { pageSize: 50, source: 'admin' })

async function fetch(keyword: string): Promise<SelectOption[]> {
  if (props.source === 'workflow') {
    const { items } = await wfParticipantApi.page({
      page: 1,
      pageSize: props.pageSize,
      keyword: keyword || undefined,
    })
    return items.flatMap((u) => {
      if (u.id == null || !u.name || !u.account) return []
      rememberParticipantName(u.id, u.name)
      return [toParticipantOption({ id: u.id, name: u.name, account: u.account }, props.disabledIds, props.disabledSuffix)]
    })
  }
  const { items } = await userApi.page({
    page: 1,
    pageSize: props.pageSize,
    name: keyword || undefined,
    orgId: props.orgId ?? undefined,
  })
  return items.flatMap((u) => {
    if (u.id == null || !u.name) return []
    rememberParticipantName(u.id, u.name)
    return [{ label: `${u.name}(${u.account})`, value: u.id }]
  })
}
// 部门过滤、或转办里「发起人不可选」名单变化 → 重新拉,避免首屏还没拿到发起人 id 时把人放成可选。
const reloadOn = computed(() =>
  props.source === 'workflow'
    ? `${props.disabledSuffix ?? ''}|${(props.disabledIds ?? []).filter((id) => id != null && id !== '').join(',')}`
    : props.orgId,
)
</script>

<template>
  <ApiSelect :fetch="fetch" :reload-on="reloadOn" v-bind="$attrs" />
</template>
