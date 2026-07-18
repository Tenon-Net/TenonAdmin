<script setup lang="ts">
// 账号绑定(个人中心,批次 D):把第三方账号绑到本账号,以后可一键 SSO 登录。
// [ActiveSession] 人人可用,静态路由不进菜单(理由同 我的会话)。绑定要走一次 OAuth 往返证明拥有该外部身份。
import { onMounted, ref } from 'vue'
import { NCard, NList, NListItem, NThing, NButton, NTag, NEmpty, NSpin, useMessage } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { externalAuthApi, type ExternalProvider, type ExternalBinding } from '@/api'
import { useConfirm } from '@/composables/useConfirm'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const { confirm } = useConfirm()

const loading = ref(true)
const providers = ref<ExternalProvider[]>([])
const bindings = ref<ExternalBinding[]>([])

const bindingOf = (code: string) => bindings.value.find((b) => b.provider === code)
const fmt = (s?: string | null) => (s ?? '').replace('T', ' ').slice(0, 19)

async function load() {
  loading.value = true
  try {
    const [ps, bs] = await Promise.all([externalAuthApi.providers(), externalAuthApi.bindings()])
    providers.value = ps
    bindings.value = bs
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}
onMounted(load)

async function bind(p: ExternalProvider) {
  try {
    const { authorizeUrl } = await externalAuthApi.bindStart(p.code)
    window.location.href = authorizeUrl // 顶层跳转开始 OAuth 往返;回调把身份绑到当前用户并回到本页
  } catch (e) {
    message.error(translateError(e))
  }
}

function unbind(p: ExternalProvider) {
  confirm({
    type: 'warning',
    content: t('oauth.unbindConfirm', { name: p.displayName }),
    action: () => externalAuthApi.unbind(p.code),
    successMsg: t('oauth.unbound'),
  }).then((ok) => {
    if (ok) load()
  })
}
</script>

<template>
  <n-card :bordered="true" :title="t('oauth.bindingsTitle')" style="max-width: 720px">
    <p class="hint">{{ t('oauth.bindingsHint') }}</p>
    <n-spin v-if="loading" />
    <n-empty v-else-if="!providers.length" :description="t('oauth.noProviders')" />
    <n-list v-else bordered>
      <n-list-item v-for="p in providers" :key="p.code">
        <template #suffix>
          <n-button v-if="bindingOf(p.code)" size="small" quaternary type="error" @click="unbind(p)">
            {{ t('oauth.unbind') }}
          </n-button>
          <n-button v-else size="small" type="primary" secondary @click="bind(p)">
            {{ t('oauth.bind') }}
          </n-button>
        </template>
        <n-thing>
          <template #avatar><Icon :icon="p.icon || 'ph:link-simple'" :width="26" /></template>
          <template #header>{{ p.displayName }}</template>
          <template #description>
            <n-tag v-if="bindingOf(p.code)" type="success" size="small" :bordered="false">
              {{ t('oauth.boundAt', { time: fmt(bindingOf(p.code)!.boundAt) }) }}
            </n-tag>
            <span v-else class="unbound">{{ t('oauth.notBound') }}</span>
          </template>
        </n-thing>
      </n-list-item>
    </n-list>
  </n-card>
</template>

<style scoped>
.hint {
  margin: 0 0 14px;
  font-size: 13px;
  line-height: 1.6;
  color: var(--color-text-secondary, #999);
}
.unbound {
  font-size: 13px;
  color: var(--color-text-tertiary, #aaa);
}
</style>
