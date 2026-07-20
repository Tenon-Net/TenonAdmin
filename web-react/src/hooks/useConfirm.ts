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
 * **busy 守卫**:antd 的 `modal.confirm({onOk})` onOk 返 Promise 时**只自动锁 OK 钮(loading)+ mask** ——
 * **取消钮与 Esc 它不锁**(实测,B9 review 抓到的 HIGH)。不补的话:请求在途时用户点取消/按 Esc →
 * onCancel → `resolve(false)` → 调用方跳过成功分支(表格不刷新),而 action 后台照样成功 → 迟到的成功 toast,
 * 正是 Vue 版 busy 守卫点名要防的"已删成功但表格没刷新"。
 *
 * 补法:onOk 一启动就 `instance.update` **禁用取消钮 + 关 keyboard(Esc)** —— 从根上让 onCancel 不再触发。
 * 这一道就够(取消/Esc 都汇入 onCancel,禁掉按钮 + 键盘即两条全断),**不另加 `started` 逻辑门**:
 * 那会与 update 冗余、单点变异测不出(B5a 的冗余闸教训),且 `started` 返回 undefined 还会让 antd
 * 在取消时**关掉弹窗**而 action 仍在途。单一机制:`instance.update`,可被变异钉住(去掉即取消/Esc 漏)。
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

  /** 确认 → 执行(onOk 期间 OK 钮 loading、取消/Esc 锁死)。取消/失败均 resolve(false)。 */
  function confirm<T>(opts: ConfirmOptions<T>): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      const instance = modal.confirm({
        title: opts.title ?? t('common.confirmTitle'),
        content: opts.content,
        okText: t('common.confirm'),
        cancelText: t('common.cancel'),
        onOk: () => {
          // antd 不锁取消/Esc,手动锁:禁用取消钮 + 关键盘(Esc),从根上让 onCancel 不再触发。
          // onOk 返回 Promise → OK 钮自动 loading;run 吞异常 → onOk 必 resolve → 弹窗必关(成/败都关)。
          instance.update({ cancelButtonProps: { disabled: true }, keyboard: false })
          return run(opts.action, opts.successMsg).then(resolve)
        },
        onCancel: () => resolve(false), // 只会在 onOk 之前触发(之后取消钮/Esc 已被 update 禁掉)。
      })
    })
  }

  return { ask, confirm, run }
}
