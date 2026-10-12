<script setup lang="ts">
// 弹窗/抽屉二合一表单容器:消化 CRUD 页「saving ref + 手写 footer + n-modal 包装」样板。
// 形态默认跟随全局偏好(app.formStyle,设置抽屉可切),variant 按实例覆盖。
// onConfirm 协议:返回 Promise → 确认钮自动 loading;reject 或 resolve(false) → 不关闭
// (n-form 校验放 onConfirm 首行,失败 reject 即挡住关闭);其余情况自动关。
import { computed, onDeactivated, ref, useId, watch } from 'vue'
import { NModal, NDrawer, NDrawerContent, NButton, NSpace } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { useWindowSize } from '@vueuse/core'
import { useAppStore, type FormStyle } from '@/stores/app'

const show = defineModel<boolean>('show', { default: false })

const props = defineProps<{
  title: string
  /** 不传 → 跟随 app.formStyle 全局偏好 */
  variant?: FormStyle
  /** modal=卡片宽 / drawer=抽屉宽,实际生效 min(width, 90vw) */
  width?: number
  /** 见顶部协议注释;不传则确认钮直接关闭 */
  onConfirm?: () => unknown | Promise<unknown>
  confirmText?: string
  cancelText?: string
  /** 默认 false:表单防误触丢输入 */
  maskClosable?: boolean
  /** Naive UI 官方过渡生命周期扩展点。 */
  onAfterEnter?: () => void
  onAfterLeave?: () => void
}>()

const { t } = useI18n()
const app = useAppStore()
const motionPanelId = useId()

const mode = computed(() => props.variant ?? app.formStyle)
const loading = ref(false)
// 窄屏不溢出:同 SettingsDrawer 惯例,宽度取 min(width, 90vw)。
const { width: winW } = useWindowSize()
const w = computed(() => Math.min(props.width ?? 560, Math.round(winW.value * 0.9)))
// 提交中禁止任何途径关闭(esc/遮罩/关闭钮/取消钮),防提交中途关闭导致状态错乱。
const canClose = computed(() => !loading.value)
const maskClose = computed(() => (props.maskClosable ?? false) && !loading.value)

// CSS 变量会改变后续过渡时长，但浏览器不会重算已经启动的 CSS Transition。
// 策略中途变化时只完成本实例公开面板节点自身的过渡，让 Naive UI 正常收到结束事件并收尾遮罩/滚动锁。
function finishRunningPanelMotion() {
  const panel = Array.from(document.querySelectorAll<HTMLElement>('.motion-form-panel'))
    .find((element) => element.dataset.formMotionId === motionPanelId)
  for (const animation of panel?.getAnimations() ?? []) {
    if (animation.playState === 'running' || animation.pending) animation.finish()
  }
}

watch(() => app.effectiveMotionMode, finishRunningPanelMotion)

async function handleConfirm() {
  if (!props.onConfirm) {
    show.value = false
    return
  }
  loading.value = true
  try {
    if ((await props.onConfirm()) !== false) show.value = false
  } catch {
    // 静默:n-form 校验失败已有内联提示;API 错误 toast 由业务 onConfirm 内负责。
  } finally {
    loading.value = false
  }
}

// keep-alive 页签切走时,teleport 到 body 的弹层会残留可见 → 强制收起。
onDeactivated(() => {
  show.value = false
})
</script>

<template>
  <n-modal
    v-if="mode === 'modal'"
    v-model:show="show"
    preset="card"
    :title="title"
    :closable="canClose"
    :close-on-esc="canClose"
    :mask-closable="maskClose"
    class="motion-form-panel"
    :data-form-motion-id="motionPanelId"
    transform-origin="center"
    :style="{ width: `${w}px` }"
    @after-enter="props.onAfterEnter"
    @after-leave="props.onAfterLeave"
  >
    <template v-if="$slots['header-extra']" #header-extra>
      <slot name="header-extra" />
    </template>
    <slot />
    <template #footer>
      <slot name="footer">
        <n-space justify="end">
          <n-button :disabled="loading" @click="show = false">{{ cancelText ?? t('common.cancel') }}</n-button>
          <n-button type="primary" :loading="loading" @click="handleConfirm">
            {{ confirmText ?? t('common.confirm') }}
          </n-button>
        </n-space>
      </slot>
    </template>
  </n-modal>

  <n-drawer
    v-else
    v-model:show="show"
    :width="w"
    :close-on-esc="canClose"
    :mask-closable="maskClose"
    class="motion-form-panel"
    :data-form-motion-id="motionPanelId"
    @after-enter="props.onAfterEnter"
    @after-leave="props.onAfterLeave"
  >
    <n-drawer-content :title="title" :closable="canClose" :native-scrollbar="false">
      <!-- n-drawer-content 无 header-extra 原生槽:业务传了才自拼 #header(标题 + 附加区并排) -->
      <template v-if="$slots['header-extra']" #header>
        <span>{{ title }}</span>
        <slot name="header-extra" />
      </template>
      <slot />
      <template #footer>
        <slot name="footer">
          <n-space justify="end">
            <n-button :disabled="loading" @click="show = false">{{ cancelText ?? t('common.cancel') }}</n-button>
            <n-button type="primary" :loading="loading" @click="handleConfirm">
              {{ confirmText ?? t('common.confirm') }}
            </n-button>
          </n-space>
        </slot>
      </template>
    </n-drawer-content>
  </n-drawer>
</template>
