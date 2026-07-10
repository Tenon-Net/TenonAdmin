<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { RouterView } from 'vue-router'
import { NWatermark, NDrawer, NDrawerContent, NButton } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useBreakpoints, breakpointsTailwind } from '@vueuse/core'
import { useI18n } from 'vue-i18n'
import { useAppStore, type LayoutMode } from '@/stores/app'
import { useUserStore } from '@/stores/user'
import { useLayoutMenu } from '@/composables/useLayoutMenu'
import { useTabsStore } from '@/stores/tabs'
import AppHeader from './AppHeader.vue'
import SideNav from './components/SideNav.vue'
import TabsBar from './TabsBar.vue'

const { t } = useI18n()
const app = useAppStore()
const user = useUserStore()
const tabs = useTabsStore()
const { menuOptions, l1Options, l2Options, selectedL1, activeKey, onSelect, onSelectL1 } = useLayoutMenu()

const isMobile = useBreakpoints(breakpointsTailwind).smaller('md')
const mobileOpen = ref(false)

// 窄屏忽略 layoutMode,统一走 mobile(单列 + 抽屉侧栏)。
const mode = computed<LayoutMode | 'mobile'>(() => (isMobile.value ? 'mobile' : app.layoutMode))

// 除 horizontal(无侧栏)外都有侧栏。
const withSider = new Set<LayoutMode>([
  'vertical',
  'vertical-mix',
  'vertical-hybrid-header-first',
  'top-hybrid-sidebar-first',
  'top-hybrid-header-first',
])
// 顶栏显示品牌的模式(其余由侧栏/rail 顶部显示品牌,保证每种布局品牌只出现一处)。
const headerBrandModes = new Set<string>([
  'horizontal',
  'vertical-hybrid-header-first',
  'top-hybrid-sidebar-first',
  'top-hybrid-header-first',
])
const showSider = computed(() => !isMobile.value && withSider.has(app.layoutMode))
const sideShowBrand = computed(() => mode.value === 'vertical' || mode.value === 'vertical-mix')
const headerShowBrand = computed(() => isMobile.value || headerBrandModes.has(mode.value))
// 顶栏菜单:horizontal=完整树;两个 header-first=一级;侧边优先=二级(选中的一级的子菜单)。
const headerTopMenu = computed<'full' | 'l1' | 'l2' | null>(() => {
  switch (mode.value) {
    case 'horizontal':
      return 'full'
    case 'vertical-hybrid-header-first':
    case 'top-hybrid-header-first':
      return 'l1'
    case 'top-hybrid-sidebar-first':
      return 'l2'
    default:
      return null
  }
})
// 折叠按钮:有可折叠树侧栏的模式(侧边优先的一级 rail 固定宽,不折叠)。
const collapsibleSider = new Set<string>([
  'vertical',
  'vertical-mix',
  'vertical-hybrid-header-first',
  'top-hybrid-header-first',
])
const showCollapse = computed(() => !isMobile.value && collapsibleSider.has(mode.value))

// 非特殊侧栏内容:vertical=完整树;两个 header-first hybrid=二级子菜单树。
const asideOptions = computed(() => (mode.value === 'vertical' ? menuOptions.value : l2Options.value))
const asideStyle = computed(() => {
  if (mode.value === 'vertical-mix') {
    return { width: app.collapsed ? 'var(--rail-w)' : 'calc(var(--rail-w) + var(--sidebar-w))' }
  }
  if (mode.value === 'top-hybrid-sidebar-first') {
    return { width: 'var(--rail-w)' } // 一级 icon rail,固定宽
  }
  return { width: app.collapsed ? 'var(--sidebar-w-collapsed)' : 'var(--sidebar-w)' }
})

const transitionName = computed(() => (app.pageTransition === 'none' ? undefined : app.pageTransition))

const watermarkContent = computed(
  () => [user.userInfo?.name, app.watermarkText].filter(Boolean).join(' · ') || 'TenonAdmin',
)
const watermarkColor = computed(() => (app.isDark ? 'rgba(255,255,255,0.12)' : 'rgba(0,0,0,0.06)'))

function onMobileSelect(key: string) {
  onSelect(key)
  mobileOpen.value = false
}

// 刷新标签:store 置 excludeName(逐出缓存)+ 递增 reloadKey;这里 v-if 关开一次令当前页重挂,再放行缓存。
const rvShow = ref(true)
watch(
  () => tabs.reloadKey,
  async () => {
    rvShow.value = false
    await nextTick()
    rvShow.value = true
    await nextTick()
    tabs.excludeName = ''
  },
)
</script>

<template>
  <div class="layout" :class="`mode-${mode}`">
    <!-- 侧栏 -->
    <aside v-if="showSider" class="aside" :class="{ 'aside--mix': mode === 'vertical-mix' }" :style="asideStyle">
      <template v-if="mode === 'vertical-mix'">
        <SideNav class="rail" :options="l1Options" :value="selectedL1" rail show-brand @select="onSelectL1" />
        <SideNav v-show="!app.collapsed" class="l2col" :options="l2Options" :value="activeKey" @select="onSelect" />
      </template>
      <!-- 顶部混合-侧边优先:侧栏只放一级 icon rail(二级在顶栏) -->
      <SideNav
        v-else-if="mode === 'top-hybrid-sidebar-first'"
        :options="l1Options"
        :value="selectedL1"
        rail
        @select="onSelectL1"
      />
      <SideNav
        v-else
        :options="asideOptions"
        :value="activeKey"
        :collapsed="app.collapsed"
        :show-brand="sideShowBrand"
        @select="onSelect"
      />
    </aside>

    <!-- 顶栏 -->
    <header class="header" :class="{ 'header--static': !app.fixedHeader }">
      <n-button v-if="isMobile" quaternary circle class="hbg" :aria-label="t('app.openMenu')" @click="mobileOpen = true">
        <Icon icon="ph:list" :width="20" />
      </n-button>
      <div class="apph">
        <AppHeader :show-collapse="showCollapse" :show-brand="headerShowBrand" :top-menu="headerTopMenu" />
      </div>
    </header>

    <!-- 内容 -->
    <main class="content">
      <div v-if="app.showTabs" class="tabs"><TabsBar /></div>
      <!-- component :key=路由 path 给稳定身份:out-in 过渡才能正确区分进/出场,否则显示上一页缓存(按 path 各自成键,不塌缩缓存)。 -->
      <div class="page">
        <router-view v-slot="{ Component }">
          <transition :name="transitionName" mode="out-in">
            <keep-alive :include="tabs.cachedNames" :exclude="tabs.excludeName">
              <component :is="Component" v-if="rvShow" :key="activeKey" />
            </keep-alive>
          </transition>
        </router-view>
      </div>
      <n-watermark
        v-if="app.watermark"
        :content="watermarkContent"
        cross
        fullscreen
        :font-size="14"
        :line-height="16"
        :width="192"
        :height="128"
        :rotate="-16"
        :font-color="watermarkColor"
      />
    </main>

    <!-- 移动端抽屉侧栏 -->
    <n-drawer v-model:show="mobileOpen" placement="left" :width="236">
      <n-drawer-content :native-scrollbar="false" body-content-style="padding:0">
        <SideNav :options="menuOptions" :value="activeKey" show-brand @select="onMobileSelect" />
      </n-drawer-content>
    </n-drawer>
  </div>
</template>

<style scoped>
.layout {
  display: grid;
  height: 100vh;
  overflow: hidden;
  background: var(--color-bg-body);
}
/* 竖向壳:侧栏满高在左,顶栏缩进到内容列上方 */
.mode-vertical,
.mode-vertical-mix,
.mode-vertical-hybrid-header-first {
  grid-template-columns: auto 1fr;
  grid-template-rows: var(--header-h) 1fr;
  grid-template-areas: 'aside header' 'aside content';
}
/* 顶栏壳:顶栏通栏,侧栏在其下方左侧 */
.mode-top-hybrid-sidebar-first,
.mode-top-hybrid-header-first {
  grid-template-columns: auto 1fr;
  grid-template-rows: var(--header-h) 1fr;
  grid-template-areas: 'header header' 'aside content';
}
.mode-horizontal,
.mode-mobile {
  grid-template-columns: 1fr;
  grid-template-rows: var(--header-h) 1fr;
  grid-template-areas: 'header' 'content';
}

.aside {
  grid-area: aside;
  height: 100%;
  overflow: hidden;
  border-right: 1px solid var(--color-border);
  background: var(--color-bg-container);
  transition: width 0.2s ease;
}
.aside--mix {
  display: flex;
}
.aside--mix .rail {
  width: var(--rail-w);
  flex-shrink: 0;
  border-right: 1px solid var(--color-border);
}
.aside--mix .l2col {
  flex: 1;
  min-width: 0;
}

.header {
  grid-area: header;
  display: flex;
  align-items: stretch;
  border-bottom: 1px solid var(--color-border);
  background: var(--color-header-bg);
  backdrop-filter: blur(12px);
  z-index: 10;
}
.header--static {
  backdrop-filter: none;
}
.hbg {
  align-self: center;
  margin-left: 8px;
}
.apph {
  flex: 1;
  min-width: 0;
  height: 100%;
}

.content {
  grid-area: content;
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.tabs {
  flex-shrink: 0;
}
.page {
  flex: 1;
  min-height: 0;
  overflow: auto;
  padding: var(--pad-page);
}
</style>
