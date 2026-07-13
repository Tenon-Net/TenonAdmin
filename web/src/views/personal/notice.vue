<script setup lang="ts">
// 我的通知(个人页,非管理端):列出发给自己的通知并读正文、标记已读。
// 走 [ActiveSession] 的 notice/mine —— 任何登录用户可调,故本页注册为静态路由,不进菜单/权限系统。
// 正文随列表一起返回(NoticeMineItem.content),点"查看"不再发请求。
import { h, ref } from 'vue'
import { NButton, NModal, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import MarkdownView from '@/components/MarkdownEditor/MarkdownView.vue'
import { useConfirm } from '@/composables/useConfirm'
import { noticeApi } from '@/api'
import { NoticeType, type NoticeMineItem } from '@/types/api'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const tableRef = ref<ProTableInst<NoticeMineItem>>()

const columns: ProTableColumn<NoticeMineItem>[] = [
  {
    key: 'isRead',
    title: () => t('notice.status'),
    width: 80,
    render: (r) =>
      h(NTag, { size: 'small', type: r.isRead ? 'default' : 'success', bordered: false }, () =>
        r.isRead ? t('notice.read') : t('notice.unread'),
      ),
  },
  { key: 'title', title: () => t('notice.noticeTitle') },
  {
    key: 'type',
    title: () => t('notice.type'),
    width: 90,
    render: (r) =>
      h(NTag, { size: 'small', type: r.type === NoticeType.Announcement ? 'warning' : 'info', bordered: false }, () =>
        r.type === NoticeType.Announcement ? t('notice.typeAnnouncement') : t('notice.typeNotice'),
      ),
  },
  { key: 'publishTime', title: () => t('notice.publishTime'), width: 170, render: (r) => (r.publishTime ?? '').replace('T', ' ').slice(0, 19) },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 80,
    hideInSetting: true,
    render: (r) => h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openView(r) }, () => t('notice.view')),
  },
]

// ── 查看正文(行里已带 content,不发请求)+ 顺手标记已读 ──
const showView = ref(false)
const viewRow = ref<NoticeMineItem | null>(null)
async function openView(r: NoticeMineItem) {
  viewRow.value = r
  showView.value = true
  if (r.isRead) return
  try {
    await noticeApi.markRead(r.id)
    await tableRef.value?.refresh() // 顶栏未读角标由其自身的 30s 轮询自愈
  } catch (e) {
    message.error(translateError(e))
  }
}

async function markAll() {
  if (await run(() => noticeApi.markAllRead(), t('notice.allRead'))) await tableRef.value?.refresh()
}
</script>

<template>
  <div>
    <ProTable
      ref="tableRef"
      :columns="columns"
      :fetcher="noticeApi.mine"
      storage-key="personal-notice"
      @error="(e) => message.error(translateError(e))"
    >
      <template #toolbar>
        <n-button @click="markAll">
          <template #icon><AppIcon icon="ph:checks" :size="16" /></template>{{ t('app.notice.markAllRead') }}
        </n-button>
      </template>
    </ProTable>

    <n-modal
      v-model:show="showView"
      preset="card"
      :title="viewRow?.title || t('notice.detailTitle')"
      style="width: 720px; max-width: 92vw"
    >
      <MarkdownView :value="viewRow?.content" />
    </n-modal>
  </div>
</template>
