<script setup lang="ts">
// 设置项行:左标签 + 右控件槽。槽内是 Naive 自绘控件(n-switch/n-select),无法用原生 <label for> 绑定,
// 故给整行 role="group" + aria-label,让读屏聚焦控件时能读到这项设置名(可见 span 仍留给视觉)。
defineProps<{ label: string }>()
</script>

<template>
  <div class="row" role="group" :aria-label="label">
    <span class="lbl">{{ label }}</span>
    <div class="ctl"><slot /></div>
  </div>
</template>

<style scoped>
.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  min-height: 40px;
}
.lbl {
  color: var(--color-text-secondary);
  font-size: var(--font-size-sm);
  white-space: nowrap;
}
.ctl {
  display: flex;
  align-items: center;
  min-width: 0;
}
</style>
