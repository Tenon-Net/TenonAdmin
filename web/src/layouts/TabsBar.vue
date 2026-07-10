<script setup lang="ts">
import { computed, h, nextTick, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NDropdown, NScrollbar, type DropdownOption } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { useTabsStore, type TabItem } from '@/stores/tabs'

const tabs = useTabsStore()
const route = useRoute()
const router = useRouter()
const { t } = useI18n()

const activePath = computed(() => route.path)
const stripRef = ref<HTMLElement>()

function tabTitle(item: TabItem) {
  return item.title.includes('.') ? t(item.title) : item.title
}
function onClick(item: TabItem) {
  if (item.path !== route.path) router.push(item.fullPath)
}

function renderIcon(name: string) {
  return () => h(Icon, { icon: name, width: 16 })
}

const ctxShow = ref(false)
const ctxX = ref(0)
const ctxY = ref(0)
const ctxTab = ref<TabItem>()
const ctxOptions = computed<DropdownOption[]>(() => [
  { label: t('tabs.refresh'), key: 'refresh', icon: renderIcon('ph:arrow-clockwise') },
  { label: t('tabs.close'), key: 'close', icon: renderIcon('ph:x'), disabled: ctxTab.value?.affix },
  { label: t('tabs.closeOthers'), key: 'others', icon: renderIcon('ph:arrows-in-line-horizontal') },
  { label: t('tabs.closeLeft'), key: 'left', icon: renderIcon('ph:arrow-line-left') },
  { label: t('tabs.closeRight'), key: 'right', icon: renderIcon('ph:arrow-line-right') },
  { label: t('tabs.closeAll'), key: 'all', icon: renderIcon('ph:list-dashes') },
])

function onContext(e: MouseEvent, item: TabItem) {
  e.preventDefault()
  ctxTab.value = item
  // 先关再于 nextTick 重开:菜单已展开时右键切到别的标签,只改坐标 NDropdown 不会重定位。
  ctxShow.value = false
  nextTick(() => {
    ctxX.value = e.clientX
    ctxY.value = e.clientY
    ctxShow.value = true
  })
}
function onCtxSelect(key: string) {
  ctxShow.value = false
  const tab = ctxTab.value
  if (!tab) return
  if (key === 'refresh') tabs.refreshTab(tab.name)
  else if (key === 'close') tabs.removeTab(tab.path)
  else if (key === 'others') tabs.closeOthers(tab.path)
  else if (key === 'left') tabs.closeLeft(tab.path)
  else if (key === 'right') tabs.closeRight(tab.path)
  else if (key === 'all') tabs.closeAll()
}

// 激活标签滚入视野
watch(activePath, () => {
  nextTick(() => stripRef.value?.querySelector('.chip.active')?.scrollIntoView({ block: 'nearest', inline: 'nearest' }))
})
</script>

<template>
  <div class="tabsbar">
    <n-scrollbar x-scrollable>
      <div ref="stripRef" class="strip">
        <!-- chip 与关闭 X 均为可点非按钮元素:补 role/tabindex/键盘激活,键盘用户也能切换/关闭标签(右键菜单是鼠标专属) -->
        <div
          v-for="item in tabs.tabs"
          :key="item.path"
          class="chip"
          :class="{ active: item.path === activePath }"
          role="button"
          tabindex="0"
          @click="onClick(item)"
          @keydown.enter="onClick(item)"
          @keydown.space.prevent="onClick(item)"
          @contextmenu="onContext($event, item)"
        >
          <Icon v-if="item.icon" :icon="item.icon" :width="15" class="chip-icon" />
          <span class="chip-label">{{ tabTitle(item) }}</span>
          <Icon
            v-if="!item.affix"
            icon="ph:x"
            :width="13"
            class="chip-close"
            role="button"
            tabindex="0"
            :aria-label="t('tabs.close')"
            @click.stop="tabs.removeTab(item.path)"
            @keydown.enter.stop="tabs.removeTab(item.path)"
            @keydown.space.stop.prevent="tabs.removeTab(item.path)"
          />
        </div>
      </div>
    </n-scrollbar>
    <n-dropdown
      trigger="manual"
      placement="bottom-start"
      :show="ctxShow"
      :x="ctxX"
      :y="ctxY"
      :options="ctxOptions"
      @select="onCtxSelect"
      @clickoutside="ctxShow = false"
    />
  </div>
</template>

<style scoped>
.tabsbar {
  height: 42px;
  display: flex;
  align-items: center;
  padding: 0 8px;
  background: var(--color-bg-container);
  border-bottom: 1px solid var(--color-border);
}
.strip {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 5px 0;
}
.chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 30px;
  padding: 0 10px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-fill);
  color: var(--color-text-secondary);
  font-size: var(--font-size-sm);
  white-space: nowrap;
  cursor: pointer;
  user-select: none;
  transition:
    color 0.15s,
    background 0.15s,
    border-color 0.15s;
}
.chip:hover {
  color: var(--color-text-primary);
}
.chip.active {
  background: var(--color-primary-light);
  border-color: var(--color-primary);
  color: var(--color-primary);
}
.chip:focus-visible,
.chip-close:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 1px;
}
.chip-close {
  border-radius: 50%;
  opacity: 0.6;
}
.chip-close:hover {
  opacity: 1;
  background: var(--color-fill);
}
</style>
