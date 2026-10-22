<script setup lang="ts">
/**
 * 流程定义设计器:配置审批、抄送、分支、并行和自动节点。
 * 菜单 component 填 `workflow/definition/designer`;通过 `?id=` 打开已有定义,
 * 无 id 时可新建草稿后进入编辑。
 */
import { computed, onBeforeUnmount, ref, shallowRef, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NInput, NSpace, NSpin, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import DetailPage from '@/components/DetailPage/index.vue'
import AppIcon from '@/components/AppIcon.vue'
import { useTabTitle } from '@/composables/useTabTitle'
import { ApiError } from '@/api'
import { wfDefinitionApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import { withDesignerNodeName } from '@/workflow/designerError'
import { normalizeWfId, wfIdEquals, workflowPathId, type WfId } from '@/workflow/id'
import { rememberParticipantNames } from '@/workflow/participantNames'
import type { WfDefinitionInput } from '@/types/workflow'
import { cloneModel, createDefaultModel, findNode, validateModel, type WfModelIssue } from '@/workflow/model'
import type { WfModel, WfNode } from '@/workflow/schema'
import WfNodeTree from './components/WfNodeTree.vue'
import WfConfigDrawer from './components/WfConfigDrawer.vue'

const ZOOM_MIN = 50
const ZOOM_MAX = 200
const ZOOM_STEP = 10

const { t, te } = useI18n()
const route = useRoute()
const router = useRouter()
const message = useMessage()
const setTabTitle = useTabTitle()

const loading = shallowRef(false)
const saving = shallowRef(false)
const defId = shallowRef<WfId | null>(null)
const name = shallowRef('')
// 设计器不编辑分组和图标,保存时必须原样带回,否则更新接口会把它们写成空。
const groupName = shallowRef<string | null>(null)
const icon = shallowRef<string | null>(null)
const model = ref<WfModel>(createDefaultModel())
const selectedId = shallowRef<string | null>(null)
const drawerShow = shallowRef(false)
const creating = shallowRef(false)
const newName = shallowRef('')

const zoom = shallowRef(100)
const canvasRef = useTemplateRef<HTMLElement>('canvasRef')
const stageRef = useTemplateRef<HTMLElement>('stageRef')

const serverNodeErrors = ref<Record<string, string>>({})

function issueText(issue: WfModelIssue): string {
  const key = `workflow.designer.issue.${issue.code}`
  const message = te(key) ? t(key) : t('workflow.designer.invalid')
  if (!issue.nodeId) return message
  const nodeName = findNode(model.value.root, issue.nodeId)?.name?.trim() || issue.nodeId
  return t('workflow.designer.issue.onNode', { nodeId: nodeName, message })
}

const errorMessages = computed(() => {
  const map: Record<string, string> = {}
  for (const issue of validateModel(model.value)) {
    if (!issue.nodeId || map[issue.nodeId]) continue
    const key = `workflow.designer.issue.${issue.code}`
    map[issue.nodeId] = te(key) ? t(key) : t('workflow.designer.invalid')
  }
  return { ...map, ...serverNodeErrors.value }
})

const errorIds = computed(() => new Set(Object.keys(errorMessages.value)))

watch(model, () => { serverNodeErrors.value = {} }, { deep: true })

const zoomLabel = computed(() => `${zoom.value}%`)

function clampZoom(n: number) {
  const snapped = Math.round(n / ZOOM_STEP) * ZOOM_STEP
  return Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, snapped))
}

function zoomBy(delta: number) {
  zoom.value = clampZoom(zoom.value + delta)
}

function fitZoom() {
  const canvas = canvasRef.value
  const stage = stageRef.value
  if (!canvas || !stage) return
  const pad = 72
  const availW = Math.max(1, canvas.clientWidth - pad)
  const availH = Math.max(1, canvas.clientHeight - pad)
  const scaleNow = zoom.value / 100
  const w = Math.max(1, stage.offsetWidth / scaleNow)
  const h = Math.max(1, stage.offsetHeight / scaleNow)
  const s = Math.min(1, availW / w, availH / h)
  zoom.value = clampZoom(s * 100)
}

function readId(): WfId | null {
  return normalizeWfId(route.query.id ?? route.params.id)
}

let loadRequest = 0

function isViewing(id: WfId): boolean {
  return wfIdEquals(id, readId()) && wfIdEquals(id, defId.value)
}

async function load(id: WfId) {
  const request = ++loadRequest
  loading.value = true
  try {
    const detail = await wfDefinitionApi.get(id)
    if (request !== loadRequest || !wfIdEquals(id, readId())) return
    defId.value = normalizeWfId(detail.id) ?? id
    name.value = detail.name ?? ''
    groupName.value = detail.groupName ?? null
    icon.value = detail.icon ?? null
    rememberParticipantNames(detail.participantNames)
    model.value = cloneModel((detail.model as unknown as WfModel | undefined) ?? createDefaultModel())
    setTabTitle(detail.name ?? '')
  } catch (e) {
    if (request !== loadRequest || !wfIdEquals(id, readId())) return
    message.error(translateError(e))
  } finally {
    if (request === loadRequest) loading.value = false
  }
}

onBeforeUnmount(() => { ++loadRequest })

watch(
  () => readId(),
  (id) => {
    if (id == null) {
      ++loadRequest
      loading.value = false
      defId.value = null
      selectedId.value = null
      drawerShow.value = false
      return
    }
    if (!wfIdEquals(id, defId.value)) {
      defId.value = null
      selectedId.value = null
      drawerShow.value = false
      void load(id)
    }
  },
  { immediate: true },
)

function onSelect(node: WfNode) {
  selectedId.value = node.id
  drawerShow.value = true
}

async function save(options?: { quiet?: boolean }): Promise<boolean> {
  const targetId = defId.value
  if (!targetId) return false
  const issues = validateModel(model.value)
  if (issues.length) {
    message.warning(issues.map(issueText).join('；'))
    return false
  }
  const targetName = name.value
  const targetModel = model.value
  saving.value = true
  try {
    await wfDefinitionApi.update({
      id: workflowPathId(targetId),
      name: targetName.trim() || t('workflow.designer.untitled'),
      groupName: groupName.value,
      icon: icon.value,
      model: targetModel as unknown as WfDefinitionInput['model'],
    })
    if (!options?.quiet) message.success(t('common.success'))
    if (isViewing(targetId)) setTabTitle(targetName)
    return true
  } catch (e) {
    message.error(translateError(withDesignerNodeName(e, targetModel.root)))
    return false
  } finally {
    saving.value = false
  }
}

async function publish() {
  const targetId = defId.value
  if (!targetId) return
  const targetRoot = model.value.root
  serverNodeErrors.value = {}
  // 发布成功自己有提示,这里不再叠一条「操作成功」。
  if (!await save({ quiet: true })) return
  if (!isViewing(targetId)) return
  try {
    await wfDefinitionApi.publish(targetId)
    if (!isViewing(targetId)) return
    message.success(t('workflow.designer.published'))
    await load(targetId)
  } catch (e) {
    if (!isViewing(targetId)) return
    const named = withDesignerNodeName(e, targetRoot)
    const nodeId = e instanceof ApiError ? String(e.args?.nodeId ?? '') : ''
    if (nodeId) serverNodeErrors.value = { [nodeId]: translateError(named) }
    // 内容没变不会生成新版本,这是提示而不是失败。
    const notice = e instanceof ApiError && e.code === 48052 ? message.warning : message.error
    notice(translateError(named))
  }
}

async function createDraft() {
  const n = newName.value.trim()
  if (!n) {
    message.warning(t('workflow.designer.nameRequired'))
    return
  }
  creating.value = true
  try {
    const id = normalizeWfId(await wfDefinitionApi.add({
      name: n,
      model: createDefaultModel() as unknown as WfDefinitionInput['model'],
    }))
    if (id == null) throw new Error('Invalid workflow definition id')
    await router.replace({ query: { ...route.query, id: String(id) } })
  } catch (e) {
    message.error(translateError(e))
  } finally {
    creating.value = false
  }
}

function onBack() {
  router.back()
}
</script>

<template>
  <DetailPage
    class="wf-page"
    :class="{ 'is-editing': !!defId }"
    :title="defId ? (name || t('workflow.designer.title')) : t('workflow.designer.title')"
    :loading="loading"
    @back="onBack"
  >
    <template v-if="defId" #actions>
      <div class="wf-toolbar">
        <n-input
          v-model:value="name"
          class="wf-name"
          :bordered="false"
          :placeholder="t('workflow.designer.name')"
        />
        <n-space :size="8">
          <n-button :loading="saving" @click="save()">
            <template #icon><AppIcon icon="ph:floppy-disk" :size="16" /></template>
            {{ t('common.save') }}
          </n-button>
          <n-button type="primary" :loading="saving" @click="publish">
            <template #icon><AppIcon icon="ph:paper-plane-tilt" :size="16" /></template>
            {{ t('workflow.designer.publish') }}
          </n-button>
        </n-space>
      </div>
    </template>

    <div v-if="!defId && !loading" class="wf-empty">
      <div class="wf-empty-icon"><AppIcon icon="ph:tree-structure" :size="36" /></div>
      <p class="wf-empty-title">{{ t('workflow.designer.needId') }}</p>
      <n-space>
        <n-input
          v-model:value="newName"
          :placeholder="t('workflow.designer.name')"
          style="width: 260px"
          @keyup.enter="createDraft"
        />
        <n-button type="primary" :loading="creating" @click="createDraft">
          {{ t('workflow.designer.create') }}
        </n-button>
      </n-space>
    </div>

    <n-spin v-else :show="loading" class="wf-spin">
      <div class="wf-canvas-wrap">
        <div ref="canvasRef" class="wf-canvas">
          <div class="wf-stage">
            <div ref="stageRef" class="wf-stage-inner" :style="{ zoom: zoom / 100 }">
              <WfNodeTree
                :model="model"
                :selected-id="selectedId"
                :error-ids="errorIds"
                :error-messages="errorMessages"
                @update:model="model = $event"
                @select="onSelect"
              />
            </div>
          </div>
        </div>

        <div class="wf-zoom" role="toolbar" :aria-label="t('workflow.designer.zoom')">
          <button type="button" class="wf-zoom-btn" :disabled="zoom <= ZOOM_MIN" :aria-label="t('workflow.designer.zoomOut')" @click="zoomBy(-ZOOM_STEP)">
            <AppIcon icon="ph:minus" :size="14" />
          </button>
          <span class="wf-zoom-pct">{{ zoomLabel }}</span>
          <button type="button" class="wf-zoom-btn" :disabled="zoom >= ZOOM_MAX" :aria-label="t('workflow.designer.zoomIn')" @click="zoomBy(ZOOM_STEP)">
            <AppIcon icon="ph:plus" :size="14" />
          </button>
          <button type="button" class="wf-zoom-btn wf-zoom-fit" :aria-label="t('workflow.designer.zoomFit')" @click="fitZoom">
            <AppIcon icon="ph:corners-out" :size="14" />
          </button>
        </div>
      </div>
    </n-spin>

    <WfConfigDrawer
      v-model:show="drawerShow"
      :model="model"
      :node-id="selectedId"
      @update:model="model = $event"
    />
  </DetailPage>
</template>

<style scoped>
.wf-page.is-editing :deep(.detail-title) {
  display: none;
}
.wf-page :deep(.detail-actions) {
  margin-left: 0;
  flex: 1;
  min-width: 0;
}
.wf-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  width: 100%;
  min-width: 0;
}
.wf-name {
  width: 280px;
  font-size: var(--font-size-md);
  font-weight: 600;
}
.wf-name :deep(.n-input__input-el) {
  font-weight: 600;
}

.wf-spin {
  display: block;
}
.wf-spin :deep(.n-spin-content) {
  min-height: calc(100vh - 168px);
}

.wf-canvas-wrap {
  position: relative;
  min-height: calc(100vh - 168px);
}
.wf-canvas {
  min-height: calc(100vh - 168px);
  height: calc(100vh - 168px);
  background: var(--color-bg-body);
  border-radius: var(--radius-lg);
  overflow: auto;
}

.wf-stage {
  display: flex;
  justify-content: center;
  width: max-content;
  min-width: 100%;
}
.wf-stage-inner {
  display: inline-block;
}

.wf-zoom {
  position: absolute;
  bottom: 16px;
  left: 16px;
  z-index: 2;
  display: inline-flex;
  align-items: center;
  gap: 2px;
  padding: 4px;
  border-radius: 999px;
  background: var(--color-bg-elevated);
  border: 1px solid var(--color-border);
  box-shadow: var(--shadow-2);
}
.wf-zoom-btn {
  width: 28px;
  height: 28px;
  border: none;
  border-radius: 50%;
  background: transparent;
  color: var(--color-text-secondary);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}
.wf-zoom-btn:hover:not(:disabled) {
  background: var(--color-fill-hover);
  color: var(--color-text-primary);
}
.wf-zoom-btn:disabled {
  opacity: 0.4;
  cursor: default;
}
.wf-zoom-pct {
  min-width: 48px;
  text-align: center;
  font-size: var(--font-size-xs);
  font-variant-numeric: tabular-nums;
  color: var(--color-text-secondary);
}
.wf-zoom-fit {
  margin-left: 2px;
}

.wf-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 80px 16px;
  color: var(--color-text-secondary);
}
.wf-empty-icon {
  width: 64px;
  height: 64px;
  border-radius: 16px;
  background: var(--color-fill);
  color: var(--color-text-tertiary);
  display: flex;
  align-items: center;
  justify-content: center;
}
.wf-empty-title {
  margin: 0 0 8px;
  max-width: 420px;
  text-align: center;
  line-height: 1.6;
}
</style>
