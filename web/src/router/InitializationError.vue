<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton, NCard } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const auth = useAuthStore()
const { t } = useI18n()
const retrying = shallowRef(false)
const errorText = computed(() => auth.initializationError)

async function retry() {
  if (retrying.value) return
  retrying.value = true
  const target = auth.initializationRedirect || '/'
  auth.initializationError = ''
  try {
    await router.replace(target)
  } finally {
    retrying.value = false
  }
}
</script>

<template>
  <main class="initialization-error">
    <n-card class="initialization-error__card" :title="t('error._fallback')">
      <n-alert type="error" :show-icon="true">{{ errorText }}</n-alert>
      <n-button type="primary" :loading="retrying" @click="retry">{{ t('common.retry') }}</n-button>
    </n-card>
  </main>
</template>

<style scoped>
.initialization-error {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: var(--space-24);
  background: var(--color-bg-body);
}

.initialization-error__card {
  width: min(480px, 100%);
}

.initialization-error__card :deep(.n-card__content) {
  display: grid;
  gap: var(--space-16);
}
</style>
