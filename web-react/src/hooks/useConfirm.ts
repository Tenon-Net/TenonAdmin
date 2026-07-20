import { App } from 'antd'
import { useTranslation } from 'react-i18next'
import { translateError } from '@/utils/error'

export interface ConfirmOptions<T> {
  content: string
  /** 默认 t('common.confirmTitle') */
  title?: string
  action: () => Promise<T>
  /** false = 成功不弹 toast(后续另有提示时) */
  successMsg?: string | false
}

/**
 * 二次确认 + 结果 toast 的样板收敛,对应 Vue 侧 `composables/useConfirm.ts` 的三 API:
 *   ask     —— 只弹确认框、不执行,resolve(用户是否确认);给需自管后续的组件(如 StatusSwitch)。
 *   confirm —— 确认后执行 action,成/败统一 toast,resolve(执行是否成功);给不适合内联的重操作。
 *   run     —— 不弹框的后半段「执行 → toast」,配触发器旁的内联确认用。
 *
 * **比 Vue 版短**:Naive 的 dialog 不自动锁按钮,Vue 手写了 busy 守卫防连点/执行中取消(那 30 行)。
 * antd 的 `modal.confirm({onOk})` **onOk 返 Promise 时自动:OK 钮 loading + 期间不可关窗**(mask/Esc/取消),
 * busy 守卫是内置的,所以这里不必手写(已用测试钉住这条语义)。
 *
 * 走 `App.useApp()` 的 `modal`/`message` 而非静态 `Modal`/`message`:静态版拿不到 ConfigProvider 的
 * 主题与 locale(v5 起告警)。holder 由 `App.tsx` 的 `<AntdApp>` 提供,这里不必自己挂 contextHolder。
 * **仅限组件内调用**(useApp 是 hook)。
 */
export function useConfirm() {
  const { modal, message } = App.useApp()
  const { t } = useTranslation()

  /** 执行 → 成/败 toast。resolve(true) 仅当执行成功(失败被吞掉,不外抛)。 */
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

  /** 仅确认不执行:取消/关闭/遮罩/Esc 均 resolve(false)。 */
  function ask(opts: { content: string; title?: string }): Promise<boolean> {
    // 走 onOk/onCancel 显式 resolve,不用 `modal.confirm` 的返回值 —— 那是个 `{destroy,update}&thenable`
    // 不是真 Promise(能 await 但 TS 不给当 Promise<boolean> 用,硬 cast 是抹类型)。onOk 不返 Promise → 立即关。
    return new Promise<boolean>((resolve) => {
      modal.confirm({
        title: opts.title ?? t('common.confirmTitle'),
        content: opts.content,
        okText: t('common.confirm'),
        cancelText: t('common.cancel'),
        onOk: () => resolve(true),
        onCancel: () => resolve(false),
      })
    })
  }

  /** 确认 → 执行(onOk 期间 OK 钮 loading、不可关窗)。取消/失败均 resolve(false)。 */
  function confirm<T>(opts: ConfirmOptions<T>): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      modal.confirm({
        title: opts.title ?? t('common.confirmTitle'),
        content: opts.content,
        okText: t('common.confirm'),
        cancelText: t('common.cancel'),
        // onOk 返回 Promise → antd 自动 loading + 挡关闭;run 内部吞异常,故 onOk 必 resolve → 弹窗必关闭。
        onOk: () => run(opts.action, opts.successMsg).then(resolve),
        onCancel: () => resolve(false),
      })
    })
  }

  return { ask, confirm, run }
}
