<script setup lang="ts">
import { NEmpty, NTag } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import type { WfHisTask, WfTaskParticipant } from '@/types/workflow'
import { ACTOR_STATUS, countersignProgress, participantHistory, type ApprovalRound } from '@/workflow/approvalProgress'
import { approvalProgressActionTagType, formatWorkflowDateTime, taskActionKey } from '@/workflow/statusLabels'

const props = defineProps<{
  rounds: ApprovalRound[]
}>()

const { t } = useI18n()

function personName(id?: number | string | null, name?: string | null) {
  const displayName = name?.trim()
  if (displayName) return displayName
  if (id == null || id === '' || String(id) === '0') return t('workflow.detail.approvalProgress.system')
  return t('workflow.detail.userFallback', { id })
}

function participantName(item: WfTaskParticipant) {
  return personName(item.userId, item.displayName)
}

function isAutomatic(item: WfHisTask | undefined) {
  return item?.isAutomatic === true
}

function historyOperator(item: WfHisTask) {
  const operator = personName(item.userId, item.operatorName)
  return isAutomatic(item)
    ? t('workflow.detail.approvalProgress.automaticActor', { name: operator })
    : operator
}

function participantDisplayName(round: ApprovalRound, person: WfTaskParticipant) {
  const action = participantAction(round, person)
  return isAutomatic(action)
    ? t('workflow.detail.approvalProgress.automaticActor', { name: participantName(person) })
    : participantName(person)
}

function originalName(item: WfTaskParticipant) {
  if (item.originalUserId == null) return ''
  return personName(item.originalUserId, item.originalDisplayName)
}

function effectiveStatus(status: WfTaskParticipant['status'], current: boolean) {
  if (!current && [ACTOR_STATUS.waiting, ACTOR_STATUS.pending].includes(Number(status) as 0 | 1)) {
    return ACTOR_STATUS.skipped
  }
  return Number(status)
}

function actorStatusLabel(status: WfTaskParticipant['status'], current: boolean) {
  const key: Record<number, string> = {
    [ACTOR_STATUS.waiting]: 'waiting',
    [ACTOR_STATUS.pending]: 'pending',
    [ACTOR_STATUS.done]: 'done',
    [ACTOR_STATUS.skipped]: 'skipped',
  }
  return t(`workflow.detail.approvalProgress.status.${key[effectiveStatus(status, current)] ?? 'unknown'}`)
}

function actorStatusType(status: WfTaskParticipant['status'], current: boolean): 'default' | 'success' | 'info' | 'warning' {
  const value = effectiveStatus(status, current)
  if (value === ACTOR_STATUS.done) return 'success'
  if (value === ACTOR_STATUS.pending) return 'info'
  if (value === ACTOR_STATUS.waiting) return 'warning'
  return 'default'
}

function actionLabel(action: WfHisTask['action']) {
  return t(`workflow.action.${taskActionKey(action)}`)
}

const actionType = approvalProgressActionTagType
const formatTime = formatWorkflowDateTime

function participantAction(round: ApprovalRound, person: WfTaskParticipant) {
  const status = Number(person.status)
  if (round.current && (status === ACTOR_STATUS.pending || status === ACTOR_STATUS.waiting)) return undefined
  return participantHistory(round, person)
}

function ratioText(round: ApprovalRound) {
  const ratio = countersignProgress(round)
  return ratio ? t('workflow.detail.approvalProgress.ratio', ratio) : ''
}

function modeLabel(mode: ApprovalRound['approvalMode']) {
  const key: Record<string, string> = { any: 'any', all: 'all', seq: 'seq' }
  return mode == null ? '' : t(`workflow.mode.${key[String(mode)] ?? String(mode)}`, String(mode))
}

function roundLabel(round: ApprovalRound) {
  if (round.current) return t('workflow.detail.approvalProgress.currentRound')
  const historyIndex = props.rounds.filter((item) => !item.current).findIndex((item) => item.key === round.key)
  return t('workflow.detail.approvalProgress.historyRound', { n: historyIndex + 1 })
}

function transferTarget(item: WfHisTask) {
  if (item.transferToUserId == null) return ''
  return personName(item.transferToUserId, item.transferToUserName)
}

function signTarget(item: WfHisTask) {
  if (item.targetUserId == null) return ''
  return personName(item.targetUserId, item.targetUserName)
}
</script>

<template>
  <div class="wf-approval-progress" data-testid="wf-approval-progress">
    <n-empty v-if="!rounds.length" :description="t('workflow.detail.approvalProgress.noRounds')" size="small" />
    <template v-for="round in rounds" :key="round.key">
      <section v-if="round.current" class="wf-progress-round is-current">
        <header class="wf-progress-round-head">
          <strong>{{ roundLabel(round) }}</strong>
          <n-tag v-if="modeLabel(round.approvalMode)" size="small" :bordered="false">
            {{ modeLabel(round.approvalMode) }}
          </n-tag>
          <span v-if="ratioText(round)" class="wf-progress-ratio">{{ ratioText(round) }}</span>
        </header>
        <div class="wf-progress-people">
          <div v-for="person in round.participants" :key="person.actorId" class="wf-progress-person">
            <div>
              <strong>{{ participantDisplayName(round, person) }}</strong>
              <span v-if="originalName(person)" class="wf-progress-original">
                {{ t('workflow.detail.approvalProgress.originalActor', { name: originalName(person) }) }}
              </span>
            </div>
            <div class="wf-progress-person-status">
              <n-tag
                size="small"
                :type="participantAction(round, person) ? actionType(participantAction(round, person)?.action) : actorStatusType(person.status, true)"
                :bordered="false"
              >
                {{ participantAction(round, person) ? actionLabel(participantAction(round, person)?.action) : actorStatusLabel(person.status, true) }}
              </n-tag>
              <time v-if="participantAction(round, person)?.createTime || person.activatedTime">
                {{ formatTime(participantAction(round, person)?.createTime ?? person.activatedTime) }}
              </time>
            </div>
          </div>
        </div>
        <div v-if="round.history.length" class="wf-progress-actions">
          <div v-for="item in round.history" :key="item.id" class="wf-progress-action">
            <div class="wf-progress-action-head">
              <strong>{{ historyOperator(item) }}</strong>
              <n-tag size="small" :bordered="false">{{ actionLabel(item.action) }}</n-tag>
              <time>{{ formatTime(item.createTime) }}</time>
            </div>
            <p v-if="item.comment">{{ item.comment }}</p>
            <p v-if="item.originalUserId != null">
              {{ t('workflow.detail.approvalProgress.originalActor', { name: personName(item.originalUserId, item.originalUserName) }) }}
            </p>
            <p v-if="transferTarget(item)">{{ t('workflow.detail.transferTo', { name: transferTarget(item) }) }}</p>
            <p v-if="signTarget(item)">{{ t('workflow.detail.signTarget', { name: signTarget(item) }) }}</p>
          </div>
        </div>
        <details class="wf-progress-technical">
          <summary>{{ t('workflow.detail.approvalProgress.technical') }}</summary>
          <code>Task #{{ round.taskId ?? '—' }} · Token #{{ round.tokenId ?? '—' }} · NodeVisit #{{ round.nodeVisitId ?? '—' }}</code>
        </details>
      </section>

      <details v-else class="wf-progress-round">
        <summary class="wf-progress-round-head">
          <strong>{{ roundLabel(round) }}</strong>
          <span v-if="ratioText(round)" class="wf-progress-ratio">{{ ratioText(round) }}</span>
          <span>{{ formatTime(round.history.at(-1)?.createTime ?? round.createdTime) }}</span>
        </summary>
        <div class="wf-progress-people">
          <div v-for="person in round.participants" :key="person.actorId" class="wf-progress-person">
            <strong>{{ participantDisplayName(round, person) }}</strong>
            <n-tag
              size="small"
              :type="participantAction(round, person) ? actionType(participantAction(round, person)?.action) : actorStatusType(person.status, false)"
              :bordered="false"
            >
              {{ participantAction(round, person) ? actionLabel(participantAction(round, person)?.action) : actorStatusLabel(person.status, false) }}
            </n-tag>
          </div>
        </div>
        <div class="wf-progress-actions">
          <div v-for="item in round.history" :key="item.id" class="wf-progress-action">
            <div class="wf-progress-action-head">
              <strong>{{ historyOperator(item) }}</strong>
              <n-tag size="small" :bordered="false">{{ actionLabel(item.action) }}</n-tag>
              <time>{{ formatTime(item.createTime) }}</time>
            </div>
            <p v-if="item.comment">{{ item.comment }}</p>
            <p v-if="item.originalUserId != null">
              {{ t('workflow.detail.approvalProgress.originalActor', { name: personName(item.originalUserId, item.originalUserName) }) }}
            </p>
            <p v-if="transferTarget(item)">{{ t('workflow.detail.transferTo', { name: transferTarget(item) }) }}</p>
            <p v-if="signTarget(item)">{{ t('workflow.detail.signTarget', { name: signTarget(item) }) }}</p>
          </div>
        </div>
        <details class="wf-progress-technical">
          <summary>{{ t('workflow.detail.approvalProgress.technical') }}</summary>
          <code>Task #{{ round.taskId ?? '—' }} · Token #{{ round.tokenId ?? '—' }} · NodeVisit #{{ round.nodeVisitId ?? '—' }}</code>
        </details>
      </details>
    </template>
  </div>
</template>

<style scoped>
.wf-approval-progress { display: grid; gap: var(--space-12); }
.wf-progress-round {
  padding: var(--space-12);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg-container);
}
.wf-progress-round.is-current { border-color: color-mix(in srgb, var(--color-primary) 40%, var(--color-border)); }
.wf-progress-round-head,
.wf-progress-person,
.wf-progress-person-status,
.wf-progress-action-head {
  display: flex;
  align-items: center;
  gap: var(--space-8);
  flex-wrap: wrap;
}
.wf-progress-round-head { cursor: pointer; color: var(--color-text-primary); }
.wf-progress-ratio { color: var(--color-text-secondary); font-size: var(--font-size-xs); }
.wf-progress-round-head > span,
.wf-progress-person-status time,
.wf-progress-action-head time { color: var(--color-text-tertiary); font-size: var(--font-size-xs); }
.wf-progress-people,
.wf-progress-actions { display: grid; gap: var(--space-8); margin-top: var(--space-12); }
.wf-progress-person { justify-content: space-between; padding: var(--space-8); background: var(--color-fill); border-radius: var(--radius-sm); }
.wf-progress-original { display: block; margin-top: 2px; color: var(--color-text-tertiary); font-size: var(--font-size-xs); }
.wf-progress-action { padding: var(--space-8) 0; border-top: 1px solid var(--color-border); }
.wf-progress-action p { margin: var(--space-4) 0 0; color: var(--color-text-secondary); font-size: var(--font-size-sm); }
.wf-progress-technical { margin-top: var(--space-12); color: var(--color-text-tertiary); font-size: var(--font-size-xs); }
.wf-progress-technical code { display: block; margin-top: var(--space-4); overflow-wrap: anywhere; }
</style>
