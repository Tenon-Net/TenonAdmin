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

  it('onOk 执行期间弹窗不关、action 只跑一次(antd 内置 busy 守卫,不必手写)', async () => {
    // 这条钉住「比 Vue 短」的依据:Naive 要手写 busy 防连点/执行中关窗,antd 的 onOk 返 Promise 时
    // 自动锁住关闭并 loading OK 钮。造一个挂起的 action,点确定后弹窗仍在、再点确定不会二次触发。
    let release!: () => void
    const gate = new Promise<void>((r) => (release = r))
    const action = vi.fn().mockImplementation(() => gate)
    mount('confirm', { content: '删?', action })
    fireEvent.click(screen.getByText('go'))
    await waitFor(() => expect(okBtn()).toBeTruthy())
    fireEvent.click(okBtn())
    await waitFor(() => expect(action).toHaveBeenCalledOnce())

    // 执行挂起中:弹窗还在(没被关掉),再点一次确定也不二次触发。
    expect(document.body.textContent).toContain('删?')
    fireEvent.click(okBtn())
    expect(action).toHaveBeenCalledOnce() // 仍是 1 次

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
