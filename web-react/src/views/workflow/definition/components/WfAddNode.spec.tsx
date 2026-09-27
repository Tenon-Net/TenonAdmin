import { describe, expect, it, vi, afterEach } from 'vitest'
import { cleanup, render, screen, fireEvent, waitFor } from '@testing-library/react'
import '@/locales'
import { WfAddNode } from './WfAddNode'

// 离线图标集在测试里无意义,且 ensureIconLoaded 会打异步 setState 噪声。
vi.mock('@/components/AppIcon', () => ({ AppIcon: () => null }))

afterEach(cleanup)

/** 点开加号菜单,回传当前可见的节点类型文案。 */
async function openMenu(allowParallel?: boolean): Promise<string[]> {
  render(<WfAddNode onAdd={() => {}} {...(allowParallel === undefined ? {} : { allowParallel })} />)
  fireEvent.click(screen.getByRole('button', { name: '添加节点' }))
  await waitFor(() => expect(document.querySelectorAll('.wf-add-label').length).toBeGreaterThan(0))
  return [...document.querySelectorAll('.wf-add-label')].map((el) => el.textContent ?? '')
}

describe('WfAddNode', () => {
  it('缺省展示并行入口(漏传 allowParallel 不能吞掉菜单项)', async () => {
    expect(await openMenu()).toContain('并行')
  })

  it('allowParallel=false 时隐藏并行、保留其余入口', async () => {
    const labels = await openMenu(false)
    expect(labels).not.toContain('并行')
    expect(labels).toContain('Webhook')
    expect(labels).toContain('审批')
  })

  it('点选菜单项上抛类型并收起弹层', async () => {
    const onAdd = vi.fn()
    render(<WfAddNode onAdd={onAdd} />)
    fireEvent.click(screen.getByRole('button', { name: '添加节点' }))
    await waitFor(() => expect(screen.getByText('条件分支')).toBeTruthy())
    fireEvent.click(screen.getByText('条件分支'))
    expect(onAdd).toHaveBeenCalledWith('branch')
  })
})
