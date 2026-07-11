<script setup lang="ts">
// 分类配置中心:按分类 Tab 组织。系统基础=结构化表单(字段级 UI);其他配置=任意 key 扁平 CRUD 兜底。
// 安全策略(登录锁定/密码策略/会话超时)延后单独一期——届时在此加一个 tab-pane + 一个结构化组件即可。
import { NTabs, NTabPane, NCard } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import SysBaseConfig from './components/SysBaseConfig.vue'
import OtherConfig from './components/OtherConfig.vue'

const { t } = useI18n()
</script>

<template>
  <n-tabs type="line" animated>
    <n-tab-pane name="base" :tab="t('config.tab.base')">
      <n-card :bordered="false" :title="t('config.tab.base')">
        <sys-base-config />
      </n-card>
    </n-tab-pane>
    <n-tab-pane name="other" :tab="t('config.tab.other')">
      <other-config />
    </n-tab-pane>
  </n-tabs>
</template>
