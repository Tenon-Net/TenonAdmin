import { useState } from 'react'
import { describe, expect, it, vi, afterEach } from 'vitest'
import { cleanup, render, screen, fireEvent, waitFor } from '@testing-library/react'
import '@/locales'
import { createWfFormField, updateWfFormFieldProp } from '@/workflow/formSchema'
import type { WfFormField, WfFormSchema } from '@/workflow/schema'
import { WfFormDesigner } from './WfFormDesigner'

vi.mock('@/components/AppIcon', () => ({ AppIcon: () => null }))

afterEach(cleanup)

/** 受控宿主:设计器不持有 schema,删空要能把 null 上抛到父级。 */
function Host({ initial }: { initial: WfFormSchema | null }) {
  const [value, setValue] = useState<WfFormSchema | null>(initial)
  return (
    <>
      <WfFormDesigner value={value} onChange={setValue} />
      <output data-testid="schema">{JSON.stringify(value)}</output>
    </>
  )
}

function schema(): WfFormSchema | null {
  return JSON.parse(screen.getByTestId('schema').textContent ?? 'null') as WfFormSchema | null
}

function schemaOf(...fields: WfFormField[]): WfFormSchema {
  return { version: 1, fields }
}

/** Form.Item 无 name 时 antd 不给 label 挂 for,只能按标签文字回溯表单项。 */
function itemByLabel(label: string): HTMLElement {
  const item = [...document.querySelectorAll<HTMLElement>('.ant-form-item')].find(
    (candidate) => candidate.querySelector('.ant-form-item-label')?.textContent?.trim() === label,
  )
  if (!item) throw new Error(`未找到表单项:${label}`)
  return item
}

async function chooseOption(select: HTMLElement, option: string) {
  fireEvent.mouseDown(select.querySelector('.ant-select-content')!)
  await waitFor(() => expect(screen.getByText(option)).toBeTruthy())
  fireEvent.click(screen.getByText(option))
}

describe('WfFormDesigner', () => {
  it('删除最后一个字段回传 null(= 该流程无内置表单)', async () => {
    render(<Host initial={schemaOf(createWfFormField('text', 'title'))} />)

    fireEvent.click(screen.getByRole('button', { name: '删除字段' }))

    await waitFor(() => expect(schema()).toBeNull())
    expect(screen.getByText('0/50')).toBeTruthy()
  })

  it('删除中间字段只摘掉它,其余顺序不变', async () => {
    render(<Host initial={schemaOf(
      createWfFormField('text', 'a'),
      createWfFormField('number', 'b'),
      createWfFormField('date', 'c'),
    )} />)

    fireEvent.click(screen.getAllByRole('button', { name: '删除字段' })[1]!)

    await waitFor(() => expect(schema()?.fields.map((field) => field.key)).toEqual(['a', 'c']))
  })

  it('人员字段关闭多选时同时清掉 maxSelected(留着会被校验判 propInvalid)', async () => {
    const user = updateWfFormFieldProp(
      updateWfFormFieldProp(createWfFormField('user', 'owner'), 'multiple', true),
      'maxSelected',
      3,
    )
    render(<Host initial={schemaOf(user)} />)
    expect(itemByLabel('最多选择')).toBeTruthy()

    fireEvent.click(itemByLabel('允许多选').querySelector('button[role="switch"]')!)

    await waitFor(() => expect(schema()?.fields[0]!.props).toEqual({ multiple: false }))
    // 输入框跟着消失,避免留下一个改不动又发布不掉的脏属性。
    expect(() => itemByLabel('最多选择')).toThrow()
  })

  it('换控件类型重建 props:保留通用字段,丢掉旧类型专属属性', async () => {
    const text = updateWfFormFieldProp(createWfFormField('text', 'title'), 'maxLength', 200)
    render(<Host initial={schemaOf({ ...text, label: '标题', required: true })} />)

    await chooseOption(itemByLabel('控件类型'), '数字')

    await waitFor(() => expect(schema()?.fields[0]!.type).toBe('number'))
    const field = schema()!.fields[0]!
    expect(field).toMatchObject({ key: 'title', label: '标题', required: true })
    expect(field.props).not.toHaveProperty('maxLength')
  })

  it('工具栏选控件类型即追加字段,上移换序', async () => {
    render(<Host initial={null} />)

    await chooseOption(document.querySelector<HTMLElement>('.wf-form-type-select')!, '单行文本')
    await waitFor(() => expect(schema()?.fields.map((field) => field.key)).toEqual(['text1']))

    await chooseOption(document.querySelector<HTMLElement>('.wf-form-type-select')!, '金额')
    await waitFor(() => expect(schema()?.fields.map((field) => field.key)).toEqual(['text1', 'money1']))

    fireEvent.click(screen.getAllByRole('button', { name: '上移字段' })[1]!)
    await waitFor(() => expect(schema()?.fields.map((field) => field.key)).toEqual(['money1', 'text1']))
  })

  it('字段键非法时就地报错', async () => {
    render(<Host initial={schemaOf(createWfFormField('text', 'title'))} />)

    fireEvent.change(itemByLabel('字段键').querySelector('input')!, { target: { value: '1-bad' } })

    await waitFor(() => expect(screen.getByText(/字段键格式无效/)).toBeTruthy())
  })
})
