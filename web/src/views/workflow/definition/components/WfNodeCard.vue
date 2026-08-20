<script setup lang="ts">
// 钉钉树节点卡:彩头 + 正文摘要 + 空态占位 + 右侧指向。
import { computed } from 'vue'
import { NButton, NTooltip } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import type { WfNode } from '@/workflow/schema'
import '../../wf-identity.css'

const props = defineProps<{
  node: WfNode
  active?: boolean
  error?: boolean
}>()
defineEmits<{ select: []; remove: [] }>()

const { t } = useI18n()

const tone = computed(() => {
  switch (props.node.type) {
    case 'start':
      return 'start'
    case 'approval':
      return 'approval'
    case 'cc':
      return 'cc'
    case 'branch':
      return 'branch'
    default:
      return 'end'
  }
})

const icon = computed(() => {
  switch (props.node.type) {
    case 'start':
      return 'ph:user'
    case 'approval':
      return 'ph:user-circle-check'
    case 'cc':
      return 'ph:paper-plane-tilt'
    case 'branch':
      return 'ph:git-branch'
    default:
      return 'ph:flag'
  }
})

const title = computed(() => props.node.name || t(`workflow.node.${props.node.type}`))

/** 正文摘要;未配办理人时显示对应空态占位。 */
const body = computed(() => {
  const n = props.node
  if (n.type === 'start') {
    const scope = n.props?.initiatorScope ?? []
    if (!scope.length) return { text: t('workflow.node.placeholder.start'), empty: false }
    return { text: t('workflow.node.initiatorCount', { n: scope.length }), empty: false }
  }
  if (n.type === 'branch') {
    return { text: t('workflow.designer.armCount', { count: n.conditions?.length ?? 0 }), empty: false }
  }

  const placeholder =
    n.type === 'cc' ? t('workflow.node.placeholder.cc') : t('workflow.node.placeholder.approval')
  const a = n.props?.assignee
  if (!a?.provider) return { text: placeholder, empty: true }

  const params = a.params ?? {}
  const providerLabel = t(`workflow.provider.${a.provider}`, a.provider)

  switch (a.provider) {
    case 'user': {
      const ids = Array.isArray(params.userIds)
        ? params.userIds
        : params.userId != null
          ? [params.userId]
          : []
      if (!ids.length) return { text: placeholder, empty: true }
      return { text: `${providerLabel} · ${ids.length}`, empty: false }
    }
    case 'role': {
      if (params.roleId == null && !(Array.isArray(params.roleIds) && params.roleIds.length)) {
        return { text: placeholder, empty: true }
      }
      return { text: providerLabel, empty: false }
    }
    case 'position': {
      if (params.positionId == null) return { text: placeholder, empty: true }
      return { text: providerLabel, empty: false }
    }
    case 'leader':
    case 'multiLeader': {
      const level = Number(params.level ?? 1) || 1
      return { text: `${providerLabel} · ${level}`, empty: false }
    }
    default:
      return { text: providerLabel, empty: false }
  }
})
</script>

<template>
  <div
    class="wf-card"
    :class="[`is-${tone}`, { 'is-active': active, 'is-error': error, 'is-root': node.type === 'start' }]"
    role="button"
    tabindex="0"
    @click="$emit('select')"
    @keydown.enter.self="$emit('select')"
    @keydown.space.self.prevent="$emit('select')"
  >
    <div class="wf-card-head">
      <AppIcon :icon="icon" :size="14" class="wf-card-head-icon" />
      <span class="wf-card-title">{{ title }}</span>
      <n-tooltip v-if="node.type !== 'start'">
        <template #trigger>
          <n-button
            text
            size="tiny"
            class="wf-card-del"
            :aria-label="t('common.delete')"
            @click.stop="$emit('remove')"
          >
            <template #icon><AppIcon icon="ph:x" :size="12" /></template>
          </n-button>
        </template>
        {{ t('common.delete') }}
      </n-tooltip>
    </div>
    <div class="wf-card-body">
      <span class="wf-card-text" :class="{ 'is-empty': body.empty }">{{ body.text }}</span>
      <AppIcon icon="ph:caret-right" :size="14" class="wf-card-arrow" />
    </div>
    <div v-if="error" class="wf-card-warn" :title="t('workflow.designer.invalid')">
      <AppIcon icon="ph:warning-circle" :size="18" />
    </div>
  </div>
</template>

<style scoped>
/* 类型色:发起鲜绿 / 审批橙 / 抄送蓝 / 结束炭灰。跟 tokens 并存,只用于节点身份。 */
.wf-card {
  --wf-head: var(--color-text-secondary);
  width: 220px;
  position: relative;
  border-radius: 8px;
  background: var(--color-bg-container);
  box-shadow: 0 1px 4px rgba(24, 26, 42, 0.08);
  cursor: pointer;
  text-align: left;
  outline: none;
  transition: box-shadow var(--transition-fast), transform var(--transition-fast);
}
.wf-card:not(.is-root)::before {
  content: '';
  position: absolute;
  top: -10px;
  left: 50%;
  transform: translateX(-50%);
  border-style: solid;
  border-width: 8px 6px 0;
  border-color: #c8c9cc transparent transparent;
}
.wf-card.is-start { --wf-head: var(--wf-start); }
.wf-card.is-approval { --wf-head: var(--wf-approval); }
.wf-card.is-cc { --wf-head: var(--wf-cc); }
.wf-card.is-branch { --wf-head: var(--color-primary); }
.wf-card.is-end { --wf-head: var(--wf-end); }

.wf-card:hover {
  box-shadow: var(--shadow-1), 0 0 0 1px color-mix(in srgb, var(--wf-head) 45%, transparent);
}
.wf-card.is-active {
  box-shadow: var(--shadow-1), 0 0 0 2px var(--wf-head);
}
.wf-card.is-error {
  box-shadow: 0 0 0 2px var(--color-danger);
}
.wf-card:focus-visible {
  box-shadow: var(--shadow-1), 0 0 0 2px var(--color-primary);
}

.wf-card-head {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 28px;
  padding: 0 12px;
  border-radius: 8px 8px 0 0;
  background: var(--wf-head);
  color: #fff;
  font-size: var(--font-size-xs);
  font-weight: 500;
  line-height: 1;
}
.wf-card-head-icon { flex-shrink: 0; }
.wf-card-title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.wf-card-del {
  margin-left: auto;
  color: #fff !important;
  opacity: 0;
  flex-shrink: 0;
}
.wf-card:hover .wf-card-del,
.wf-card:focus-within .wf-card-del { opacity: 0.9; }

.wf-card-body {
  display: flex;
  align-items: center;
  min-height: 52px;
  padding: 10px 12px 12px;
  gap: 8px;
}
.wf-card-text {
  flex: 1;
  min-width: 0;
  font-size: var(--font-size-sm);
  color: var(--color-text-secondary);
  line-height: var(--line-height-sm);
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
}
.wf-card-text.is-empty {
  color: var(--color-text-tertiary);
}
.wf-card-arrow {
  flex-shrink: 0;
  color: var(--color-text-disabled);
}
.wf-card-warn {
  position: absolute;
  right: -28px;
  top: 18px;
  color: var(--color-danger);
  display: flex;
}

:global(html[data-theme='dark']) .wf-card:not(.is-root)::before {
  border-top-color: #5a5c64;
}
</style>
