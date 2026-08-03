<script setup lang="ts">
// 第三方登录运营 Tab:预置品牌全展示;未部署/未配密钥的不可打开「登录页显示」。
import { computed, onMounted, ref } from 'vue'
import { NAlert, NButton, NEmpty, NSpin, NSwitch, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import BrandIcon from '@/components/oauth/BrandIcon.vue'
import { configApi, externalAuthApi } from '@/api'
import { buildConfigProviderRows, type ConfigProviderRow } from '@/utils/oauthBrand'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const loading = ref(true)
const saving = ref(false)
const rows = ref<ConfigProviderRow[]>([])
const enabledMap = ref<Record<string, boolean>>({})
const baseline = ref<Record<string, boolean>>({})

const dirty = computed(() =>
  rows.value
    .filter((p) => p.registered)
    .some((p) => !!enabledMap.value[p.code] !== !!baseline.value[p.code]),
)
const enabledCount = computed(
  () => rows.value.filter((p) => p.registered && enabledMap.value[p.code]).length,
)
const registeredCount = computed(() => rows.value.filter((p) => p.registered).length)

async function load() {
  loading.value = true
  try {
    const list = await externalAuthApi.providersAll()
    const built = buildConfigProviderRows(list)
    rows.value = built
    const m: Record<string, boolean> = {}
    for (const p of built) m[p.code] = p.registered ? p.enabled : false
    enabledMap.value = { ...m }
    baseline.value = { ...m }
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}
onMounted(load)

async function save() {
  saving.value = true
  try {
    // 只写已注册项;未配置的永不写成 enabled=true
    const payload = rows.value
      .filter((p) => p.registered)
      .map((p) => ({
        configKey: `sys.externalauth.${p.code}.enabled`,
        configValue: String(!!enabledMap.value[p.code]),
      }))
    await configApi.saveBatch(payload)
    message.success(t('config.saved'))
    await load()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}

function reset() {
  enabledMap.value = { ...baseline.value }
}

function onToggle(code: string, registered: boolean, v: boolean) {
  if (!registered) return
  enabledMap.value = { ...enabledMap.value, [code]: v }
}
</script>

<template>
  <n-spin :show="loading">
    <div class="ea">
      <n-alert type="info" :bordered="false" class="ea-alert">
        {{ t('config.externalAuth.hint') }}
      </n-alert>

      <n-empty v-if="!loading && !rows.length" :description="t('config.externalAuth.empty')" class="ea-empty" />

      <template v-else-if="rows.length">
        <div class="ea-toolbar">
          <span class="ea-summary">
            {{
              t('config.externalAuth.summaryFull', {
                total: rows.length,
                registered: registeredCount,
                on: enabledCount,
              })
            }}
          </span>
          <div class="ea-toolbar-actions">
            <n-button v-if="dirty" quaternary size="small" @click="reset">
              {{ t('config.externalAuth.reset') }}
            </n-button>
            <n-button
              v-auth="'PUT:/api/v1/sys/config/batch'"
              type="primary"
              size="small"
              :loading="saving"
              :disabled="!dirty"
              @click="save"
            >
              <template #icon><AppIcon icon="ph:floppy-disk" :size="16" /></template>
              {{ t('common.save') }}
            </n-button>
          </div>
        </div>

        <div class="ea-grid">
          <div
            v-for="p in rows"
            :key="p.code"
            class="ea-card"
            :class="{
              'ea-card--on': p.registered && enabledMap[p.code],
              'ea-card--off': p.registered && !enabledMap[p.code],
              'ea-card--na': !p.registered,
            }"
          >
            <div class="ea-card-main">
              <span class="ea-avatar">
                <BrandIcon :code="p.code" :icon="p.icon" :size="36" />
              </span>
              <div class="ea-meta">
                <div class="ea-title-row">
                  <span class="ea-name">{{ p.displayName }}</span>
                  <n-tag
                    v-if="!p.registered"
                    size="small"
                    type="warning"
                    :bordered="false"
                    round
                  >
                    {{ t('config.externalAuth.notConfigured') }}
                  </n-tag>
                  <n-tag
                    v-else
                    size="small"
                    :type="enabledMap[p.code] ? 'success' : 'default'"
                    :bordered="false"
                    round
                  >
                    {{
                      enabledMap[p.code]
                        ? t('config.externalAuth.showOnLogin')
                        : t('config.externalAuth.hidden')
                    }}
                  </n-tag>
                </div>
                <code class="ea-code">{{ p.code }}</code>
                <p v-if="!p.registered" class="ea-na-tip">{{ t('config.externalAuth.notConfiguredTip') }}</p>
              </div>
            </div>
            <div class="ea-switch">
              <span class="ea-switch-label">{{ t('config.externalAuth.loginVisible') }}</span>
              <n-switch
                :value="!!enabledMap[p.code]"
                :disabled="!p.registered"
                size="medium"
                @update:value="(v) => onToggle(p.code, p.registered, v)"
              />
            </div>
          </div>
        </div>
      </template>
    </div>
  </n-spin>
</template>

<style scoped>
.ea {
  max-width: 920px;
}
.ea-alert {
  margin-bottom: 16px;
  border-radius: 10px;
}
.ea-empty {
  padding: 48px 0;
}
.ea-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
  flex-wrap: wrap;
}
.ea-summary {
  font-size: 13px;
  color: var(--color-text-secondary, #666);
}
.ea-toolbar-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}
.ea-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 12px;
}
.ea-card {
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
.ea-card:hover {
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.06);
}
.ea-card--off {
  opacity: 0.85;
  background: var(--color-fill-quaternary, #fafafa);
}
.ea-card--on {
  border-color: color-mix(in srgb, var(--color-primary, #18a058) 35%, var(--color-border, #e8e8e8));
}
.ea-card--na {
  opacity: 0.72;
  background: var(--color-fill-quaternary, #f7f7f7);
  border-style: dashed;
}
.ea-card-main {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  min-width: 0;
}
.ea-avatar {
  display: inline-flex;
  width: 44px;
  height: 44px;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  overflow: hidden;
  background: var(--color-fill, #f5f5f5);
}
.ea-meta {
  min-width: 0;
  flex: 1;
}
.ea-title-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 4px;
}
.ea-name {
  font-size: 15px;
  font-weight: 600;
  color: var(--color-text-primary, #1f1f1f);
  line-height: 1.3;
}
.ea-code {
  font-size: 12px;
  color: var(--color-text-tertiary, #999);
  background: var(--color-fill, #f5f5f5);
  padding: 1px 6px;
  border-radius: 4px;
}
.ea-na-tip {
  margin: 8px 0 0;
  font-size: 12px;
  line-height: 1.45;
  color: var(--color-text-tertiary, #999);
}
.ea-switch {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding-top: 12px;
  border-top: 1px dashed var(--color-border, #e8e8e8);
}
.ea-switch-label {
  font-size: 13px;
  color: var(--color-text-secondary, #666);
}
</style>
