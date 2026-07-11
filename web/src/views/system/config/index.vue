<script setup lang="ts">
// 分类配置中心:按分类 Tab 组织。系统基础=结构化表单(字段级 UI);安全策略=锁定/密码/会话结构化表单
// (后端 ISecurityPolicyProvider 强制执行,改值即时生效);其他配置=任意 key 扁平 CRUD 兜底。
import { NTabs, NTabPane, NCard } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import SysBaseConfig from './components/SysBaseConfig.vue'
import SecurityConfig from './components/SecurityConfig.vue'
import UploadConfig from './components/UploadConfig.vue'
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
    <n-tab-pane name="security" :tab="t('config.tab.security')">
      <n-card :bordered="false" :title="t('config.tab.security')">
        <security-config />
      </n-card>
    </n-tab-pane>
    <n-tab-pane name="upload" :tab="t('config.tab.upload')">
      <n-card :bordered="false" :title="t('config.tab.upload')">
        <upload-config />
      </n-card>
    </n-tab-pane>
    <n-tab-pane name="other" :tab="t('config.tab.other')">
      <other-config />
    </n-tab-pane>
  </n-tabs>
</template>
