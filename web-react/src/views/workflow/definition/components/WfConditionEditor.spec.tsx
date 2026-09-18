import { useState } from 'react'
import { describe, expect, it, vi, afterEach } from 'vitest'
import { cleanup, render, screen, fireEvent, waitFor } from '@testing-library/react'
import '@/locales'
import { createConditionGroup, createConditionLeaf } from '@/workflow/configuration'
import type { WfConditionExpr } from '@/workflow/schema'
import { WfConditionEditor } from './WfConditionEditor'

vi.mock('@/components/AppIcon', () => ({ AppIcon: () => null }))

afterEach(cleanup)

/** 受控宿主:编辑器是纯展示 + 上抛,表达式态必须由父持有。 */
function Host({ initial }: { initial: WfConditionExpr }) {
  const [value, setValue] = useState(initial)
  return (
    <>
      <WfConditionEditor value={value} onChange={setValue} />
      <output data-testid="expr">{JSON.stringify(value)}</output>
    </>
  )
}

function expr(): WfConditionExpr {
  return JSON.parse(screen.getByTestId('expr').textContent ?? 'null') as WfConditionExpr
}

function leaf(field: string, over: Partial<WfConditionExpr> = {}): WfConditionExpr {
  return { ...createConditionLeaf(), field, ...over }
}

function group(children: WfConditionExpr[]): WfConditionExpr {
  return { ...createConditionGroup(), children }
}

/** 直属子项(嵌套组的折叠项不算在内)。 */
function directItems(container: Element): Element[] {
  return [...container.children].filter((child) => child.classList.contains('ant-collapse-item'))
}

/** antd Select:aria-label 落在内部 combobox 上,展开靠 selector 的 mousedown。 */
async function chooseOption(label: string, option: string) {
  const combobox = screen.getByLabelText(label)
  fireEvent.mouseDown(combobox.closest('.ant-select-content') ?? combobox)
  await waitFor(() => expect(screen.getByText(option)).toBeTruthy())
  fireEvent.click(screen.getByText(option))
}

describe('WfConditionEditor', () => {
  it('空组显示占位,添加条件与条件组各自落成正确形状', async () => {
    render(<Host initial={createConditionGroup()} />)
    expect(screen.getByText('暂无条件，可添加条件或嵌套组')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '添加条件' }))
    await waitFor(() => expect(expr().children).toHaveLength(1))
    // 叶子:children 为 null,带默认操作符与空值。
    expect(expr().children![0]).toMatchObject({ field: '', op: 'eq', value: '', children: null })

    fireEvent.click(screen.getByRole('button', { name: '添加条件组' }))
    await waitFor(() => expect(expr().children).toHaveLength(2))
    expect(expr().children![1]).toMatchObject({ logic: 'and', children: [] })
    expect(screen.queryByText('暂无条件，可添加条件或嵌套组')).toBeNull()
  })

  it('删除按钮只摘掉对应子项', async () => {
    render(<Host initial={group([leaf('amount'), leaf('days')])} />)

    // 展开的第一项才渲染内容;删除钮在该项内。
    fireEvent.click(document.querySelector('.wf-condition-delete')!)
    await waitFor(() => expect(expr().children).toHaveLength(1))
    expect(expr().children![0]).toMatchObject({ field: 'days' })
  })

  it('组内 logic 切换写回表达式', async () => {
    render(<Host initial={group([leaf('amount')])} />)

    await chooseOption('条件逻辑', '满足任一')

    await waitFor(() => expect(expr().logic).toBe('or'))
  })

  it('不需要比较值的操作符:换成「为空」后清掉 value 并给出提示', async () => {
    render(<Host initial={group([leaf('amount', { value: 'x' })])} />)

    await chooseOption('比较方式', '为空')

    await waitFor(() => expect(expr().children![0]!.op).toBe('empty'))
    // setConditionOp 对 none 类操作符直接丢掉 value 键,不留一条发不出去的比较值。
    expect(expr().children![0]).not.toHaveProperty('value')
    expect(screen.getByText('无需比较值')).toBeTruthy()
    expect(document.querySelector('.wf-condition-value')).toBeNull()
  })

  it('叶子字段与比较值受控上抛', async () => {
    render(<Host initial={group([leaf('')])} />)

    fireEvent.change(screen.getByLabelText('变量字段'), { target: { value: 'amount' } })
    await waitFor(() => expect(expr().children![0]).toMatchObject({ field: 'amount' }))

    fireEvent.change(screen.getByLabelText('比较值'), { target: { value: '10000' } })
    await waitFor(() => expect(expr().children![0]).toMatchObject({ value: '10000' }))
  })

  it('只展开根的第一个子项,嵌套后代默认全收起', async () => {
    const nested = group([leaf('nested')])
    render(<Host initial={group([leaf('first'), leaf('second'), nested])} />)

    const rootCollapse = document.querySelector('.wf-condition-children[data-depth="0"]')!
    const rootItems = directItems(rootCollapse)
    expect(rootItems).toHaveLength(3)
    expect(rootItems.filter((item) => item.classList.contains('ant-collapse-item-active'))).toEqual([rootItems[0]])

    // accordion:点开第三项会收起第一项,嵌套组内的子项仍是全关。
    fireEvent.click(rootItems[2]!.querySelector('.ant-collapse-header')!)
    await waitFor(() => expect(rootItems[2]!.classList.contains('ant-collapse-item-active')).toBe(true))
    expect(rootItems[0]!.classList.contains('ant-collapse-item-active')).toBe(false)

    const nestedCollapse = rootItems[2]!.querySelector('.wf-condition-children[data-depth="1"]')!
    const nestedItems = directItems(nestedCollapse)
    expect(nestedItems).toHaveLength(1)
    expect(nestedItems.some((item) => item.classList.contains('ant-collapse-item-active'))).toBe(false)
  })
})
