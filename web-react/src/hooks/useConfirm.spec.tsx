import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, waitFor, fireEvent } from '@testing-library/react'
import { App as AntdApp, Button } from 'antd'
import '@/locales' // t() 要真文案
import { useConfirm, type ConfirmOptions } from './useConfirm'
import { ApiError } from '@/api'

/**
 * 确认框走 `App.useApp().modal`,要在 `<AntdApp>` 里渲染;弹窗挂在 document.body。
 * 用一个小组件把三 API 暴露成按钮,点按钮触发,再在弹窗里点确定/取消。
 */
let lastResult: boolean | undefined
function Harness({ mode, opts }: { mode: 'ask' | 'confirm'; opts: ConfirmOptions<unknown> }) {
  const { ask, confirm } = useConfirm()
  return (
    <Button
      onClick={async () => {
        lastResult = mode === 'ask' ? await ask({ content: opts.content }) : await confirm(opts)
      }}
    >
      go
    </Button>
  )
}

function mount(mode: 'ask' | 'confirm', opts: ConfirmOptions<unknown>) {
  render(
    <AntdApp>
      <Harness mode={mode} opts={opts} />
    </AntdApp>,
  )
}

// 弹窗按钮:antd confirm 的确定/取消。文案是 common.confirm/cancel。
// antd 对两个中文字的按钮自动插空格(autoInsertSpace),可及名是「确 定」不是「确定」,用容忍空格的正则。
const okBtn = () => screen.getByRole('button', { name: /确\s*定/ })
const cancelBtn = () => screen.getByRole('button', { name: /取\s*消/ })

beforeEach(() => {
  lastResult = undefined
})
afterEach(cleanup)

describe('ask —— 只确认不执行', () => {
  it('点确定 → resolve(true)', async () => {
    const action = vi.fn()
    mount('ask', { content: '确定吗', action })
    fireEvent.click(screen.getByText('go'))
    await waitFor(() => expect(okBtn()).toBeTruthy())
    fireEvent.click(okBtn())
    await waitFor(() => expect(lastResult).toBe(true))
    expect(action).not.toHaveBeenCalled() // ask 不执行动作
  })

  it('点取消 → resolve(false)', async () => {
    mount('ask', { content: '确定吗', action: vi.fn() })
    fireEvent.click(screen.getByText('go'))
    await waitFor(() => expect(cancelBtn()).toBeTruthy())
    fireEvent.click(cancelBtn())
    await waitFor(() => expect(lastResult).toBe(false))
  })
})

describe('confirm —— 确认后执行 + toast', () => {
  it('确认 → 执行 action → 成功 toast → resolve(true)', async () => {
    const action = vi.fn().mockResolvedValue(undefined)
    mount('confirm', { content: '删除?', action })
    fireEvent.click(screen.getByText('go'))
    await waitFor(() => expect(okBtn()).toBeTruthy())
    fireEvent.click(okBtn())
    await waitFor(() => expect(action).toHaveBeenCalledOnce())
    await waitFor(() => expect(lastResult).toBe(true))
    expect(await screen.findByText('操作成功')).toBeTruthy() // message.success 默认文案
  })

  it('action 失败 → 错误 toast(经 translateError)→ resolve(false)', async () => {
    const action = vi.fn().mockRejectedValue(new ApiError(40004, 'error.auth.passwordWrong'))
    mount('confirm', { content: '删除?', action })
    fireEvent.click(screen.getByText('go'))
    await waitFor(() => expect(okBtn()).toBeTruthy())
    fireEvent.click(okBtn())
    await waitFor(() => expect(lastResult).toBe(false))
    expect(await screen.findByText('账号或密码错误')).toBeTruthy() // 本地化,不是 msgKey 原文
  })

  it('取消 → 不执行 action → resolve(false)', async () => {
    const action = vi.fn().mockResolvedValue(undefined)
    mount('confirm', { content: '删除?', action })
    fireEvent.click(screen.getByText('go'))
    await waitFor(() => expect(cancelBtn()).toBeTruthy())
    fireEvent.click(cancelBtn())
    await waitFor(() => expect(lastResult).toBe(false))
    expect(action).not.toHaveBeenCalled()
  })

  it('onOk 执行期间:OK 钮 loading、再点不二发、且弹窗不关', async () => {
    let release!: () => void
    const gate = new Promise<void>((r) => (release = r))
    const action = vi.fn().mockImplementation(() => gate)
    mount('confirm', { content: '删?', action })
    fireEvent.click(screen.getByText('go'))
    await waitFor(() => expect(okBtn()).toBeTruthy())
    fireEvent.click(okBtn())
    await waitFor(() => expect(action).toHaveBeenCalledOnce())

    // 唯一能判别「锁住未关」vs「正在关(离场动画)」的观测量是 OK 钮的 loading 类 —— 必须断它,
    // 否则 `void run().then(resolve)`(去掉返回 Promise、antd 不再锁关)也照样绿(review 的 MEDIUM)。
    await waitFor(() => expect(okBtn().className).toContain('loading'))
    fireEvent.click(okBtn())
    expect(action).toHaveBeenCalledOnce() // 再点不二发

    release()
    await waitFor(() => expect(lastResult).toBe(true))
  })

  it('执行期间点「取消」不提前 resolve —— 结果只由 action 结局决定(防"已删成功但表格没刷新")', async () => {
    // review 的 HIGH:antd 只锁 OK 钮 + mask,**不锁取消/Esc**。不补守卫的话,请求在途时点取消 →
    // onCancel → resolve(false) → 调用方跳过成功分支(不刷新),而 action 后台照样成功。
    let release!: () => void
    const gate = new Promise<void>((r) => (release = r))
    const action = vi.fn().mockImplementation(() => gate)
    mount('confirm', { content: '删?', action })
    fireEvent.click(screen.getByText('go'))
    await waitFor(() => expect(okBtn()).toBeTruthy())
    fireEvent.click(okBtn())
    await waitFor(() => expect(action).toHaveBeenCalledOnce())

    // 挂起中点取消:不该抢先 settle。取消钮此时应被禁用(disabled),点了也没反应。
    fireEvent.click(cancelBtn())
    await new Promise((r) => setTimeout(r, 20))
    expect(lastResult).toBeUndefined() // 还没 settle —— 没被取消抢先

    release()
    await waitFor(() => expect(lastResult).toBe(true)) // 最终由 action 成功决定
  })

  it('执行期间按 Esc 不提前 resolve(Esc 走 onCancel,同样要被守卫挡住)', async () => {
    let release!: () => void
    const gate = new Promise<void>((r) => (release = r))
    const action = vi.fn().mockImplementation(() => gate)
    mount('confirm', { content: '删?', action })
    fireEvent.click(screen.getByText('go'))
    await waitFor(() => expect(okBtn()).toBeTruthy())
    fireEvent.click(okBtn())
    await waitFor(() => expect(action).toHaveBeenCalledOnce())

    fireEvent.keyDown(document.body, { key: 'Escape' })
    await new Promise((r) => setTimeout(r, 20))
    expect(lastResult).toBeUndefined() // Esc 没抢先 settle

    release()
    await waitFor(() => expect(lastResult).toBe(true))
  })

  it('successMsg=false → 成功不弹 toast(仍 resolve true)', async () => {
    const action = vi.fn().mockResolvedValue(undefined)
    mount('confirm', { content: 'x', action, successMsg: false })
    fireEvent.click(screen.getByText('go'))
    await waitFor(() => expect(okBtn()).toBeTruthy())
    fireEvent.click(okBtn())
    await waitFor(() => expect(lastResult).toBe(true))
    await new Promise((r) => setTimeout(r, 30))
    expect(screen.queryByText('操作成功')).toBeNull() // 没弹成功 toast
  })
})
