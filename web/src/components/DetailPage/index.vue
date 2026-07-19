<script setup lang="ts">
// 详情页外壳:返回按钮 + 标题 + actions 插槽 + body 插槽。
// 补偿非菜单详情路由的空面包屑(菜单树里没这条路由 → 头部面包屑为空,故详情页自带标题/返回)。
// @back 交父级决定语义:路由态 = 关标签回列表(见 views/system/user/detail.vue);就地态 = 清列表页的详情状态。
import { NButton, NSpin } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'

withDefaults(defineProps<{ title?: string; loading?: boolean; showBack?: boolean }>(), { showBack: true })
defineEmits<{ back: [] }>()
const { t } = useI18n()
</script>

<template>
  <div class="detail-page">
    <div class="detail-header">
      <n-button v-if="showBack" quaternary size="small" :aria-label="t('common.back')" @click="$emit('back')">
        <template #icon><AppIcon icon="ph:arrow-left" :size="18" /></template>
      </n-button>
      <h2 class="detail-title">{{ title }}</h2>
      <div class="detail-actions"><slot name="actions" /></div>
    </div>
    <n-spin :show="loading">
      <div class="detail-body"><slot /></div>
    </n-spin>
  </div>
</template>

<style scoped>
.detail-page {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card, 16px);
}
.detail-header {
  display: flex;
  align-items: center;
  gap: 8px;
}
.detail-title {
  margin: 0;
  font-size: var(--font-size-lg, 18px);
  font-weight: 600;
  color: var(--color-text-primary);
}
.detail-actions {
  margin-left: auto;
}
</style>
