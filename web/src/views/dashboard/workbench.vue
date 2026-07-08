<script setup lang="ts">
import { computed } from 'vue'
import { NGrid, NGi, NCard } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { useUserStore } from '@/stores/user'
import { useAppStore } from '@/stores/app'
import { btnGrad } from '@/theme/mix'

const { t } = useI18n()
const user = useUserStore()
const app = useAppStore()

const avatarStyle = computed(() => ({ background: btnGrad(app.accent) }))

// 骨架期用占位统计;真实数值等后端接口/后续刀接入(纯 token,不加图表库)。
const stats = computed(() => [
  { key: 'roles', icon: 'ph:shield-duotone', value: 24, color: 'var(--color-primary)' },
  { key: 'users', icon: 'ph:users-duotone', value: 186, color: 'var(--color-success)' },
  { key: 'perms', icon: 'ph:key-duotone', value: 312, color: 'var(--color-warning)' },
  { key: 'pending', icon: 'ph:hourglass-duotone', value: 5, color: 'var(--color-danger)' },
])
</script>

<template>
  <div class="page">
    <!-- 欢迎横幅(英雄区:btnGrad 头像)-->
    <div class="banner">
      <div class="avatar" :style="avatarStyle">
        <Icon icon="ph:user" :width="26" color="#fff" />
      </div>
      <div>
        <div class="hi">{{ t('workbench.welcome', { name: user.userInfo?.name ?? '' }) }}</div>
        <div class="tip">TenonAdmin · 企业级权限管理后台</div>
      </div>
    </div>

    <n-grid :cols="'1 s:2 l:4'" responsive="screen" :x-gap="16" :y-gap="16">
      <n-gi v-for="s in stats" :key="s.key">
        <n-card :bordered="true">
          <div class="stat">
            <div class="stat-ico" :style="{ color: s.color }"><Icon :icon="s.icon" :width="30" /></div>
            <div>
              <div class="stat-val tabular">{{ s.value }}</div>
              <div class="stat-label">{{ t(`workbench.${s.key}`) }}</div>
            </div>
          </div>
        </n-card>
      </n-gi>
    </n-grid>
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card);
}
.banner {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 24px;
  border-radius: var(--radius-lg);
  background: var(--color-primary-light);
}
.avatar {
  width: 52px;
  height: 52px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
}
.hi {
  font-size: var(--font-size-lg);
  font-weight: 600;
  color: var(--color-text-primary);
}
.tip {
  color: var(--color-text-secondary);
  margin-top: 4px;
}
.stat {
  display: flex;
  align-items: center;
  gap: 16px;
}
.stat-val {
  font-size: 28px;
  font-weight: 700;
  color: var(--color-text-primary);
  line-height: 1.1;
}
.stat-label {
  color: var(--color-text-secondary);
  margin-top: 2px;
}
</style>
