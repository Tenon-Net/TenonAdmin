<script setup lang="ts">
// 侧栏薄壳:对 n-menu 的展示层封装,default.vue 复用于 4 处——完整树/二级子树/一级图标 rail/移动抽屉。
// collapsed(用户折叠开关) 与 rail(一级细栏,恒图标态、固定窄宽、无品牌字) 是两条独立的"收窄"来源,
// 二者任一为真都令 n-menu 进入 collapsed;rail 另用更窄的 collapsed-width。
import { NMenu, NScrollbar, type MenuOption } from 'naive-ui'
import TenonLogo from '@/components/TenonLogo.vue'

const props = defineProps<{
  options: MenuOption[]
  value?: string
  collapsed?: boolean
  rail?: boolean // 细图标栏(一级),强制图标态、无品牌文字
  showBrand?: boolean
}>()
const emit = defineEmits<{ select: [key: string] }>()
</script>

<template>
  <div class="sidenav" :class="{ rail: props.rail }">
    <div v-if="props.showBrand" class="brand">
      <TenonLogo :size="28" />
      <span v-show="!props.collapsed && !props.rail" class="brand-name">TenonAdmin</span>
    </div>
    <n-scrollbar>
      <n-menu
        :options="props.options"
        :value="props.value"
        :collapsed="props.collapsed || props.rail"
        :collapsed-width="props.rail ? 64 : 76"
        :indent="20"
        :root-indent="20"
        @update:value="(k: string) => emit('select', k)"
      />
    </n-scrollbar>
  </div>
</template>

<style scoped>
.sidenav {
  height: 100%;
  display: flex;
  flex-direction: column;
  background: var(--color-bg-container);
  overflow: hidden;
}
.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  height: var(--header-h);
  padding: 0 22px;
  color: var(--color-text-primary);
  font-weight: 600;
  overflow: hidden;
  white-space: nowrap;
  flex-shrink: 0;
}
.rail .brand {
  padding: 0;
  justify-content: center;
}
.brand-name {
  font-size: var(--font-size-md);
}
.sidenav :deep(.n-scrollbar) {
  flex: 1;
  min-height: 0;
}
</style>
