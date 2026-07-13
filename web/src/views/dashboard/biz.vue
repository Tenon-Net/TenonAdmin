<script setup lang="ts">
// 业务系统的工作台。每个应用一个首页组件——这里和 dashboard/workbench.vue 是两个独立页面,
// 由各自应用的「工作台」菜单(component 字段)指向,切应用即换首页。
// ponytail: 占位内容,接业务统计接口后替换。
import { NCard, NGrid, NGi, NStatistic } from 'naive-ui'
import { useUserStore } from '@/stores/user'

const user = useUserStore()
</script>

<template>
  <div class="biz-wb">
    <n-card>
      <h2 class="hi">业务系统 · 工作台</h2>
      <p class="sub">{{ user.userInfo?.name ?? user.userInfo?.account }},欢迎回来</p>
    </n-card>

    <n-grid :cols="4" :x-gap="16" responsive="screen" item-responsive class="cards">
      <n-gi span="4 s:2 m:1"><n-card><n-statistic label="待办工单" :value="8" /></n-card></n-gi>
      <n-gi span="4 s:2 m:1"><n-card><n-statistic label="进行中" :value="3" /></n-card></n-gi>
      <n-gi span="4 s:2 m:1"><n-card><n-statistic label="本月完成" :value="42" /></n-card></n-gi>
      <n-gi span="4 s:2 m:1"><n-card><n-statistic label="异常" :value="1" /></n-card></n-gi>
    </n-grid>
  </div>
</template>

<style scoped>
.biz-wb {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card);
}
.hi {
  font-size: var(--font-size-xl);
  font-weight: 600;
  color: var(--color-text-primary);
}
.sub {
  margin-top: 4px;
  color: var(--color-text-secondary);
}
</style>
