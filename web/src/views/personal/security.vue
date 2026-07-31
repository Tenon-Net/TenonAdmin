<script setup lang="ts">
// 个人安全:TOTP 自助绑定/恢复入口(ADR 0006)。不进业务菜单,顶栏用户下拉进入。
import { useRouter } from 'vue-router'
import { NCard, NButton, NAlert, NSpace } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useUserStore } from '@/stores/user'

const { t } = useI18n()
const router = useRouter()
const user = useUserStore()

function goBind() {
  const account = user.userInfo?.account
  router.push({ path: '/mfa/bind', query: account ? { account } : {} })
}
</script>

<template>
  <n-card :bordered="true" :title="t('personalSecurity.title')" style="max-width: 560px">
    <n-alert type="info" :bordered="false" style="margin-bottom: 16px">
      {{ t('personalSecurity.hint') }}
    </n-alert>
    <p class="sec-desc">{{ t('personalSecurity.bindDesc') }}</p>
    <n-space>
      <n-button type="primary" @click="goBind">{{ t('personalSecurity.setupAuthenticator') }}</n-button>
      <n-button quaternary @click="goBind">{{ t('personalSecurity.useRecovery') }}</n-button>
    </n-space>
    <p class="sec-note">{{ t('personalSecurity.note') }}</p>
  </n-card>
</template>

<style scoped>
.sec-desc { margin: 0 0 16px; color: var(--color-text-2, #666); line-height: 1.5; }
.sec-note { margin: 20px 0 0; font-size: 12px; color: var(--color-text-3, #999); line-height: 1.5; }
</style>
