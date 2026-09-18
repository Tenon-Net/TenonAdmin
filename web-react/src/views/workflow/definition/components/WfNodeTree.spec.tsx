import { useState } from 'react'
import { describe, expect, it, vi, afterEach } from 'vitest'
import { cleanup, render, screen, fireEvent, waitFor } from '@testing-library/react'
import '@/locales'
import { createBranchNode, createDefaultModel, createParallelNode, findNode, flattenChain } from '@/workflow/model'
import type { WfModel, WfNode } from '@/workflow/schema'
import { WfNodeTree } from './WfNodeTree'

vi.mock('@/components/AppIcon', () => ({ AppIcon: () => null }))

afterEach(cleanup)

/** 受控宿主:树是纯展示 + 上抛,模型态必须由父持有。 */
function Host({ initial, onSelect }: { initial: WfModel; onSelect?: (node: WfNode) => void }) {
  const [model, setModel] = useState(initial)
  return (
    <>
      <WfNodeTree model={model} onModelChange={setModel} onSelect={(node) => onSelect?.(node)} />
      <output data-testid="chain">{flattenChain(model.root).map((n) => n.type).join('>')}</output>
      <output data-testid="arms">
        {(findNode(model.root, 'br1')?.conditions ?? []).map((arm) => `${arm.id}=${arm.name}`).join(',')}
      </output>
      <output data-testid="parms">
        {(findNode(model.root, 'pa1')?.parallelArms ?? []).map((arm) => arm.id).join(',')}
      </output>
    </>
  )
}

function modelWith(node: WfNode): WfModel {
  const model = createDefaultModel()
  model.root.next = node
  return model
}

/** 点开第 index 个加号,回传菜单里可见的节点类型文案。 */
async function openAdd(index: number): Promise<string[]> {
  const buttons = screen.getAllByRole('button', { name: '添加节点' })
  fireEvent.click(buttons[index]!)
  await waitFor(() => expect(document.querySelectorAll('.wf-add-label').length).toBeGreaterThan(0))
  return [...document.querySelectorAll('.wf-add-label')].map((el) => el.textContent ?? '')
}

describe('WfNodeTree', () => {
  it('主链加号提供并行入口(顶层恒可插并行)', async () => {
    render(<Host initial={createDefaultModel()} />)
    expect(await openAdd(0)).toContain('并行')
  })

  it('条件分支臂内仍可插并行,并行臂内禁止嵌套并行', async () => {
    const { unmount } = render(<Host initial={modelWith(createBranchNode({ id: 'br1' }))} />)
    // 加号顺序:发起人之后 → 分支臂头 ×2 → 分支之后
    expect(await openAdd(1)).toContain('并行')
    unmount()

    render(<Host initial={modelWith(createParallelNode({ id: 'pa1' }))} />)
    const inArm = await openAdd(1)
    expect(inArm).not.toContain('并行')
    expect(inArm).toContain('审批')
  })

  it('插入节点后写回模型并选中新节点', async () => {
    const onSelect = vi.fn()
    render(<Host initial={createDefaultModel()} onSelect={onSelect} />)
    await openAdd(0)
    fireEvent.click(screen.getByText('审批'))
    await waitFor(() => expect(screen.getByTestId('chain').textContent).toBe('start>approval'))
    expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ type: 'approval' }))
  })

  it('臂名只在失焦/回车结算,逐键输入不改模型', async () => {
    render(<Host initial={modelWith(createBranchNode({ id: 'br1', conditions: [
      { id: 'a1', name: '大额', expr: { logic: 'and', children: [] }, isDefault: false, next: null },
      { id: 'a2', name: '其他', expr: null, isDefault: true, next: null },
    ] }))} />)

    const input = screen.getAllByLabelText('分支名称')[0] as HTMLInputElement
    fireEvent.change(input, { target: { value: '小额' } })
    expect(screen.getByTestId('arms').textContent).toBe('a1=大额,a2=其他')

    fireEvent.blur(input)
    await waitFor(() => expect(screen.getByTestId('arms').textContent).toBe('a1=小额,a2=其他'))
  })

  it('并行臂可增可删,臂数写回模型', async () => {
    render(<Host initial={modelWith(createParallelNode({ id: 'pa1' }))} />)
    const before = screen.getByTestId('parms').textContent!.split(',')
    expect(before).toHaveLength(2)

    fireEvent.click(screen.getByRole('button', { name: '添加并行臂' }))
    await waitFor(() => expect(screen.getByTestId('parms').textContent!.split(',')).toHaveLength(3))
    const added = screen.getByTestId('parms').textContent!.split(',')
    expect(added.slice(0, 2)).toEqual(before)

    // 并行臂没有默认臂,三条都带删除钮;删首条后剩余顺序不变。
    expect(document.querySelectorAll('.wf-arm-remove')).toHaveLength(3)
    fireEvent.click(document.querySelector<HTMLElement>('.wf-arm-remove')!)
    await waitFor(() => expect(screen.getByTestId('parms').textContent).toBe(added.slice(1).join(',')))
  })

  it('条件分支加臂插在默认臂之前,默认臂始终唯一', async () => {
    render(<Host initial={modelWith(createBranchNode({ id: 'br1' }))} />)

    fireEvent.click(screen.getByRole('button', { name: '添加条件分支' }))
    await waitFor(() => expect(screen.getByTestId('arms').textContent!.split(',')).toHaveLength(3))
    expect(screen.getAllByText('默认')).toHaveLength(1)
    expect(document.querySelectorAll('.wf-arm-remove')).toHaveLength(2)
  })

  it('默认臂只显示默认标记,不给删除按钮', () => {
    render(<Host initial={modelWith(createBranchNode({ id: 'br1' }))} />)
    expect(screen.getByText('默认')).toBeTruthy()
    // 两条臂里只有非默认臂带删除钮(节点卡的删除钮 aria-label 也是「删除」,故按类名定位)。
    expect(document.querySelectorAll('.wf-arm-remove')).toHaveLength(1)
  })
})
