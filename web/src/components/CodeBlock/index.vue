<script setup lang="ts">
// 只读代码/JSON 展示:NCode + highlight.js/lib/core 按需注册(现仅 json;未注册语言 NCode 自动降级纯文本渲染)。
// 配色走 Naive 主题的 code 变量、不引 hljs css;hljs 是 naive-ui 的直接依赖,显式声明零新下载(COMPONENTS.md 备案)。
// 后续配置页要 yaml/sql 再来这里注册,别提前加。
import { NButton, NCode } from 'naive-ui'
import { useClipboard } from '@vueuse/core'
import { useI18n } from 'vue-i18n'
import hljs from 'highlight.js/lib/core'
import json from 'highlight.js/lib/languages/json'
import AppIcon from '@/components/AppIcon.vue'

hljs.registerLanguage('json', json)

withDefaults(
  defineProps<{
    /** 已格式化的文本(格式化由调用方负责,如 JSON.stringify(v, null, 2)) */
    code: string
    /** hljs 语言名;未注册的语言降级纯文本 */
    language?: string
    /** 长行自动换行(抽屉等窄容器必开,否则撑宽) */
    wordWrap?: boolean
    /** 右上角悬浮复制按钮 */
    copyable?: boolean
  }>(),
  { language: 'json', wordWrap: true, copyable: true },
)

const { t } = useI18n()
const { copy, copied } = useClipboard()
</script>

<template>
  <div class="code-block">
    <n-code :hljs="hljs" :code="code" :language="language" :word-wrap="wordWrap" />
    <n-button
      v-if="copyable"
      class="copy-btn"
      size="tiny"
      quaternary
      :title="copied ? t('common.copied') : t('common.copy')"
      @click="copy(code)"
    >
      <template #icon>
        <AppIcon :icon="copied ? 'ph:check' : 'ph:copy'" :size="14" />
      </template>
    </n-button>
  </div>
</template>

<style scoped>
.code-block {
  position: relative;
  font-size: 12px;
  line-height: 1.5;
}
.copy-btn {
  position: absolute;
  top: 0;
  right: 0;
}
</style>
