<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { NGrid, NGi, NCard, NSpin } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { useUserStore } from '@/stores/user'
import { useAppStore } from '@/stores/app'
import { btnGrad } from '@/theme/mix'
import { dashboardApi } from '@/api'
import type { DashboardSummary } from '@/types/api'
import LineChart from '@/components/Chart/LineChart.vue'
import PieChart from '@/components/Chart/PieChart.vue'

const { t } = useI18n()
const user = useUserStore()
const app = useAppStore()

const avatarStyle = computed(() => ({ background: btnGrad(app.accent) }))

const summary = ref<DashboardSummary | null>(null)
const loading = ref(true)

onMounted(async () => {
  try {
    summary.value = await dashboardApi.summary()
  } catch {
    // 首页不该因为一张统计图挂了就糊用户一脸红:统计取不到就显示 —,页面其余部分照常可用。
  } finally {
    loading.value = false
  }
})

/** 计数未到位时显示 —,而不是先闪个 0 再跳到真值。 */
function num(v: number | undefined) {
  return v === undefined ? '—' : v
}

const stats = computed(() => [
  { key: 'roles', icon: 'ph:shield-duotone', value: num(summary.value?.roles), color: 'var(--color-primary)' },
  { key: 'users', icon: 'ph:users-duotone', value: num(summary.value?.users), color: 'var(--color-success)' },
  { key: 'perms', icon: 'ph:key-duotone', value: num(summary.value?.perms), color: 'var(--color-warning)' },
  { key: 'online', icon: 'ph:broadcast-duotone', value: num(summary.value?.onlineSessions), color: 'var(--color-danger)' },
])

const trendDays = computed(() => summary.value?.trendDays ?? [])
const trendSeries = computed(() => [
  { name: t('workbench.trendLogins'), data: summary.value?.trendLogins ?? [] },
  { name: t('workbench.trendActive'), data: summary.value?.trendActiveUsers ?? [] },
])
// 资源分布就是前三个计数,不必让后端再返一份
const distribution = computed(() => [
  { name: t('workbench.roles'), value: summary.value?.roles ?? 0 },
  { name: t('workbench.users'), value: summary.value?.users ?? 0 },
  { name: t('workbench.perms'), value: summary.value?.perms ?? 0 },
])
</script>

<template>
  <div class="view">
    <!-- 欢迎横幅(英雄区:btnGrad 头像)-->
    <div class="banner">
      <div class="avatar" :style="avatarStyle">
        <Icon icon="ph:user" :width="26" color="#fff" />
      </div>
      <div>
        <div class="hi">{{ t('workbench.welcome', { name: user.userInfo?.name ?? '' }) }}</div>
        <div class="tip">{{ t('workbench.subtitle') }}</div>
      </div>
    </div>

    <n-grid :cols="'1 s:2 l:4'" responsive="screen" :x-gap="16" :y-gap="16">
      <n-gi v-for="s in stats" :key="s.key">
        <n-card :bordered="true">
          <div class="stat">
            <div class="stat-ico" :style="{ color: s.color }"><Icon :icon="s.icon" :width="28" /></div>
            <div>
              <div class="stat-val tabular">{{ s.value }}</div>
              <div class="stat-label">{{ t(`workbench.${s.key}`) }}</div>
            </div>
          </div>
        </n-card>
      </n-gi>
    </n-grid>

    <n-grid :cols="'1 l:2'" responsive="screen" :x-gap="16" :y-gap="16">
      <n-gi>
        <n-card :title="t('workbench.trendTitle')" :bordered="true">
          <n-spin :show="loading">
            <LineChart :categories="trendDays" :series="trendSeries" />
          </n-spin>
        </n-card>
      </n-gi>
      <n-gi>
        <n-card :title="t('workbench.distributionTitle')" :bordered="true">
          <n-spin :show="loading">
            <PieChart :data="distribution" />
          </n-spin>
        </n-card>
      </n-gi>
    </n-grid>
  </div>
</template>

<style scoped>
.view {
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
/* 图标底盘:取当前语义色 12% 作浅底圆角,呼应 banner 头像,避免图标"裸浮" */
.stat-ico {
  flex-shrink: 0;
  width: 48px;
  height: 48px;
  border-radius: var(--radius-md);
  display: flex;
  align-items: center;
  justify-content: center;
  background: color-mix(in srgb, currentColor 12%, transparent);
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
