import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, waitFor, fireEvent } from '@testing-library/react'
import { useState } from 'react'
import { App as AntdApp } from 'antd'
import '@/locales' // t() 要真文案(成功 toast)
import { StatusSwitch, type StatusSwitchProps } from './StatusSwitch'

// ask 走 useConfirm;这里把它 mock 成可控开关,专测 StatusSwitch 自己的确认门 + 悲观语义。
const askMock = vi.fn()
vi.mock('@/hooks/useConfirm', () => ({
  useConfirm: () => ({ ask: askMock, confirm: vi.fn(), run: vi.fn() }),
}))

/** 用 Host 托管 value:StatusSwitch 是受控悲观开关,只有父在 onChange 里回写 value 才会真正翻转。 */
function Host(props: Omit<StatusSwitchProps, 'value' | 'onChange'>) {
  const [v, setV] = useState(false)
  return (
    <AntdApp>
      <StatusSwitch value={v} onChange={setV} {...props} />
    </AntdApp>
  )
}
const sw = () => screen.getByRole('switch')
const checked = () => sw().getAttribute('aria-checked') === 'true'

beforeEach(() => { askMock.mockReset() })
afterEach(cleanup)

describe('StatusSwitch —— 悲观更新', () => {
  it('请求成功才翻到新态(onChange 生效)', async () => {
    const request = vi.fn().mockResolvedValue(undefined)
    render(<Host request={request} />)
    expect(checked()).toBe(false)
    fireEvent.click(sw())
    await waitFor(() => expect(request).toHaveBeenCalledWith(true))
    await waitFor(() => expect(checked()).toBe(true)) // 成功后才翻
  })

  it('请求失败不翻转(不 onChange = 零成本回滚)', async () => {
    const request = vi.fn().mockRejectedValue(new Error('boom'))
    render(<Host request={request} />)
    fireEvent.click(sw())
    await waitFor(() => expect(request).toHaveBeenCalled())
    await new Promise((r) => setTimeout(r, 20))
    expect(checked()).toBe(false) // 从未翻转
  })
})

describe('StatusSwitch —— 二次确认门', () => {
  it('取消(ask=false)→ 不请求、不翻转', async () => {
    askMock.mockResolvedValue(false)
    const request = vi.fn().mockResolvedValue(undefined)
    render(<Host request={request} confirm="确定停用?" />)
    fireEvent.click(sw())
    await waitFor(() => expect(askMock).toHaveBeenCalledWith({ content: '确定停用?' }))
    await new Promise((r) => setTimeout(r, 20))
    expect(request).not.toHaveBeenCalled()
    expect(checked()).toBe(false)
  })

  it('确认(ask=true)→ 照常请求', async () => {
    askMock.mockResolvedValue(true)
    const request = vi.fn().mockResolvedValue(undefined)
    render(<Host request={request} confirm="确定?" />)
    fireEvent.click(sw())
    await waitFor(() => expect(request).toHaveBeenCalledWith(true))
  })

  it('confirm 函数返回空 → 本次跳过确认,直接请求(ask 不被调用)', async () => {
    const request = vi.fn().mockResolvedValue(undefined)
    const confirm = (next: boolean) => (next ? null : '停用吗') // 启用不问
    render(<Host request={request} confirm={confirm} />)
    fireEvent.click(sw()) // false→true,confirm(true)=null → 跳过确认
    await waitFor(() => expect(request).toHaveBeenCalledWith(true))
    expect(askMock).not.toHaveBeenCalled()
  })
})

describe('StatusSwitch —— loading / toast', () => {
  it('请求在途:自锁(loading + disabled),防连点', async () => {
    let release!: () => void
    const gate = new Promise<void>((r) => (release = r))
    const request = vi.fn().mockImplementation(() => gate)
    render(<Host request={request} />)
    fireEvent.click(sw())
    await waitFor(() => expect(sw().className).toContain('ant-switch-loading'))
    expect((sw() as HTMLButtonElement).disabled).toBe(true)
    release()
    await waitFor(() => expect(checked()).toBe(true))
  })

  it('成功默认弹 toast', async () => {
    const request = vi.fn().mockResolvedValue(undefined)
    render(<Host request={request} />)
    fireEvent.click(sw())
    expect(await screen.findByText('操作成功')).toBeTruthy()
  })

  it('successMsg=false → 成功不弹 toast(压根不出 message)', async () => {
    const request = vi.fn().mockResolvedValue(undefined)
    render(<Host request={request} successMsg={false} />)
    fireEvent.click(sw())
    await waitFor(() => expect(request).toHaveBeenCalled())
    await new Promise((r) => setTimeout(r, 30))
    // 断「一条 toast 都没出」,不是断「没有『操作成功』这几个字」——
    // 变异 `successMsg !== false → true` 会走 `message.success(false ?? …)`=`message.success(false)`,
    // 弹出的是**空内容** toast,`queryByText('操作成功')` 照样为 null(变异存活)。改数 notice DOM 节点。
    expect(document.querySelectorAll('.ant-message-notice').length).toBe(0)
  })
})
