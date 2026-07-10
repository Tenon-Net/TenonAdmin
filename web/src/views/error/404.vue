<script setup lang="ts">
import { useRouter } from 'vue-router'
import { NButton } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useAppStore } from '@/stores/app'

const router = useRouter()
const app = useAppStore()
const { t } = useI18n()
</script>

<template>
  <div class="nf">
    <div class="code" :style="{ color: app.accent }">404</div>
    <div class="msg">{{ t('notFound.desc') }}</div>
    <n-button type="primary" @click="router.replace('/')">{{ t('notFound.back') }}</n-button>
  </div>
</template>

<style scoped>
.nf {
  height: 100vh;
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
