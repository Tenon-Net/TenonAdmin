<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NDropdown,
  NBadge,
  NTooltip,
  NMenu,
  NModal,
  NBreadcrumb,
  NBreadcrumbItem,
  useMessage,
  type DropdownOption,
} from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useFullscreen, useIntervalFn } from '@vueuse/core'
import { useI18n } from 'vue-i18n'
import { useAppStore, type Locale } from '@/stores/app'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { useLayoutMenu } from '@/composables/useLayoutMenu'
import { useMenuFlat } from '@/composables/useMenuFlat'
import { useSite } from '@/composables/useSite'
import { authApi, noticeApi } from '@/api'
import type { NoticeMineItem } from '@/types/api'
import { resetRouter } from '@/router'
import { translateError } from '@/utils/error'
import TenonLogo from '@/components/TenonLogo.vue'
import SettingsDrawer from './SettingsDrawer.vue'
import MenuSearch from '@/components/MenuSearch.vue'
import MarkdownView from '@/components/MarkdownEditor/MarkdownView.vue'

withDefaults(
  defineProps<{
    showCollapse?: boolean
    showBrand?: boolean
    topMenu?: 'full' | 'l1' | 'l2' | null
  }>(),
  { showCollapse: true, showBrand: false, topMenu: null },
)

const app = useAppStore()
const { site } = useSite()
const user = useUserStore()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const message = useMessage()
const { t } = useI18n()
const { isFullscreen, toggle: toggleFullscreen } = useFullscreen()
const { menuOptions, l1Options, l2Options, activeKey, selectedL1, onSelect, onSelectL1 } = useLayoutMenu()
const flat = useMenuFlat()

const settingsOpen = ref(false)
const searchOpen = ref(false)

const title = computed(() => {
  const m = route.meta.title as string | undefined
  if (!m) return ''
  return m.includes('.') ? t(m) : m
})
const crumbs = computed(() => flat.value.find((l) => l.path === route.path)?.breadcrumb ?? [])

// 语言名按各自书写系统展示,不随当前 locale 翻译。
const localeOptions: DropdownOption[] = [
  { label: '简体中文', key: 'zh-CN' },
  { label: 'English', key: 'en-US' },
]
function onLocale(key: string) {
  app.setLocale(key as Locale)
}

const userOptions = computed<DropdownOption[]>(() => [
  { label: t('app.profile'), key: 'profile' },
  { label: t('app.password'), key: 'password' },
  { type: 'divider', key: 'd1' },
  { label: t('app.logout'), key: 'logout' },
])
async function onUser(key: string) {
  if (key === 'profile') router.push('/personal/profile')
  else if (key === 'password') router.push('/personal/password')
  else if (key === 'logout') await logout()
}
async function logout() {
  try {
    await authApi.logout()
  } catch (e) {
    message.error(translateError(e)) // 尽力而为,不阻断登出
  }
  resetRouter()
  auth.reset() // 内部会 clearTabs()
  user.clear()
  router.replace('/login')
}

// ── 消息通知铃铛 ──────────────────────────────────────────────
// 未读数每 30s 轮询(设计 §4 消息中心轮询模型);下拉展开时拉最近若干条,点击标记已读。
const unread = ref(0)
const recentNotices = ref<NoticeMineItem[]>([])

async function fetchUnread() {
  try {
    unread.value = await noticeApi.unreadCount()
  } catch {
    /* 轮询失败静默,不打扰用户;下次轮询自愈 */
  }
}
async function fetchRecent() {
  try {
    const { items } = await noticeApi.mine({ page: 1, pageSize: 8 })
    recentNotices.value = items
  } catch {
    /* 忽略 */
  }
}

const noticeOptions = computed<DropdownOption[]>(() => {
  const opts: DropdownOption[] = []
  if (recentNotices.value.length === 0) {
    opts.push({ key: '__empty', label: t('app.notice.empty'), disabled: true })
  } else {
    for (const n of recentNotices.value) {
      // 未读加圆点前缀,一眼可辨
      opts.push({ key: `n:${n.id}`, label: (n.isRead ? '' : '● ') + n.title })
    }
  }
  opts.push({ type: 'divider', key: '__div' })
  opts.push({ key: '__all', label: t('app.notice.markAllRead') })
  opts.push({ key: '__view', label: t('app.notice.viewAll') })
  return opts
})

// 点条目 = 看正文。正文随列表一起拿到了(NoticeMineItem.content),弹层直接渲染,不再发请求。
const showNotice = ref(false)
const viewNotice = ref<NoticeMineItem | null>(null)

async function onNoticeSelect(key: string) {
  if (key.startsWith('n:')) {
    const id = Number(key.slice(2))
    const item = recentNotices.value.find((n) => n.id === id)
    if (!item) return
    viewNotice.value = item
    showNotice.value = true
    if (item.isRead) return
    try {
      await noticeApi.markRead(id)
    } catch (e) {
      message.error(translateError(e))
    }
    await Promise.all([fetchUnread(), fetchRecent()])
  } else if (key === '__all') {
    try {
      await noticeApi.markAllRead()
      await Promise.all([fetchUnread(), fetchRecent()])
    } catch (e) {
      message.error(translateError(e))
    }
  } else if (key === '__view') {
    // 不是 /system/notice —— 那是管理员菜单,普通用户既无路由(404)也无权限(403)
    router.push('/personal/notice')
  }
}
function onBellShow(show: boolean) {
  if (show) fetchRecent()
}

// AppHeader 仅在登录后的布局内挂载,token 必在;useIntervalFn 随组件卸载(登出)自动停。
useIntervalFn(fetchUnread, 30000)
fetchUnread()
</script>

<template>
  <div class="bar">
    <div class="left">
      <n-button v-if="showCollapse" quaternary circle :aria-label="t('app.collapse')" @click="app.toggleCollapsed()">
        <Icon :icon="app.collapsed ? 'ph:list' : 'ph:sidebar-simple'" :width="20" />
      </n-button>
      <div v-if="showBrand" class="brand">
        <TenonLogo :size="26" />
        <span class="brand-name mobile-hide">{{ site.title }}</span>
      </div>
      <n-breadcrumb v-if="app.showBreadcrumb && crumbs.length">
        <n-breadcrumb-item v-for="(c, i) in crumbs" :key="i">{{ c }}</n-breadcrumb-item>
      </n-breadcrumb>
      <span v-else class="title">{{ title }}</span>
    </div>

    <div v-if="topMenu" class="center">
      <n-menu
        v-if="topMenu === 'full'"
        mode="horizontal"
        responsive
        :options="menuOptions"
        :value="activeKey"
        @update:value="onSelect"
      />
      <n-menu
        v-else-if="topMenu === 'l1'"
        mode="horizontal"
        responsive
        :options="l1Options"
        :value="selectedL1"
        @update:value="onSelectL1"
      />
      <n-menu
        v-else
        mode="horizontal"
        responsive
        :options="l2Options"
        :value="activeKey"
        @update:value="onSelect"
      />
    </div>

    <!-- mobile-hide:窄屏(<768px)藏起来的次要项。顶栏总宽 > 屏宽时右端会被 layout 的 overflow:hidden 裁掉,
         而全站唯一的登出入口就在最右边的用户下拉里 —— 手机上不能因为一排图标而退不出登录。
         搜索有 Ctrl+K 兜底、全屏在手机上无意义、语言与设置属低频(设置抽屉本就是宽屏调布局用的)。 -->
    <div class="right">
      <n-tooltip>
        <template #trigger>
          <n-button quaternary circle class="mobile-hide" @click="searchOpen = true">
            <Icon icon="ph:magnifying-glass" :width="18" />
          </n-button>
        </template>
        {{ t('app.search') }} (Ctrl+K)
      </n-tooltip>

      <n-tooltip>
        <template #trigger>
          <n-button quaternary circle class="mobile-hide" @click="toggleFullscreen()">
            <Icon :icon="isFullscreen ? 'ph:arrows-in' : 'ph:arrows-out'" :width="18" />
          </n-button>
        </template>
        {{ isFullscreen ? t('app.exitFullscreen') : t('app.fullscreen') }}
      </n-tooltip>

      <n-tooltip>
        <template #trigger>
          <n-button quaternary circle @click="app.toggleDark()">
            <Icon :icon="app.isDark ? 'ph:moon-stars' : 'ph:sun'" :width="18" />
          </n-button>
        </template>
        {{ app.isDark ? t('app.dark') : t('app.light') }}
      </n-tooltip>

      <n-dropdown :options="localeOptions" @select="onLocale">
        <n-button quaternary circle class="mobile-hide" :aria-label="t('app.language')">
          <Icon icon="ph:translate" :width="18" />
        </n-button>
      </n-dropdown>

      <n-tooltip>
        <template #trigger>
          <n-button quaternary circle class="mobile-hide" @click="settingsOpen = true">
            <Icon icon="ph:gear-six" :width="18" />
          </n-button>
        </template>
        {{ t('app.settings') }}
      </n-tooltip>

      <!-- 导航到应用选择页,而非下拉直切:应用一多下拉就难用,且"设为默认"只在选择页上。 -->
      <n-tooltip v-if="auth.modules.length > 1">
        <template #trigger>
          <n-button quaternary circle :aria-label="t('app.switchModule')" @click="router.push('/module')">
            <Icon icon="ph:squares-four" :width="18" />
          </n-button>
        </template>
        {{ t('app.switchModule') }}
      </n-tooltip>

      <n-dropdown trigger="click" :options="noticeOptions" @select="onNoticeSelect" @update:show="onBellShow">
        <n-badge :value="unread" :max="99" :show="unread > 0">
          <n-button quaternary circle :aria-label="t('app.notice.title')">
            <Icon icon="ph:bell" :width="18" />
          </n-button>
        </n-badge>
      </n-dropdown>

      <n-dropdown :options="userOptions" @select="onUser">
        <n-button quaternary :aria-label="t('app.profile')">
          <Icon icon="ph:user-circle" :width="20" />
          <span class="uname mobile-hide">{{ user.userInfo?.name ?? user.userInfo?.account ?? '' }}</span>
        </n-button>
      </n-dropdown>
    </div>

    <SettingsDrawer v-model:show="settingsOpen" />
    <MenuSearch v-model:show="searchOpen" />

    <!-- 通知正文:内容随铃铛列表一并取回,这里只渲染(Markdown,不能当纯文本直出) -->
    <n-modal
      v-model:show="showNotice"
      preset="card"
      :title="viewNotice?.title || t('notice.detailTitle')"
      style="width: 640px; max-width: 92vw"
    >
      <MarkdownView :value="viewNotice?.content" />
    </n-modal>
  </div>
</template>

<style scoped>
.bar {
  height: 100%;
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 0 16px;
}
/* 可收缩(min-width:0 + overflow):右侧那组是刚需(含唯一的登出入口),要挤先挤左边的标题/面包屑。 */
.left {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
  overflow: hidden;
}
.brand {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--color-text-primary);
  font-weight: 600;
  flex-shrink: 0;
}
.brand-name {
  font-size: var(--font-size-md);
}
.title {
  font-size: var(--font-size-md);
  font-weight: 500;
  color: var(--color-text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.uname {
  margin-left: 6px;
}
.center {
  flex: 1;
  min-width: 0;
  overflow: hidden;
}
.right {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-left: auto;
  flex-shrink: 0;
}

/* 窄屏(断点与 layouts/default.vue 的 breakpointsTailwind.smaller('md') 一致):
   只留暗色/应用切换/铃铛/用户下拉,其余隐藏,保证最右的登出入口不被裁掉。 */
@media (max-width: 767px) {
  .bar {
    gap: 8px;
    padding: 0 8px;
  }
  .mobile-hide {
    display: none;
  }
}
</style>
