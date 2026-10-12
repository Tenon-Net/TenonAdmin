<script setup lang="ts">
import { computed, h, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NDropdown, NScrollbar, type DropdownOption } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { useTabsStore, type TabItem } from '@/stores/tabs'

const tabs = useTabsStore()
const route = useRoute()
const router = useRouter()
const { t, locale } = useI18n()

const activePath = computed(() => route.path)
const stripRef = ref<HTMLElement>()
const tabsbarRef = ref<HTMLElement>()
const indicatorReady = ref(false)
const indicatorStyle = ref({ width: '0px', transform: 'translateX(0px)' })
let resizeObserver: ResizeObserver | undefined
let scrollContainer: HTMLElement | undefined
let measureFrame = 0

function measureIndicator() {
  cancelAnimationFrame(measureFrame)
  measureFrame = requestAnimationFrame(() => {
    const strip = stripRef.value
    const chip = Array.from(strip?.querySelectorAll<HTMLElement>('.chip') ?? []).find(
      (element) => element.dataset.tabPath === activePath.value,
    )
    if (!strip || !chip) {
      indicatorReady.value = false
      return
    }
    indicatorStyle.value = {
      width: `${chip.offsetWidth}px`,
      transform: `translateX(${chip.offsetLeft}px)`,
    }
    indicatorReady.value = true
  })
}

async function syncIndicatorTargets() {
  await nextTick()
  resizeObserver?.disconnect()
  if (stripRef.value) {
    resizeObserver?.observe(stripRef.value)
    stripRef.value.querySelectorAll('.chip').forEach((chip) => resizeObserver?.observe(chip))
  }
  measureIndicator()
}

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
  { label: t('tabs.refresh'), key: 'refresh', icon: renderIcon('lucide:rotate-cw') },
  // 应用首页(affix)恒固定,不提供固定/取消项;其余标签可用户手动固定。
  {
    label: ctxTab.value?.pinned ? t('tabs.unpin') : t('tabs.pin'),
    key: 'pin', icon: renderIcon('lucide:pin'), disabled: ctxTab.value?.affix,
  },
  { label: t('tabs.close'), key: 'close', icon: renderIcon('lucide:x'), disabled: ctxTab.value?.affix || ctxTab.value?.pinned },
  { label: t('tabs.closeOthers'), key: 'others', icon: renderIcon('lucide:minimize-2') },
  { label: t('tabs.closeLeft'), key: 'left', icon: renderIcon('lucide:arrow-left-to-line') },
  { label: t('tabs.closeRight'), key: 'right', icon: renderIcon('lucide:arrow-right-to-line') },
  { label: t('tabs.closeAll'), key: 'all', icon: renderIcon('lucide:list-x') },
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
  else if (key === 'pin') tabs.togglePin(tab.path)
  else if (key === 'close') tabs.removeTab(tab.path)
  else if (key === 'others') tabs.closeOthers(tab.path)
  else if (key === 'left') tabs.closeLeft(tab.path)
  else if (key === 'right') tabs.closeRight(tab.path)
  else if (key === 'all') tabs.closeAll()
}

// 激活标签滚入视野
watch(activePath, () => {
  nextTick(() => {
    stripRef.value?.querySelector('.chip.active')?.scrollIntoView({ block: 'nearest', inline: 'nearest' })
    measureIndicator()
  })
})
watch([() => tabs.tabs.map((tab) => `${tab.path}:${tab.title}`).join('|'), locale], syncIndicatorTargets)

onMounted(() => {
  resizeObserver = new ResizeObserver(measureIndicator)
  scrollContainer = tabsbarRef.value?.querySelector<HTMLElement>('.n-scrollbar-container') ?? undefined
  scrollContainer?.addEventListener('scroll', measureIndicator, { passive: true })
  window.addEventListener('resize', measureIndicator, { passive: true })
  void syncIndicatorTargets()
})
onBeforeUnmount(() => {
  cancelAnimationFrame(measureFrame)
  resizeObserver?.disconnect()
  scrollContainer?.removeEventListener('scroll', measureIndicator)
  window.removeEventListener('resize', measureIndicator)
})
</script>

<template>
  <div ref="tabsbarRef" class="tabsbar">
    <n-scrollbar x-scrollable>
      <div ref="stripRef" class="strip">
        <span
          class="tab-active-indicator"
          :class="{ ready: indicatorReady }"
          :style="indicatorStyle"
          aria-hidden="true"
        />
        <!-- chip 与关闭 X 均为可点非按钮元素:补 role/tabindex/键盘激活,键盘用户也能切换/关闭标签(右键菜单是鼠标专属) -->
        <div
          v-for="item in tabs.tabs"
          :key="item.path"
          class="chip"
          :class="{ active: item.path === activePath }"
          :data-tab-path="item.path"
          role="button"
          tabindex="0"
          @click="onClick(item)"
          @keydown.enter="onClick(item)"
          @keydown.space.prevent="onClick(item)"
          @contextmenu="onContext($event, item)"
          @mousedown.middle.prevent
          @auxclick.middle.prevent="tabs.removeTab(item.path)"
        >
          <Icon v-if="item.icon" :icon="item.icon" :width="15" class="chip-icon" />
          <span class="chip-label">{{ tabTitle(item) }}</span>
          <!-- 固定标签(用户 pin)显示图钉、不显示关闭 X;应用首页 affix 两者都不显示 -->
          <Icon v-if="item.pinned" icon="lucide:pin" :width="12" class="chip-pin" />
          <Icon
            v-else-if="!item.affix"
            icon="lucide:x"
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
  position: relative;
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 5px 0;
}
.tab-active-indicator {
  position: absolute;
  top: 5px;
  left: 0;
  height: 30px;
  border: 1px solid var(--color-primary);
  border-radius: var(--radius-md);
  background: var(--color-primary-light);
  opacity: 0;
  pointer-events: none;
  transition:
    transform var(--motion-duration-base) var(--motion-ease-standard),
    width var(--motion-duration-base) var(--motion-ease-standard),
    opacity var(--motion-duration-feedback) var(--motion-ease-standard);
}
.tab-active-indicator.ready {
  opacity: 1;
}
.chip {
  position: relative;
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
  transition: color var(--transition-fast), background var(--transition-fast), border-color var(--transition-fast), transform var(--transition-fast);
}
.chip:hover {
  color: var(--color-text-primary);
}
.chip.active {
  background: transparent;
  border-color: transparent;
  color: var(--color-primary);
}
.chip:active {
  transform: scale(var(--motion-press-scale));
}
.chip:focus-visible,
.chip-close:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 1px;
}
.chip-pin {
  opacity: 0.7;
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
