<script setup lang="ts">
// 在线会话 = 只读 ProTable + 行内「强制下线」。无表单。踢人走 useConfirm 二次确认(warning),
// 踢自己的会话置灰(防误踢自己下线)。后端仅按 UserId 过滤,故不设业务搜索列。
import { h, ref } from 'vue'
import { NButton, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import { useConfirm } from '@/composables/useConfirm'
import { useProTableLabels } from '@/composables/useProTableLabels'
import { useUserStore } from '@/stores/user'
import { sessionApi } from '@/api'
import { translateError } from '@/utils/error'
import type { OnlineSessionItem } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { confirm } = useConfirm()
const labels = useProTableLabels()
const userStore = useUserStore()
const tableRef = ref<ProTableInst<OnlineSessionItem>>()

function kick(r: OnlineSessionItem) {
  confirm({
    type: 'warning',
    content: t('session.kickConfirm', { account: r.account }),
    action: () => sessionApi.kick(r.sessionId),
    successMsg: t('session.kicked'),
  }).then((ok) => {
    if (ok) tableRef.value?.refresh()
  })
}

const columns: ProTableColumn<OnlineSessionItem>[] = [
  { key: 'account', title: () => t('session.account') },
  { key: 'ip', title: () => t('session.ip'), render: (r) => r.ip || '—' },
  { key: 'loginTime', title: () => t('session.loginTime'), format: 'datetime' },
  { key: 'expiresAt', title: () => t('session.expiresAt'), format: 'datetime' },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 120,
    hideInSetting: true,
    render: (r) => {
      const isSelf = r.userId === userStore.userInfo?.userId
      return h(
        NButton,
        {
          size: 'small',
          quaternary: true,
          type: 'error',
          // 踢自己的会话置灰:防误把自己下线
          disabled: isSelf,
          onClick: () => kick(r),
        },
        () => (isSelf ? t('session.self') : t('session.kick')),
      )
    },
  },
]
</script>

<template>
  <ProTable
    ref="tableRef"
    :columns="columns"
    :fetcher="sessionApi.online"
    :title="t('session.title')"
    :labels="labels"
    storage-key="sys-session"
    @error="(e) => message.error(translateError(e))"
  />
</template>
