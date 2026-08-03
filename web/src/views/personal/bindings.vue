<script setup lang="ts">
// 账号绑定(个人中心 + 品牌化):启用 providers ∪ 已绑定停用项(B-A);卡片网格与配置 Tab 同风。
import { computed, onMounted, ref } from 'vue'
import { NAlert, NButton, NEmpty, NSpin, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { externalAuthApi, type ExternalProvider, type ExternalBinding } from '@/api'
import { useConfirm } from '@/composables/useConfirm'
import { translateError } from '@/utils/error'
import { mergeBindingRows, type BindingRow } from '@/utils/oauthBrand'
import BrandIcon from '@/components/oauth/BrandIcon.vue'
import AppIcon from '@/components/AppIcon.vue'

const { t } = useI18n()
const message = useMessage()
const { confirm } = useConfirm()

const loading = ref(true)
const providers = ref<ExternalProvider[]>([])
const bindings = ref<ExternalBinding[]>([])
const busyCode = ref<string | null>(null)

const rows = computed(() => mergeBindingRows(providers.value, bindings.value))
const boundCount = computed(() => rows.value.filter((r) => !!r.binding).length)
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

async function bind(row: BindingRow) {
  if (!row.enabled || busyCode.value) return
  busyCode.value = row.code
  try {
    const { authorizeUrl } = await externalAuthApi.bindStart(row.code)
    window.location.href = authorizeUrl
  } catch (e) {
    message.error(translateError(e))
    busyCode.value = null
  }
}

function unbind(row: BindingRow) {
  if (busyCode.value) return
  confirm({
    type: 'warning',
    content: t('oauth.unbindConfirm', { name: row.displayName }),
    action: () => externalAuthApi.unbind(row.code),
    successMsg: t('oauth.unbound'),
  }).then((ok) => {
    if (ok) load()
  })
}

function cardClass(row: BindingRow) {
  if (!row.enabled) return 'bind-card--disabled'
  if (row.binding) return 'bind-card--bound'
  return 'bind-card--free'
}
</script>

<template>
  <div class="bind">
    <header class="bind-header">
      <div class="bind-header-text">
        <h2 class="bind-title">{{ t('oauth.bindingsTitle') }}</h2>
        <p class="bind-hint">{{ t('oauth.bindingsHint') }}</p>
      </div>
      <div v-if="!loading && rows.length" class="bind-summary">
        <span class="bind-summary-num">{{ boundCount }}</span>
        <span class="bind-summary-sep">/</span>
        <span>{{ rows.length }}</span>
        <span class="bind-summary-label">{{ t('oauth.boundCountLabel') }}</span>
      </div>
    </header>

    <n-alert type="info" :bordered="false" class="bind-alert">
      {{ t('oauth.bindingsTip') }}
    </n-alert>

    <n-spin :show="loading">
      <n-empty v-if="!loading && !rows.length" :description="t('oauth.noProviders')" class="bind-empty" />

      <div v-else-if="rows.length" class="bind-grid">
        <article
          v-for="row in rows"
          :key="row.code"
          class="bind-card"
          :class="cardClass(row)"
        >
          <div class="bind-card-main">
            <span class="bind-avatar" :class="{ 'bind-avatar--bound': !!row.binding }">
              <BrandIcon :code="row.code" :icon="row.icon" :size="44" />
              <span v-if="row.binding" class="bind-check" aria-hidden="true">
                <AppIcon icon="ph:check-bold" :size="12" />
              </span>
            </span>
            <div class="bind-meta">
              <div class="bind-title-row">
                <span class="bind-name">{{ row.displayName }}</span>
                <n-tag
                  v-if="!row.enabled"
                  size="small"
                  type="warning"
                  :bordered="false"
                  round
                >
                  {{ t('oauth.disabled') }}
                </n-tag>
                <n-tag
                  v-else-if="row.binding"
                  size="small"
                  type="success"
                  :bordered="false"
                  round
                >
                  {{ t('oauth.bound') }}
                </n-tag>
                <n-tag v-else size="small" :bordered="false" round>
                  {{ t('oauth.notBound') }}
                </n-tag>
              </div>
              <code class="bind-code">{{ row.code }}</code>
              <p v-if="row.binding" class="bind-time">
                {{ t('oauth.boundAt', { time: fmt(row.binding.boundAt) }) }}
              </p>
              <p v-else-if="!row.enabled" class="bind-disabled-tip">
                {{ t('oauth.disabledTip') }}
              </p>
              <p v-else class="bind-free-tip">{{ t('oauth.bindTip') }}</p>
            </div>
          </div>

          <div class="bind-actions">
            <n-button
              v-if="row.binding"
              size="small"
              quaternary
              type="error"
              :disabled="!!busyCode"
              @click="unbind(row)"
            >
              <template #icon><AppIcon icon="ph:link-break" :size="15" /></template>
              {{ t('oauth.unbind') }}
            </n-button>
            <n-button
              v-else-if="row.enabled"
              size="small"
              type="primary"
              secondary
              :loading="busyCode === row.code"
              :disabled="!!busyCode && busyCode !== row.code"
              @click="bind(row)"
            >
              <template #icon><AppIcon icon="ph:link" :size="15" /></template>
              {{ t('oauth.bind') }}
            </n-button>
            <span v-else class="bind-na">{{ t('oauth.cannotBind') }}</span>
          </div>
        </article>
      </div>
    </n-spin>
  </div>
</template>

<style scoped>
.bind {
  max-width: 920px;
}
.bind-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 14px;
  flex-wrap: wrap;
}
.bind-title {
  margin: 0 0 6px;
  font-size: 18px;
  font-weight: 600;
  line-height: 1.3;
  color: var(--color-text-primary, #1f1f1f);
}
.bind-hint {
  margin: 0;
  max-width: 520px;
  font-size: 13px;
  line-height: 1.55;
  color: var(--color-text-secondary, #666);
}
.bind-summary {
  display: inline-flex;
  align-items: baseline;
  gap: 4px;
  padding: 8px 12px;
  border-radius: 10px;
  background: var(--color-fill, #f5f5f5);
  font-size: 13px;
  color: var(--color-text-secondary, #666);
  flex-shrink: 0;
}
.bind-summary-num {
  font-size: 18px;
  font-weight: 700;
  color: var(--color-primary, #646cff);
  font-variant-numeric: tabular-nums;
}
.bind-summary-sep {
  opacity: 0.5;
}
.bind-summary-label {
  margin-left: 4px;
}
.bind-alert {
  margin-bottom: 16px;
  border-radius: 10px;
}
.bind-empty {
  padding: 48px 0;
}
.bind-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 12px;
}
.bind-card {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 16px;
  border-radius: 12px;
  border: 1px solid var(--color-border, #e8e8e8);
  background: var(--color-bg-container, #fff);
  transition:
    border-color 0.15s ease,
    box-shadow 0.15s ease,
    opacity 0.15s ease;
}
.bind-card:hover {
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.06);
}
.bind-card--bound {
  border-color: color-mix(in srgb, var(--color-success, #18a058) 40%, var(--color-border, #e8e8e8));
  background: color-mix(in srgb, var(--color-success, #18a058) 4%, var(--color-bg-container, #fff));
}
.bind-card--free {
  /* default card */
}
.bind-card--disabled {
  opacity: 0.78;
  background: var(--color-fill-quaternary, #f7f7f7);
  border-style: dashed;
}
.bind-card-main {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  min-width: 0;
}
.bind-avatar {
  position: relative;
  display: inline-flex;
  width: 48px;
  height: 48px;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  overflow: visible;
  background: var(--color-fill, #f5f5f5);
}
.bind-avatar :deep(.oauth-brand-badge),
.bind-avatar :deep(img) {
  border-radius: 50%;
  overflow: hidden;
}
.bind-avatar--bound {
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--color-success, #18a058) 35%, transparent);
}
.bind-check {
  position: absolute;
  right: -2px;
  bottom: -2px;
  display: inline-flex;
  width: 18px;
  height: 18px;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background: var(--color-success, #18a058);
  color: #fff;
  border: 2px solid var(--color-bg-container, #fff);
  line-height: 0;
}
.bind-meta {
  min-width: 0;
  flex: 1;
}
.bind-title-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 4px;
}
.bind-name {
  font-size: 15px;
  font-weight: 600;
  color: var(--color-text-primary, #1f1f1f);
  line-height: 1.3;
}
.bind-code {
  display: inline-block;
  font-size: 12px;
  color: var(--color-text-tertiary, #999);
  background: var(--color-fill, #f5f5f5);
  padding: 1px 6px;
  border-radius: 4px;
}
.bind-time,
.bind-free-tip,
.bind-disabled-tip {
  margin: 8px 0 0;
  font-size: 12px;
  line-height: 1.45;
  color: var(--color-text-tertiary, #999);
}
.bind-time {
  color: var(--color-text-secondary, #666);
}
.bind-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 12px;
  border-top: 1px dashed var(--color-border, #e8e8e8);
  min-height: 36px;
}
.bind-na {
  font-size: 12px;
  color: var(--color-text-tertiary, #999);
}
</style>
