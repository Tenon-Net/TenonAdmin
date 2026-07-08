<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NDropdown, NPopover, NTooltip, useMessage, type DropdownOption } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { useAppStore } from '@/stores/app'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { ACCENTS } from '@/theme/accents'
import { authApi } from '@/api'
import { resetRouter } from '@/router'
import { translateError } from '@/utils/error'

const app = useAppStore()
const user = useUserStore()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const message = useMessage()
const { t } = useI18n()

const title = computed(() => {
  const m = route.meta.title as string | undefined
  if (!m) return ''
  return m.includes('.') ? t(m) : m
})

const moduleOptions = computed<DropdownOption[]>(() =>
  auth.modules.map((m) => ({ label: m.title, key: String(m.id) })),
)
async function onSwitchModule(key: string) {
  const { useModule } = await import('@/composables/useModule')
  await useModule().switchModule(Number(key))
}

const userOptions = computed<DropdownOption[]>(() => [
  { label: t('app.profile'), key: 'profile' },
  { label: t('app.password'), key: 'password' },
  { type: 'divider', key: 'd1' },
  { label: t('app.logout'), key: 'logout' },
])
async function onUser(key: string) {
  if (key === 'profile') router.push('/personal/profile')
  else if (key === 'password') router.push('/personal/password')
  else if (key === 'logout') await logout()
}
async function logout() {
  try {
    await authApi.logout()
  } catch (e) {
    message.error(translateError(e)) // 尽力而为,不阻断登出
  }
  resetRouter()
  auth.reset()
  user.clear()
  router.replace('/login')
}
</script>

<template>
  <div class="bar">
    <div class="left">
      <n-button quaternary circle @click="app.toggleCollapsed()">
        <Icon :icon="app.collapsed ? 'ph:list' : 'ph:sidebar-simple'" :width="20" />
      </n-button>
      <span class="title">{{ title }}</span>
    </div>

    <div class="right">
      <!-- 主色 -->
      <n-popover trigger="click" placement="bottom">
        <template #trigger>
          <n-button quaternary circle><Icon icon="ph:palette" :width="18" /></n-button>
        </template>
        <div class="accents">
          <button
            v-for="c in ACCENTS"
            :key="c"
            class="dot"
            :class="{ on: app.accent === c }"
            :style="{ background: c }"
            @click="app.setAccent(c)"
          />
        </div>
      </n-popover>

      <!-- 密度 -->
      <n-tooltip>
        <template #trigger>
          <n-button quaternary circle @click="app.setDensity(app.density === 'comfortable' ? 'compact' : 'comfortable')">
            <Icon :icon="app.density === 'compact' ? 'ph:rows' : 'ph:rows-plus-bottom'" :width="18" />
          </n-button>
        </template>
        {{ app.density === 'comfortable' ? t('app.comfortable') : t('app.compact') }}
      </n-tooltip>

      <!-- 主题 -->
      <n-tooltip>
        <template #trigger>
          <n-button quaternary circle @click="app.toggleDark()">
            <Icon :icon="app.dark ? 'ph:moon-stars' : 'ph:sun'" :width="18" />
          </n-button>
        </template>
        {{ app.dark ? t('app.dark') : t('app.light') }}
      </n-tooltip>

      <!-- 语言 -->
      <n-button quaternary size="small" @click="app.setLocale(app.locale === 'zh-CN' ? 'en-US' : 'zh-CN')">
        {{ app.locale === 'zh-CN' ? '中' : 'EN' }}
      </n-button>

      <!-- 切换应用 -->
      <n-dropdown v-if="auth.modules.length > 1" :options="moduleOptions" @select="onSwitchModule">
        <n-button quaternary circle>
          <n-tooltip>
            <template #trigger><Icon icon="ph:squares-four" :width="18" /></template>
            {{ t('app.switchModule') }}
          </n-tooltip>
        </n-button>
      </n-dropdown>

      <!-- 用户 -->
      <n-dropdown :options="userOptions" @select="onUser">
        <n-button quaternary>
          <Icon icon="ph:user-circle" :width="20" style="margin-right: 6px" />
          {{ user.userInfo?.name ?? user.userInfo?.account ?? '' }}
        </n-button>
      </n-dropdown>
    </div>
  </div>
</template>

<style scoped>
.bar {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 16px;
}
.left {
  display: flex;
  align-items: center;
  gap: 10px;
}
.title {
  font-size: var(--font-size-md);
  font-weight: 500;
  color: var(--color-text-primary);
}
.right {
  display: flex;
  align-items: center;
  gap: 6px;
}
.accents {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 8px;
  padding: 4px;
}
.dot {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  border: 2px solid transparent;
  cursor: pointer;
  outline: 1px solid var(--color-border);
}
.dot.on {
  border-color: var(--color-text-primary);
}
</style>
