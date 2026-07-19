<script setup lang="ts">
// 缓存管理 = 定向失效动作卡片。只清不看:缓存值含明文验证码/一次性令牌、键内嵌手机号/IP(后端故无键浏览/取值端点)。
// 每张卡二次确认后执行,toast 显被清条数。正常授权变更后端已自动失效,此页面向"直接改库"等旁路场景做手动刷新。
import { ref } from 'vue'
import { NGrid, NGi, NCard, NButton, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import { useConfirm } from '@/composables/useConfirm'
import { cacheApi } from '@/api'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const { ask } = useConfirm()

interface CacheAction {
  key: 'auth' | 'dict' | 'config' | 'portal'
  icon: string
  perm: string
  run: () => Promise<number>
}

const actions: CacheAction[] = [
  { key: 'auth', icon: 'ph:shield-check-duotone', perm: 'POST:/api/v1/sys/cache/flush-auth', run: cacheApi.flushAuth },
  { key: 'dict', icon: 'ph:book-open-text-duotone', perm: 'POST:/api/v1/sys/cache/flush-dict', run: cacheApi.flushDict },
  { key: 'config', icon: 'ph:sliders-horizontal-duotone', perm: 'POST:/api/v1/sys/cache/flush-config', run: cacheApi.flushConfig },
  { key: 'portal', icon: 'ph:squares-four-duotone', perm: 'POST:/api/v1/sys/cache/rebuild-portal', run: cacheApi.rebuildPortal },
]

const busy = ref<string | null>(null)

async function trigger(a: CacheAction) {
  if (!(await ask({ type: 'warning', content: t(`cache.${a.key}Confirm`) }))) return
  busy.value = a.key
  try {
    const n = await a.run()
    // 门户是"重建代际"语义(返回新代际值,非清除条数),单独文案;其余显被清条数
    message.success(a.key === 'portal' ? t('cache.portalDone') : t('cache.flushed', { n }))
  } catch (e) {
    message.error(translateError(e))
  } finally {
    busy.value = null
  }
}
</script>

<template>
  <div class="cache">
    <n-grid cols="1 s:2 l:2" responsive="screen" :x-gap="12" :y-gap="12">
      <n-gi v-for="a in actions" :key="a.key" v-auth="a.perm">
        <n-card size="small" class="action-card">
          <div class="head">
            <AppIcon :icon="a.icon" :size="22" />
            <span class="title">{{ t(`cache.${a.key}Title`) }}</span>
          </div>
          <p class="desc">{{ t(`cache.${a.key}Desc`) }}</p>
          <n-button type="primary" secondary :loading="busy === a.key" @click="trigger(a)">
            <template #icon><AppIcon icon="ph:broom" :size="16" /></template>{{ t('cache.execute') }}
          </n-button>
        </n-card>
      </n-gi>
    </n-grid>
    <p class="note">{{ t('cache.note') }}</p>
  </div>
</template>

<style scoped>
.cache {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.action-card {
  height: 100%;
}
.head {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 15px;
  font-weight: 600;
}
.desc {
  margin: 10px 0 14px;
  font-size: 13px;
  line-height: 1.6;
  color: var(--color-text-secondary, #999);
  min-height: 42px;
}
.note {
  margin: 4px 0 0;
  font-size: 12px;
  line-height: 1.6;
  color: var(--color-text-secondary, #999);
}
</style>
