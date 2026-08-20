<script setup lang="ts">
/**
 * 流程设计器 MVP(M1):串行审批+抄送 + 配置抽屉 + 灰底可缩放画布。
 * 菜单 component 填 `workflow/definition/designer`;通过 `?id=` 打开已有定义,
 * 无 id 时可新建草稿后进入编辑。
 */
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NInput, NSpace, NSpin, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import DetailPage from '@/components/DetailPage/index.vue'
import AppIcon from '@/components/AppIcon.vue'
import { useTabTitle } from '@/composables/useTabTitle'
import { useConfirm } from '@/composables/useConfirm'
import { wfDefinitionApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import type { WfDefinitionInput } from '@/types/workflow'
import { cloneModel, createDefaultModel, validateModel } from '@/workflow/model'
import type { WfModel, WfNode } from '@/workflow/schema'
import WfNodeTree from './components/WfNodeTree.vue'
import WfConfigDrawer from './components/WfConfigDrawer.vue'

const ZOOM_MIN = 50
const ZOOM_MAX = 200
const ZOOM_STEP = 10

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const message = useMessage()
const setTabTitle = useTabTitle()
const { run } = useConfirm()

const loading = ref(false)
const saving = ref(false)
const defId = ref<number | null>(null)
const name = ref('')
const model = ref<WfModel>(createDefaultModel())
const selectedId = ref<string | null>(null)
const drawerShow = ref(false)
const creating = ref(false)
const newName = ref('')

const zoom = ref(100)
const canvasRef = ref<HTMLElement | null>(null)
const stageRef = ref<HTMLElement | null>(null)

const errorIds = computed(() => {
  const issues = validateModel(model.value)
  return new Set(issues.map((i) => i.nodeId).filter(Boolean) as string[])
})

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

function readId(): number | null {
  const raw = (route.query.id as string) || (route.params.id as string)
  if (!raw) return null
  const n = Number(raw)
  return Number.isFinite(n) && n > 0 ? n : null
}

async function load(id: number) {
  loading.value = true
  try {
    const detail = await wfDefinitionApi.get(id)
    defId.value = Number(detail.id)
    name.value = detail.name ?? ''
    model.value = cloneModel((detail.model as unknown as WfModel | undefined) ?? createDefaultModel())
    setTabTitle(detail.name ?? '')
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  const id = readId()
  if (id) void load(id)
})

watch(
  () => route.query.id,
  (v) => {
    const id = v ? Number(v) : null
    if (id && id > 0 && id !== defId.value) void load(id)
  },
)

function onSelect(node: WfNode) {
  selectedId.value = node.id
  drawerShow.value = true
}

async function save(): Promise<boolean> {
  if (!defId.value) return false
  const issues = validateModel(model.value)
  if (issues.length) {
    message.warning(t('workflow.designer.invalid'))
    return false
  }
  saving.value = true
  try {
    await wfDefinitionApi.update({
      id: defId.value,
      name: name.value.trim() || t('workflow.designer.untitled'),
      model: model.value as unknown as WfDefinitionInput['model'],
    })
    message.success(t('common.success'))
    setTabTitle(name.value)
    return true
  } catch (e) {
    message.error(translateError(e))
    return false
  } finally {
    saving.value = false
  }
}

async function publish() {
  if (!defId.value) return
  if (!await save()) return
  const ok = await run(() => wfDefinitionApi.publish(defId.value!), t('workflow.designer.published'))
  if (ok) await load(defId.value)
}

async function createDraft() {
  const n = newName.value.trim()
  if (!n) {
    message.warning(t('workflow.designer.nameRequired'))
    return
  }
  creating.value = true
  try {
    const id = Number(await wfDefinitionApi.add({
      name: n,
      model: createDefaultModel() as unknown as WfDefinitionInput['model'],
    }))
    await router.replace({ query: { ...route.query, id: String(id) } })
    await load(id)
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
          <n-button :loading="saving" @click="save">
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
