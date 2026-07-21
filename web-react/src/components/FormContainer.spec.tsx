import { describe, it, expect, afterEach, vi } from 'vitest'
import { render, screen, cleanup, waitFor, fireEvent } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales' // t() 要真文案
import { useAppStore, type FormStyle } from '@/stores/app'
import { FormContainer, shouldCloseAfterConfirm } from './FormContainer'

const okBtn = () => screen.getByRole('button', { name: /确\s*定/ })
const cancelBtn = () => screen.getByRole('button', { name: /取\s*消/ })

// 受控渲染:open 恒 true,onOpenChange 是 spy —— 断「是否关闭」看 spy 是否被 (false) 调用,
// 而不是断弹层 DOM 是否消失:happy-dom 不跑 CSS 过渡,antd 关闭动画的 transitionend 永不触发,
// 弹层子节点不会卸载(useConfirm.spec 也是断 promise 结果、从不断 DOM 消失)。
function renderFC(props: { variant?: FormStyle; onConfirm?: () => unknown | Promise<unknown> } = {}) {
  const onOpenChange = vi.fn()
  render(
    <AntdApp>
      <FormContainer open onOpenChange={onOpenChange} title="弹层标题" variant={props.variant} onConfirm={props.onConfirm}>
        <div>表单体</div>
      </FormContainer>
    </AntdApp>,
  )
  return { onOpenChange }
}

afterEach(() => {
  cleanup()
  useAppStore.setState({ formStyle: 'modal' }) // 复位全局形态,免污染下一条
})

describe('shouldCloseAfterConfirm(纯逻辑)', () => {
  it('无 onConfirm → 关(true)', async () => {
    expect(await shouldCloseAfterConfirm(undefined)).toBe(true)
  })
  it('resolve(false) → 不关', async () => {
    expect(await shouldCloseAfterConfirm(() => false)).toBe(false)
    expect(await shouldCloseAfterConfirm(() => Promise.resolve(false))).toBe(false)
  })
  it('resolve(true)/undefined/0 等非 false → 关', async () => {
    expect(await shouldCloseAfterConfirm(() => true)).toBe(true)
    expect(await shouldCloseAfterConfirm(() => undefined)).toBe(true)
    expect(await shouldCloseAfterConfirm(() => Promise.resolve())).toBe(true)
    expect(await shouldCloseAfterConfirm(() => 0)).toBe(true) // 0 !== false → 关
  })
  it('抛错/reject → 不关(false)', async () => {
    expect(await shouldCloseAfterConfirm(() => { throw new Error('x') })).toBe(false)
    expect(await shouldCloseAfterConfirm(() => Promise.reject(new Error('x')))).toBe(false)
  })
})

describe('FormContainer 行为', () => {
  it('modal:渲染标题 + body', () => {
    renderFC({ variant: 'modal', onConfirm: vi.fn() })
    expect(screen.getByText('弹层标题')).toBeTruthy()
    expect(screen.getByText('表单体')).toBeTruthy()
  })

  it('modal:确认成功 → onOpenChange(false)', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined)
    const { onOpenChange } = renderFC({ variant: 'modal', onConfirm })
    fireEvent.click(okBtn())
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false))
  })

  it('modal:onConfirm 返 false → 不关(onOpenChange 不被调用)', async () => {
    const onConfirm = vi.fn().mockResolvedValue(false)
    const { onOpenChange } = renderFC({ variant: 'modal', onConfirm })
    fireEvent.click(okBtn())
    await waitFor(() => expect(onConfirm).toHaveBeenCalled())
    await new Promise((r) => setTimeout(r, 30))
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  it('不传 onConfirm:点确定直接关(onOpenChange(false))', async () => {
    const { onOpenChange } = renderFC({ variant: 'modal' })
    fireEvent.click(okBtn())
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false))
  })

  it('提交中:取消钮禁用(不可中途关闭)', async () => {
    let release!: () => void
    const gate = new Promise<void>((r) => (release = r))
    const onConfirm = vi.fn().mockImplementation(() => gate)
    const { onOpenChange } = renderFC({ variant: 'modal', onConfirm })
    fireEvent.click(okBtn())
    await waitFor(() => expect(onConfirm).toHaveBeenCalled())
    await waitFor(() => expect((cancelBtn() as HTMLButtonElement).disabled).toBe(true))
    expect(onOpenChange).not.toHaveBeenCalled() // 在途中未关
    release()
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false)) // 完成后才关
  })

  it('drawer:渲染标题 + body + 自建 footer 的取消/确定', () => {
    renderFC({ variant: 'drawer', onConfirm: vi.fn() })
    expect(screen.getByText('弹层标题')).toBeTruthy()
    expect(screen.getByText('表单体')).toBeTruthy()
    expect(cancelBtn()).toBeTruthy()
    expect(okBtn()).toBeTruthy()
  })

  it('不传 variant → 跟随 app.formStyle(drawer)', () => {
    useAppStore.setState({ formStyle: 'drawer' })
    const { baseElement } = render(
      <AntdApp>
        <FormContainer open onOpenChange={vi.fn()} title="x" onConfirm={vi.fn()}>
          <div>表单体</div>
        </FormContainer>
      </AntdApp>,
    )
    expect(baseElement.querySelector('.ant-drawer')).toBeTruthy()
    expect(baseElement.querySelector('.ant-modal')).toBeNull()
  })
})
