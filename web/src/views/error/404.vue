<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useAppStore } from '@/stores/app'
import { isKnownPagePath } from '@/router/knownPage'

const route = useRoute()
const router = useRouter()
const app = useAppStore()
const { t } = useI18n()
const denied = computed(() => isKnownPagePath(route.path))
</script>

<template>
  <div class="nf">
    <div class="code" :style="{ color: app.accent }">{{ denied ? '403' : '404' }}</div>
    <div class="msg">{{ denied ? t('forbidden.desc') : t('notFound.desc') }}</div>
    <n-button type="primary" @click="router.replace('/')">{{ t('notFound.back') }}</n-button>
  </div>
</template>

<style scoped>
.nf {
  /* 撑满 layout 的 .page 容器,而不是整个视口——404 现在住在外壳里 */
  min-height: 60vh;
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 16px;
  background: var(--color-bg-body);
}
.code {
  font-size: 96px;
  font-weight: 800;
  line-height: 1;
}
.msg {
  color: var(--color-text-secondary);
  font-size: var(--font-size-md);
}
</style>
