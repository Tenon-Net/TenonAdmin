<script setup lang="ts">
// 顶栏消息通知铃铛。富面板(n-popover):无页签单列表 + 正文弹层。
// 未读=圆点+加粗+淡主色底,已读=整行变淡;"读没读"是过滤器不是分类,不做成页签(筛选在个人通知页)。
// 未读数每 30s 轮询(设计 §4 消息中心轮询模型);面板展开时拉取列表。
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  NPopover, NBadge, NButton, NTag, NEllipsis, NScrollbar, NText, NEmpty, NModal, NSpin,
  useMessage,
} from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useIntervalFn } from '@vueuse/core'
import { useI18n } from 'vue-i18n'
import { noticeApi } from '@/api'
import { noticeBus } from '@/composables/useRealtime'
import { NoticeType, type NoticeMineItem } from '@/types/api'
import MarkdownView from '@/components/MarkdownEditor/MarkdownView.vue'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const router = useRouter()
const message = useMessage()

const unread = ref(0)
const items = ref<NoticeMineItem[]>([])
const loading = ref(false)

async function fetchUnread() {
  try {
    unread.value = await noticeApi.unreadCount()
  } catch {
    /* 轮询失败静默,下次自愈 */
  }
}
// 无页签单列表:恒拉最近一页混排,未读靠条目样式强调;筛选能力在"查看全部"的个人通知页。
async function fetchList() {
  loading.value = true
  try {
    const { items: list } = await noticeApi.mine({ page: 1, pageSize: 10 })
    items.value = list
  } catch {
    items.value = []
  } finally {
    loading.value = false
  }
}
function onShow(show: boolean) {
  if (show) fetchList()
}

// 正文弹层:内容随列表取回,直接渲染(Markdown,不能纯文本直出);点条目即标记已读。
const showNotice = ref(false)
const viewNotice = ref<NoticeMineItem | null>(null)
async function openNotice(item: NoticeMineItem) {
  viewNotice.value = item
  showNotice.value = true
  if (item.isRead) return
  try {
    await noticeApi.markRead(item.id)
    item.isRead = true
    await fetchUnread()
  } catch (e) {
    message.error(translateError(e))
  }
}
async function markAllRead() {
  try {
    await noticeApi.markAllRead()
    await Promise.all([fetchUnread(), fetchList()])
  } catch (e) {
    message.error(translateError(e))
  }
}
function viewAll() {
  router.push('/personal/notice')
}

// 类型 → 标签色/文案。新增类型在此补一行即可。
const tagType = (ty: NoticeType) =>
  ty === NoticeType.Announcement ? 'warning' : ty === NoticeType.Message ? 'success' : 'info'
const typeLabel = (ty: NoticeType) =>
  ty === NoticeType.Announcement ? t('notice.typeAnnouncement')
    : ty === NoticeType.Message ? t('notice.typeMessage')
      : t('notice.typeNotice')

// 正文摘要:去掉常见 Markdown 记号,压空白,截断。ponytail: 正则够用,要精准渲染再引 md 解析。
function snippet(md?: string | null) {
  if (!md) return ''
  const s = md
    .replace(/!\[[^\]]*\]\([^)]*\)/g, '') // 图片
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1') // 链接留文字
    .replace(/[#>*_`~-]/g, '') // 记号
    .replace(/\s+/g, ' ')
    .trim()
  return s.length > 60 ? s.slice(0, 60) + '…' : s
}
// 短时间戳(本地,YYYY-MM-DD HH:mm)。ponytail: 够看,要"3 分钟前"再引 formatTimeAgo。
const shortTime = (iso: string) => (iso ?? '').replace('T', ' ').slice(0, 16)

// 双腿:实时推送到达即刷新未读(SignalR 开启时,即时);30s 轮询兜底(实时关闭/连接掉线时)。
// VueUse 的 useEventBus.on 在组件卸载时自动退订。
noticeBus.on(() => { fetchUnread() })
useIntervalFn(fetchUnread, 30000)
fetchUnread()
</script>

<template>
  <n-popover trigger="click" placement="bottom-end" :show-arrow="false" style="padding: 0" @update:show="onShow">
    <template #trigger>
      <n-badge :value="unread" :max="99" :show="unread > 0">
        <n-button quaternary circle :aria-label="t('app.notice.title')">
          <Icon icon="ph:bell" :width="18" />
        </n-button>
      </n-badge>
    </template>

    <div class="notice-panel">
      <div class="notice-head">
        <span class="notice-title">{{ t('app.notice.title') }}</span>
        <n-button text size="tiny" :disabled="unread === 0" @click="markAllRead">
          <template #icon><Icon icon="ph:checks" :width="15" /></template>
          {{ t('app.notice.markAllRead') }}
        </n-button>
      </div>

      <n-spin :show="loading">
        <n-scrollbar style="max-height: 360px">
          <div v-if="items.length === 0" class="notice-empty">
            <n-empty :description="t('app.notice.empty')" size="small" />
          </div>
          <ul v-else class="notice-list">
            <li v-for="n in items" :key="n.id" :class="['notice-item', n.isRead ? 'read' : 'unread']" @click="openNotice(n)">
              <span v-if="!n.isRead" class="dot" />
              <div class="notice-body">
                <div class="line1">
                  <n-tag :type="tagType(n.type)" size="tiny" :bordered="false" round>{{ typeLabel(n.type) }}</n-tag>
                  <n-ellipsis class="item-title" :class="{ unread: !n.isRead }">{{ n.title }}</n-ellipsis>
                </div>
                <n-text v-if="snippet(n.content)" depth="3" class="item-snippet">{{ snippet(n.content) }}</n-text>
                <n-text depth="3" class="item-time">{{ shortTime(n.publishTime) }}</n-text>
              </div>
            </li>
          </ul>
        </n-scrollbar>
      </n-spin>

      <div class="notice-foot">
        <n-button text size="small" @click="viewAll">
          {{ t('app.notice.viewAll') }}
          <template #icon><Icon icon="ph:arrow-right" :width="14" /></template>
        </n-button>
      </div>
    </div>
  </n-popover>

  <n-modal
    v-model:show="showNotice"
    preset="card"
    :title="viewNotice?.title || t('notice.detailTitle')"
    style="width: 640px; max-width: 92vw"
  >
    <MarkdownView :value="viewNotice?.content" />
  </n-modal>
</template>

<style scoped>
.notice-panel {
  width: 360px;
  max-width: 92vw;
}
.notice-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px 4px;
}
.notice-title {
  font-weight: 600;
  font-size: var(--font-size-md);
}
.notice-empty {
  padding: 32px 0;
}
.notice-list {
  margin: 0;
  padding: 0;
  list-style: none;
}
.notice-item {
  position: relative;
  display: flex;
  gap: 8px;
  padding: 12px 16px;
  cursor: pointer;
  border-top: 1px solid var(--n-divider-color, rgba(128, 128, 128, 0.15));
  transition: background-color 0.2s;
}
.notice-item:hover {
  background-color: rgba(128, 128, 128, 0.08);
}
/* 未读:整行淡主色底(明暗主题都成立);已读:整行变淡 */
.notice-item.unread {
  background-color: color-mix(in srgb, var(--color-primary) 4%, transparent);
}
.notice-item.unread:hover {
  background-color: color-mix(in srgb, var(--color-primary) 9%, transparent);
}
.notice-item.read {
  opacity: 0.55;
}
.dot {
  position: absolute;
  left: 6px;
  top: 18px;
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--color-primary, #d03050);
}
.notice-body {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.line1 {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}
.item-title {
  flex: 1;
  min-width: 0;
  font-size: var(--font-size-sm);
}
.item-title.unread {
  font-weight: 600;
}
.item-snippet {
  font-size: 12px;
  line-height: 1.4;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.item-time {
  font-size: 11px;
}
.notice-foot {
  display: flex;
  justify-content: center;
  padding: 8px;
  border-top: 1px solid var(--n-divider-color, rgba(128, 128, 128, 0.15));
}
</style>
