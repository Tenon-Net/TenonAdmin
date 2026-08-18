<script setup lang="ts">
// 节点间大圆加号;Popover 为图标+文字双列(M1 仅审批/抄送)。语言学 wflow InsertButton / Workflow-Vue3 addNode。
import { ref } from 'vue'
import { NPopover } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import type { WfNodeType } from '@/workflow/schema'
import '../../wf-identity.css'

const emit = defineEmits<{ add: [type: Extract<WfNodeType, 'approval' | 'cc'>] }>()
const { t } = useI18n()
const open = ref(false)

function pick(type: Extract<WfNodeType, 'approval' | 'cc'>) {
  open.value = false
  emit('add', type)
}
</script>

<template>
  <div class="wf-add">
    <n-popover v-model:show="open" trigger="click" placement="right-start" :show-arrow="false" :width="280">
      <template #trigger>
        <button type="button" class="wf-add-btn" :aria-label="t('workflow.designer.addNode')" :title="t('workflow.designer.addNode')">
          <AppIcon icon="ph:plus-bold" :size="16" />
        </button>
      </template>
      <div class="wf-add-grid" role="menu">
        <button type="button" class="wf-add-item" role="menuitem" @click="pick('approval')">
          <span class="wf-add-icon is-approval"><AppIcon icon="ph:user-circle-check" :size="22" /></span>
          <span class="wf-add-label">{{ t('workflow.node.approval') }}</span>
        </button>
        <button type="button" class="wf-add-item" role="menuitem" @click="pick('cc')">
          <span class="wf-add-icon is-cc"><AppIcon icon="ph:paper-plane-tilt" :size="22" /></span>
          <span class="wf-add-label">{{ t('workflow.node.cc') }}</span>
        </button>
      </div>
    </n-popover>
  </div>
</template>

<style scoped>
.wf-add {
  position: relative;
  display: flex;
  justify-content: center;
  width: 220px;
  padding: 18px 0 26px;
  flex-shrink: 0;
}
.wf-add::before {
  content: '';
  position: absolute;
  inset: 0;
  margin: auto;
  width: 2px;
  height: 100%;
  background: #c8c9cc;
  z-index: 0;
}
.wf-add-btn {
  position: relative;
  z-index: 1;
  width: 32px;
  height: 32px;
  border: none;
  border-radius: 50%;
  background: var(--wf-accent);
  color: #fff;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  box-shadow: 0 2px 6px rgba(24, 26, 42, 0.16);
  transition: transform 0.28s cubic-bezier(0.645, 0.045, 0.355, 1), box-shadow var(--transition-base);
}
.wf-add-btn:hover {
  transform: scale(1.28);
  box-shadow: 0 8px 18px rgba(24, 26, 42, 0.16);
}
.wf-add-btn:active {
  transform: scale(1);
}
.wf-add-btn:focus-visible {
  outline: 2px solid var(--wf-accent);
  outline-offset: 3px;
}

.wf-add-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
}
.wf-add-item {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 0;
  padding: 10px 12px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-fill);
  cursor: pointer;
  text-align: left;
  color: var(--color-text-primary);
  transition: background var(--transition-fast), box-shadow var(--transition-fast), border-color var(--transition-fast);
}
.wf-add-item:hover {
  background: var(--color-bg-elevated);
  border-color: var(--color-border-strong);
  box-shadow: var(--shadow-1);
}
.wf-add-icon {
  width: 36px;
  height: 36px;
  border-radius: 10px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  background: var(--color-bg-container);
  border: 1px solid var(--color-border);
}
.wf-add-icon.is-approval { color: var(--wf-approval); }
.wf-add-icon.is-cc { color: var(--wf-cc); }
.wf-add-label {
  font-size: var(--font-size-sm);
  font-weight: 500;
}

:global(html[data-theme='dark']) .wf-add::before {
  background: #5a5c64;
}
</style>
