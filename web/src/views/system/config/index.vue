<script setup lang="ts">
// 分类配置中心:按分类 Tab 组织。系统基础=结构化表单(字段级 UI);安全策略=锁定/密码/会话结构化表单
// (后端 ISecurityPolicyProvider 强制执行,改值即时生效);其他配置=任意 key 扁平 CRUD 兜底。
import { NTabs, NTabPane, NCard } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import SysBaseConfig from './components/SysBaseConfig.vue'
import SecurityConfig from './components/SecurityConfig.vue'
import ExternalAuthConfig from './components/ExternalAuthConfig.vue'
import UploadConfig from './components/UploadConfig.vue'
import JobConfig from './components/JobConfig.vue'
import OtherConfig from './components/OtherConfig.vue'

const { t } = useI18n()
</script>

<template>
  <!-- display-directive="show:lazy":每个面板首次访问才挂载(保住懒加载),之后常驻、切走只是 v-show 隐藏。
       naive 的默认值是 'if' —— 面板一离场就销毁,而四个子面板的未保存改动全在各自的组件本地 state 里,
       于是"改了值 → 切个 Tab 看一眼 → 切回来"就把改动无声吃掉了(回来还会重新拉一遍后端旧值,让人以为没改过)。
       注意 display-directive 是 n-tab-pane 的 prop,不是 n-tabs 的,四个都得写。 -->
  <n-tabs type="line" animated>
    <n-tab-pane name="base" :tab="t('config.tab.base')" display-directive="show:lazy">
      <n-card :bordered="false" :title="t('config.tab.base')">
        <sys-base-config />
      </n-card>
    </n-tab-pane>
    <n-tab-pane name="security" :tab="t('config.tab.security')" display-directive="show:lazy">
      <n-card :bordered="false" :title="t('config.tab.security')">
        <security-config />
      </n-card>
    </n-tab-pane>
    <n-tab-pane name="externalAuth" :tab="t('config.tab.externalAuth')" display-directive="show:lazy">
      <n-card :bordered="false" :title="t('config.tab.externalAuth')">
        <external-auth-config />
      </n-card>
    </n-tab-pane>
    <n-tab-pane name="upload" :tab="t('config.tab.upload')" display-directive="show:lazy">
      <n-card :bordered="false" :title="t('config.tab.upload')">
        <upload-config />
      </n-card>
    </n-tab-pane>
    <n-tab-pane name="job" :tab="t('config.tab.job')" display-directive="show:lazy">
      <n-card :bordered="false" :title="t('config.tab.job')">
        <job-config />
      </n-card>
    </n-tab-pane>
    <n-tab-pane name="other" :tab="t('config.tab.other')" display-directive="show:lazy">
      <other-config />
    </n-tab-pane>
  </n-tabs>
</template>
