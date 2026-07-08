<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NEmpty, NSpin, useMessage } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useModule } from '@/composables/useModule'
import { translateError } from '@/utils/error'
import TenonLogo from '@/components/TenonLogo.vue'

const router = useRouter()
const message = useMessage()
const { t } = useI18n()
const auth = useAuthStore()
const busy = ref(false)

async function pick(id: number, defaultRoute?: string) {
  busy.value = true
  try {
    await useModule().enter(id)
    router.replace(defaultRoute || '/workbench')
  } catch (e) {
    message.error(translateError(e))
  } finally {
    busy.value = false
  }
}

async function setDefault(id: number) {
  try {
    await useModule().setDefault(id)
    message.success(t('common.success'))
  } catch (e) {
    message.error(translateError(e))
  }
}
</script>

<template>
  <div class="wrap">
    <div class="head">
      <TenonLogo :size="34" />
      <h1>{{ t('module.choose') }}</h1>
    </div>

    <n-spin :show="busy">
      <n-empty v-if="!auth.modules.length" :description="t('module.empty')" style="padding: 60px 0" />
      <div v-else class="grid">
        <div v-for="m in auth.modules" :key="m.id" class="card" @click="pick(m.id, m.defaultRoute)">
          <div class="ico"><Icon :icon="m.icon || 'ph:app-window-duotone'" :width="28" /></div>
          <div class="body">
            <div class="name">{{ m.title }}</div>
            <div class="code">{{ m.code }}</div>
          </div>
          <n-button text size="tiny" class="def" @click.stop="setDefault(m.id)">{{ t('module.setDefault') }}</n-button>
        </div>
      </div>
    </n-spin>
  </div>
</template>

<style scoped>
.wrap {
  min-height: 100vh;
  background: var(--color-bg-body);
  padding: 80px 24px;
  display: flex;
  flex-direction: column;
  align-items: center;
}
.head {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 32px;
}
.head h1 {
  font-size: var(--font-size-xl);
  font-weight: 600;
  color: var(--color-text-primary);
}
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: var(--gap-card);
  width: 100%;
  max-width: 900px;
}
.card {
  position: relative;
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 20px;
  background: var(--color-bg-container);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-1);
  cursor: pointer;
  transition: transform var(--transition-fast), border-color var(--transition-fast);
}
.card:hover {
  transform: translateY(-3px);
  border-color: var(--color-primary);
  box-shadow: var(--shadow-2);
}
.ico {
  color: var(--color-primary);
}
.name {
  font-size: var(--font-size-md);
  font-weight: 600;
  color: var(--color-text-primary);
}
.code {
  font-size: var(--font-size-sm);
  color: var(--color-text-tertiary);
  font-family: var(--font-family-mono);
}
.def {
  position: absolute;
  top: 10px;
  right: 12px;
}
</style>
