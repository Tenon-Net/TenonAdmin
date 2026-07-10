// 二次确认 + 结果 toast 的样板收敛(参考 SimpleAdmin useHandleData 的取舍):
//   ask —— 仅弹确认框不执行动作,resolve(用户是否确认);给需要自管后续流程的组件(如 StatusSwitch)。
//   confirm —— dialog 确认后执行 action,成/败统一 toast,resolve(执行是否成功);给不适合内联的重操作。
//   run —— 不弹 dialog 的后半段「执行 → toast」,配模板层 n-popconfirm 的 positive-click 用
//          (popconfirm 是触发器旁的内联组件,保留在模板层比 composable 化更顺手)。
// 仅限 setup 中调用:useDialog/useMessage 依赖 App.vue 的 Provider 注入。
import { useDialog, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { translateError } from '@/utils/error'

export interface ConfirmOptions<T> {
  content: string
  /** 默认 t('common.confirmTitle') */
  title?: string
  type?: 'warning' | 'error' | 'info'
  action: () => Promise<T>
  /** false = 成功不弹 toast(后续另有提示时) */
  successMsg?: string | false
}

export function useConfirm() {
  const dialog = useDialog()
  const message = useMessage()
  const { t } = useI18n()

  /** 仅确认不执行:取消/关闭/遮罩/Esc 均 resolve(false)。Esc 必须显式接 onEsc——Naive 的 Esc 路径不走 onClose。 */
  function ask(opts: { content: string; title?: string; type?: 'warning' | 'error' | 'info' }): Promise<boolean> {
    return new Promise((resolve) => {
      dialog.create({
        type: opts.type ?? 'warning',
        title: opts.title ?? t('common.confirmTitle'),
        content: opts.content,
        positiveText: t('common.confirm'),
        negativeText: t('common.cancel'),
        onPositiveClick: () => resolve(true),
        onNegativeClick: () => resolve(false),
        onClose: () => resolve(false),
        onMaskClick: () => resolve(false),
        onEsc: () => resolve(false),
      })
    })
  }

  /** 执行 → 成/败 toast。resolve(true) 仅当执行成功。 */
  async function run<T>(action: () => Promise<T>, successMsg?: string | false): Promise<boolean> {
    try {
      await action()
      if (successMsg !== false) message.success(successMsg ?? t('common.success'))
      return true
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  /** dialog 确认 → run(action 在 dialog 挂起期间执行)。取消/关闭/Esc/失败均 resolve(false)。 */
  function confirm<T>(opts: ConfirmOptions<T>): Promise<boolean> {
    return new Promise((resolve) => {
      // Naive 只把关闭延迟到 onPositiveClick 的 Promise settle,不会自动置 loading/锁按钮——
      // 手动锁死全部关闭路径,防连点重复执行、防执行中取消导致「已删成功但表格没刷新」。
      let busy = false
      const inst = dialog.create({
        type: opts.type ?? 'warning',
        title: opts.title ?? t('common.confirmTitle'),
        content: opts.content,
        positiveText: t('common.confirm'),
        negativeText: t('common.cancel'),
        onPositiveClick: () => {
          busy = true
          inst.loading = true // 确认钮 loading + 禁点
          inst.closeOnEsc = false
          inst.maskClosable = false
          return run(opts.action, opts.successMsg).then(resolve)
        },
        onNegativeClick: () => {
          if (busy) return false // 执行中禁取消
          resolve(false)
        },
        onClose: () => {
          if (busy) return false
          resolve(false)
        },
        onMaskClick: () => {
          if (busy) return false
          resolve(false)
        },
        onEsc: () => resolve(false), // busy 时已由 closeOnEsc=false 挡住,不会走到这里
      })
    })
  }

  return { ask, confirm, run }
}
