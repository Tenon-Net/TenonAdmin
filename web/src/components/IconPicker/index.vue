<script setup lang="ts">
// 离线优先图标选择器。骨架参照 XiHan packages/iconify/IconPicker.vue(NModal+NTabs+NGrid,按 Tab 懒加载+缓存),
// 值契约单串 `prefix:name`(直接落进 menu.icon),另补本地 SVG 页与在线自由输入页(XiHan 未有)。
import { computed, ref, watch } from 'vue'
import { NButton, NEmpty, NInput, NModal, NScrollbar, NTab, NTabs } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
// 内置图标集(Tab)在 @/lib/icons 的 ICON_SET_META 配置 —— 增删默认集看那里的说明。
import { ICON_SET_META, LOCAL_PREFIX, loadIconNames, localIconNames } from '@/lib/icons'

const model = defineModel<string>({ default: '' })
const props = withDefaults(defineProps<{ placeholder?: string; clearable?: boolean }>(), {
  placeholder: '',
  clearable: true,
})

const { t } = useI18n()

const LOCAL_TAB = '__local__'
const ONLINE_TAB = '__online__'
const CAP = 300 // 单次可见上限,超出提示继续输入缩小范围(避免一次渲染上千图标)

const show = ref(false)
const active = ref('ph')
const keyword = ref('')
const names = ref<string[]>([])
const loading = ref(false)
const onlineInput = ref('')
const isOnline = ref(true)
const cache: Record<string, string[]> = {}

const placeholderText = computed(() => props.placeholder || t('iconPicker.placeholder'))

const sourceNames = computed(() => (active.value === LOCAL_TAB ? localIconNames : names.value))
const filtered = computed(() => {
  const k = keyword.value.trim().toLowerCase()
  return k ? sourceNames.value.filter((n) => n.toLowerCase().includes(k)) : sourceNames.value
})
const visibleNames = computed(() => filtered.value.slice(0, CAP))
const overflow = computed(() => filtered.value.length - visibleNames.value.length)

function iconId(name: string): string {
  return active.value === LOCAL_TAB ? `${LOCAL_PREFIX}:${name}` : `${active.value}:${name}`
}

async function loadTab() {
  const prefix = active.value
  if (prefix === LOCAL_TAB || prefix === ONLINE_TAB) {
    loading.value = false
    return
  }
  if (cache[prefix]) {
    names.value = cache[prefix]
    return
  }
  loading.value = true
  try {
    const list = await loadIconNames(prefix)
    cache[prefix] = list
    if (active.value === prefix) names.value = list
  } finally {
    if (active.value === prefix) loading.value = false
  }
}

watch(active, () => {
  keyword.value = ''
  loadTab()
})
watch(show, (v) => {
  if (v) {
    keyword.value = ''
    onlineInput.value = ''
    isOnline.value = navigator.onLine
    loadTab()
  }
})

function pick(name: string) {
  model.value = iconId(name)
  show.value = false
}
function pickOnline() {
  const v = onlineInput.value.trim()
  if (v) {
    model.value = v
    show.value = false
  }
}
function clear() {
  model.value = ''
}
</script>

<template>
  <div class="icon-picker">
    <div class="trigger" :class="{ empty: !model }" @click="show = true">
      <AppIcon v-if="model" :icon="model" :size="18" />
      <span class="val">{{ model || placeholderText }}</span>
      <span v-if="props.clearable && model" class="clear" @click.stop="clear">
        <AppIcon icon="ph:x" :size="13" />
      </span>
    </div>

    <n-modal
      v-model:show="show"
      preset="card"
      :title="t('iconPicker.title')"
      class="ip-modal"
      :style="{ width: '600px', maxWidth: '94vw' }"
      :bordered="false"
    >
      <div class="ip-search">
        <n-input v-model:value="keyword" :placeholder="t('iconPicker.search')" clearable>
          <template #prefix><AppIcon icon="ph:magnifying-glass" :size="16" /></template>
        </n-input>
      </div>

      <n-tabs :value="active" type="line" size="small" @update:value="(v: string) => (active = v)">
        <n-tab v-for="m in ICON_SET_META" :key="m.prefix" :name="m.prefix" :tab="m.name" />
        <n-tab :name="LOCAL_TAB" :tab="t('iconPicker.local')" />
        <n-tab :name="ONLINE_TAB" :tab="t('iconPicker.online')" />
      </n-tabs>

      <n-scrollbar class="ip-scroll">
        <!-- 在线自由输入(联网时用在线 API 预览,离线降级提示) -->
        <div v-if="active === ONLINE_TAB" class="ip-online">
          <n-input
            v-model:value="onlineInput"
            :placeholder="t('iconPicker.onlinePlaceholder')"
            @keyup.enter="pickOnline"
          >
            <template #suffix>
              <AppIcon v-if="onlineInput.trim()" :icon="onlineInput.trim()" :size="20" />
            </template>
          </n-input>
          <n-button type="primary" :disabled="!onlineInput.trim()" @click="pickOnline">
            {{ t('iconPicker.use') }}
          </n-button>
          <p v-if="!isOnline" class="ip-hint">{{ t('iconPicker.offlineHint') }}</p>
        </div>

        <!-- 图标网格 -->
        <div v-else-if="loading" class="ip-state">{{ t('common.loading') }}</div>
        <n-empty v-else-if="!visibleNames.length" class="ip-state" :description="t('iconPicker.empty')" />
        <template v-else>
          <div class="ip-grid">
            <button
              v-for="name in visibleNames"
              :key="name"
              type="button"
              class="cell"
              :class="{ sel: model === iconId(name) }"
              :title="iconId(name)"
              @click="pick(name)"
            >
              <AppIcon :icon="iconId(name)" :size="22" />
              <span class="cell-name">{{ name }}</span>
            </button>
          </div>
          <p v-if="overflow > 0" class="ip-more">{{ t('iconPicker.more', { n: overflow }) }}</p>
        </template>
      </n-scrollbar>
    </n-modal>
  </div>
</template>

<style scoped>
.trigger {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 34px;
  padding: 0 10px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg-container);
  color: var(--color-text-primary);
  cursor: pointer;
  transition: border-color 0.2s;
}
.trigger:hover {
  border-color: var(--color-border-strong);
}
.trigger.empty .val {
  color: var(--color-text-tertiary);
}
.val {
  flex: 1;
  min-width: 0;
  font-size: var(--font-size-sm);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.clear {
  display: inline-flex;
  color: var(--color-text-tertiary);
}
.clear:hover {
  color: var(--color-text-primary);
}

.ip-search {
  margin-bottom: 8px;
}
.ip-scroll {
  max-height: 46vh;
}
.ip-state {
  padding: 40px 0;
  text-align: center;
  color: var(--color-text-tertiary);
}
.ip-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(76px, 1fr));
  gap: 8px;
  padding: 8px 4px;
}
.cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 10px 4px;
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--color-text-secondary);
  cursor: pointer;
  transition: all 0.15s;
}
.cell:hover {
  background: var(--color-fill-hover);
  color: var(--color-text-primary);
}
.cell.sel {
  background: var(--color-primary-light);
  border-color: var(--color-primary);
  color: var(--color-primary);
}
.cell-name {
  max-width: 100%;
  font-size: 11px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ip-more {
  padding: 4px 0 8px;
  text-align: center;
  font-size: var(--font-size-sm);
  color: var(--color-text-tertiary);
}
.ip-online {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  padding: 16px 4px;
}
.ip-hint {
  width: 100%;
  margin: 0;
  font-size: var(--font-size-sm);
  color: var(--color-text-tertiary);
}
</style>
