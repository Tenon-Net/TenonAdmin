<script setup lang="ts">
defineProps<{ label: string }>()
</script>

<template>
  <div class="row">
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
