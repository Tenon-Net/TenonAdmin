<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'

const route = useRoute()
const { t } = useI18n()
const component = computed(() => route.meta.missingComponent as string | undefined)
const message = computed(() => t('missingRoute.message', {
  title: String(route.meta.title ?? ''),
  component: component.value ?? '',
}))
</script>

<template>
  <div class="missing-route">
    <h1>{{ t('missingRoute.title') }}</h1>
    <p>{{ message }}</p>
    <code v-if="component">{{ component }}</code>
  </div>
</template>

<style scoped>
.missing-route {
  min-height: 60vh;
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  color: var(--color-text-secondary);
  text-align: center;
}

h1 {
  margin: 0;
  color: var(--color-text-primary);
  font-size: var(--font-size-xl);
}

p {
  margin: 0;
}

code {
  padding: 6px 8px;
  background: var(--color-bg-container);
  border: 1px solid var(--color-border);
  border-radius: 4px;
  color: var(--color-danger);
}
</style>
