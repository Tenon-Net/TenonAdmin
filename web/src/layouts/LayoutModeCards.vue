<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useAppStore, LAYOUT_MODES, type LayoutMode } from '@/stores/app'

// 布局模式彩色线框卡片(对齐 SoybeanAdmin theme-drawer/layout-mode.vue)。
// 三档主色 strong(500)/p300/p200 由实时 --color-primary 派生;形状与 soybean 逐模式一致。
const app = useAppStore()
const { t } = useI18n()

function label(m: LayoutMode) {
  const pascal = m.split('-').map((w) => w[0]!.toUpperCase() + w.slice(1)).join('')
  return t('settings.layout' + pascal)
}
</script>

<template>
  <div class="grid">
    <div v-for="m in LAYOUT_MODES" :key="m" class="cell" @click="app.setLayoutMode(m)">
      <button
        type="button"
        class="card"
        :class="{ on: app.layoutMode === m }"
        :aria-pressed="app.layoutMode === m"
      >
        <span class="inner" :class="m.includes('vertical') ? 'row' : 'col'">
          <!-- 左侧菜单:满高侧栏 + (头 / 内容) -->
          <template v-if="m === 'vertical'">
            <i class="sider strong" />
            <i class="vwrap"><i class="header p200" /><i class="main p200" /></i>
          </template>
          <!-- 左侧混合:一级 rail + 二级列 + (头 / 内容) -->
          <template v-else-if="m === 'vertical-mix'">
            <i class="rail strong" />
            <i class="subsider p300" />
            <i class="vwrap"><i class="header p200" /><i class="main p200" /></i>
          </template>
          <!-- 左侧混合-顶部优先:一级上顶,rail(二级) + 三级列 + (强调头 / 内容) -->
          <template v-else-if="m === 'vertical-hybrid-header-first'">
            <i class="rail strong" />
            <i class="subsider p300" />
            <i class="vwrap"><i class="header strong" /><i class="main p200" /></i>
          </template>
          <!-- 顶部菜单:通栏头 + 内容 -->
          <template v-else-if="m === 'horizontal'">
            <i class="header full strong" />
            <i class="hwrap"><i class="main p200" /></i>
          </template>
          <!-- 顶部混合-侧边优先:头(二级)+ (强调 rail(一级) / 内容) -->
          <template v-else-if="m === 'top-hybrid-sidebar-first'">
            <i class="header full p300" />
            <i class="hwrap"><i class="sider strong" /><i class="main p200" /></i>
          </template>
          <!-- 顶部混合-顶部优先:强调头(一级)+ (侧栏(二级) / 内容) -->
          <template v-else>
            <i class="header full strong" />
            <i class="hwrap"><i class="sider p300" /><i class="main p200" /></i>
          </template>
        </span>
      </button>
      <p class="label" :class="{ on: app.layoutMode === m }">{{ label(m) }}</p>
    </div>
  </div>
</template>

<style scoped>
.grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 12px;
  /* animated tab 的 .n-tabs-pane-wrapper 是 overflow:hidden,会裁掉边列卡片的 2px 选中环;内缩 3px 让环留在裁剪框内 */
  padding-inline: 3px;
}
.cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  cursor: pointer;
}

/* 卡片:soybean = shadow + ring-2(默认透明,hover/选中转主色) */
.card {
  width: 100%;
  height: 62px;
  padding: 6px;
  border: none;
  border-radius: 4px;
  background: var(--color-bg-container);
  box-shadow: 0 1px 3px rgba(20, 27, 45, 0.1), 0 0 0 2px transparent;
  cursor: pointer;
  transition: box-shadow 0.15s ease;
}
.cell:hover .card {
  box-shadow: 0 1px 3px rgba(20, 27, 45, 0.1), 0 0 0 2px var(--color-primary);
}
.card.on {
  box-shadow: 0 1px 3px rgba(20, 27, 45, 0.1), 0 0 0 2px var(--color-primary);
}

/* 线框:三档主色派生跟随实时 --color-primary */
.inner {
  --strong: var(--color-primary);
  --p300: color-mix(in srgb, var(--color-primary) 58%, var(--color-bg-container));
  --p200: color-mix(in srgb, var(--color-primary) 34%, var(--color-bg-container));
  display: flex;
  gap: 5px;
  width: 100%;
  height: 100%;
}
.inner.row {
  flex-direction: row;
}
.inner.col {
  flex-direction: column;
}
.inner i,
.inner .vwrap,
.inner .hwrap {
  border-radius: 3px;
}
.strong {
  background: var(--strong);
}
.p300 {
  background: var(--p300);
}
.p200 {
  background: var(--p200);
}
.sider {
  width: 18%;
  flex-shrink: 0;
}
.rail {
  width: 9%;
  flex-shrink: 0;
}
.subsider {
  width: 16%;
  flex-shrink: 0;
}
.header {
  flex-shrink: 0;
  height: 13px;
}
.header.full {
  width: 100%;
}
.main {
  flex: 1;
}
.vwrap {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 5px;
}
.vwrap .header {
  width: 100%;
}
.hwrap {
  flex: 1;
  display: flex;
  flex-direction: row;
  gap: 5px;
}

.label {
  margin: 0;
  text-align: center;
  font-size: var(--font-size-xs);
  line-height: var(--line-height-xs);
  color: var(--color-text-secondary);
}
.label.on {
  color: var(--color-primary);
  font-weight: var(--font-weight-medium);
}
</style>
