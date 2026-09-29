<script setup lang="ts">
// 一次性凭据展示:完整凭据只在发放/轮换的这一次响应里出现。它只活在本组件的局部 ref 里,
// 关闭即清空;不写 Pinia / localStorage / sessionStorage,不打任何日志。
// 既没复制、也没点「我已妥善保存」就要关(Esc / 关闭钮 / 取消)或离开本页时先二次确认,免得凭据就此丢失;
// 离开本页在导航之前就问:拒绝则留在本页,确认则丢弃凭据再走,弹窗不会残留到下一页。
import { ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { NAlert, NButton, NInput, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import FormContainer from '@/components/FormContainer/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import type { OpenAppCredentialIssued } from '@/types/integration'

const { t } = useI18n()
const message = useMessage()
const { ask } = useConfirm()

const show = ref(false)
const apiKey = ref('')
let settled = false
let asking = false

function open(issued: OpenAppCredentialIssued) {
  apiKey.value = issued.apiKey ?? ''
  settled = false
  show.value = true
}
defineExpose({ open })

const acknowledge = () => {
  settled = true
}

/** 未复制、未确认保存时问一句是否丢弃;已经在问就不重复弹(按未同意处理)。 */
async function confirmDiscard() {
  if (settled) return true
  if (asking) return false
  asking = true
  try {
    return await ask({ content: t('integration.credential.secretCloseConfirm') })
  } finally {
    asking = false
  }
}

/** 容器的关闭请求(Esc / 关闭钮 / 取消)都经这里:show 受控,不改就不会关。 */
async function onShowChange(next: boolean) {
  if (!next && !(await confirmDiscard())) return
  show.value = next
}

onBeforeRouteLeave(async () => {
  if (!show.value) return true
  if (!(await confirmDiscard())) return false
  settled = true
  show.value = false
  return true
})

// 关闭即丢弃原文,组件内不留痕
watch(show, (visible) => {
  if (!visible) apiKey.value = ''
})

async function copy() {
  try {
    await navigator.clipboard.writeText(apiKey.value)
    settled = true
    message.success(t('integration.credential.copied'))
  } catch {
    message.error(t('integration.credential.copyFailed'))
  }
}
</script>

<template>
  <FormContainer
    :show="show"
    variant="modal"
    :title="t('integration.credential.secretTitle')"
    :width="600"
    :on-confirm="acknowledge"
    :confirm-text="t('integration.credential.secretAck')"
    @update:show="onShowChange"
  >
    <n-alert type="warning" :bordered="false" class="secret-alert">
      {{ t('integration.credential.secretWarning') }}
    </n-alert>
    <n-input :value="apiKey" readonly class="secret-value" data-testid="integration-secret">
      <template #suffix>
        <n-button text type="primary" @click="copy">{{ t('integration.credential.copy') }}</n-button>
      </template>
    </n-input>
    <p class="secret-hint">{{ t('integration.credential.usageHint') }}</p>
  </FormContainer>
</template>

<style scoped>
.secret-alert {
  margin-bottom: var(--space-12, 12px);
}
.secret-value :deep(input) {
  font-family: var(--font-family-mono, monospace);
}
.secret-hint {
  margin: var(--space-8, 8px) 0 0;
  font-size: var(--font-size-sm, 13px);
  color: var(--color-text-secondary);
}
</style>
