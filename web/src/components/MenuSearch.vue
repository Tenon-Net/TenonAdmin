<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NModal, NInput, NScrollbar, NEmpty } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { onKeyStroke } from '@vueuse/core'
import { useI18n } from 'vue-i18n'
import { useMenuFlat, type MenuLeaf } from '@/composables/useMenuFlat'
import { isHttpUrl } from '@/utils/url'

type Hit = MenuLeaf & { display: string }

const show = defineModel<boolean>('show', { default: false })
const router = useRouter()
const { t } = useI18n()
const leaves = useMenuFlat()

const q = ref('')
const cursor = ref(0)
const inputRef = ref<InstanceType<typeof NInput>>()

// 标题含 '.' 视为 i18n key(如 menu.dashboard)、否则为字面量——与 useMenuFlat 同一约定。
function label(item: MenuLeaf) {
  return item.title.includes('.') ? t(item.title) : item.title
}

const results = computed<Hit[]>(() => {
  const list: Hit[] = leaves.value.map((l) => ({ ...l, display: label(l) }))
  const kw = q.value.trim().toLowerCase()
  if (!kw) return list
  return list.filter((l) => l.display.toLowerCase().includes(kw) || l.path.toLowerCase().includes(kw))
})

watch(results, () => (cursor.value = 0))
watch(show, (v) => {
  if (v) {
    q.value = ''
    cursor.value = 0
    nextTick(() => inputRef.value?.focus())
  }
})

function go(item?: Hit) {
  if (item) {
    if (isHttpUrl(item.path)) window.open(item.path, '_blank', 'noopener,noreferrer')
    else router.push(item.path)
  }
  show.value = false
}

// esc/highlight 产物走模板 v-html:必须先转义原文、再插 <mark>,顺序颠倒即 XSS。
function esc(s: string) {
  return s.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c] ?? c)
}
function highlight(text: string) {
  const kw = q.value.trim()
  const safe = esc(text)
  if (!kw) return safe
  const k = esc(kw)
  const i = safe.toLowerCase().indexOf(k.toLowerCase())
  if (i === -1) return safe
  return safe.slice(0, i) + '<mark>' + safe.slice(i, i + k.length) + '</mark>' + safe.slice(i + k.length)
}

// 全局热键 Ctrl/Cmd+K 开关面板
onKeyStroke('k', (e) => {
  if (e.ctrlKey || e.metaKey) {
    e.preventDefault()
    show.value = !show.value
  }
})
onKeyStroke('ArrowDown', (e) => {
  if (!show.value) return
  e.preventDefault()
  cursor.value = Math.min(cursor.value + 1, results.value.length - 1)
})
onKeyStroke('ArrowUp', (e) => {
  if (!show.value) return
  e.preventDefault()
  cursor.value = Math.max(cursor.value - 1, 0)
})
onKeyStroke('Enter', (e) => {
  if (!show.value) return
  e.preventDefault()
  go(results.value[cursor.value])
})
</script>

<template>
  <n-modal v-model:show="show" transform-origin="center">
    <div class="palette">
      <div class="search">
        <Icon icon="ph:magnifying-glass" :width="18" class="sicon" />
        <n-input
          ref="inputRef"
          v-model:value="q"
          :placeholder="t('settings.searchPlaceholder')"
          :bordered="false"
          size="large"
        />
      </div>
      <n-scrollbar style="max-height: 52vh">
        <div v-if="results.length" class="list">
          <div
            v-for="(item, i) in results"
            :key="item.path"
            class="item"
            :class="{ active: i === cursor }"
            @click="go(item)"
            @mouseenter="cursor = i"
          >
            <Icon v-if="item.icon" :icon="item.icon" :width="18" class="iicon" />
            <span class="ititle" v-html="highlight(item.display)" />
            <span class="ipath">{{ item.breadcrumb.join(' / ') }}</span>
          </div>
        </div>
        <n-empty v-else :description="t('settings.searchEmpty')" class="empty" />
      </n-scrollbar>
      <div class="hint">{{ t('settings.searchHint') }}</div>
    </div>
  </n-modal>
</template>

<style scoped>
.palette {
  width: 560px;
  max-width: 92vw;
  background: var(--color-bg-container);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-3);
  overflow: hidden;
}
.search {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 16px;
  border-bottom: 1px solid var(--color-border);
}
.sicon {
  color: var(--color-text-tertiary);
  flex-shrink: 0;
}
.list {
  padding: 8px;
}
.item {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 44px;
  padding: 0 12px;
  border-radius: var(--radius-md);
  cursor: pointer;
  color: var(--color-text-primary);
}
.item.active {
  background: var(--color-primary-light);
  color: var(--color-primary);
}
.iicon {
  flex-shrink: 0;
}
.ititle {
  font-size: var(--font-size-md);
}
.ititle :deep(mark) {
  background: transparent;
  color: var(--color-primary);
  font-weight: 600;
}
.ipath {
  margin-left: auto;
  font-size: var(--font-size-sm);
  color: var(--color-text-tertiary);
}
.empty {
  padding: 32px 0;
}
.hint {
  padding: 8px 16px;
  border-top: 1px solid var(--color-border);
  font-size: var(--font-size-sm);
  color: var(--color-text-tertiary);
}
</style>
