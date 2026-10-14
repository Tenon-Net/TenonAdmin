import { useRef, useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App as AntdApp } from 'antd'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import '@/locales'
import type { WfFormValues } from '@/workflow/formRuntime'
import type { WfFormFieldPerm, WfFormSchema } from '@/workflow/schema'
import { WfBuiltinForm, type WfBuiltinFormHandle, type WfFormMode } from './WfBuiltinForm'

vi.mock('@/components/AppIcon', () => ({ AppIcon: () => null }))
vi.mock('@/api', () => ({
  userApi: { page: vi.fn().mockResolvedValue({ items: [], total: 0 }) },
}))
// 上传只在 onUploaded 处交接文件 Id;用假触发器把「上传成功」这件事变成一次点击。
vi.mock('@/components/FileUpload', () => ({
  FileUpload: ({ onUploaded }: { onUploaded: (out: { id: string }) => void }) => (
    <button type="button" onClick={() => onUploaded({ id: '1500000000000000001' })}>fake-upload</button>
  ),
}))

afterEach(cleanup)

const schema: WfFormSchema = {
  version: 1,
  fields: [
    { key: 'title', label: '标题', required: true, type: 'text', props: { maxLength: 256 } },
    { key: 'days', label: '天数', required: false, type: 'number', props: { min: 1, max: 30 } },
    { key: 'reason', label: '原因', required: false, type: 'textarea', props: { rows: 3 } },
    { key: 'kind', label: '类型', required: false, type: 'select', props: { options: [{ label: '年假', value: 'annual' }] } },
    { key: 'amount', label: '金额', required: false, type: 'money', props: { min: 0, max: 10000 } },
    { key: 'beginDate', label: '开始日期', required: false, type: 'date' },
    { key: 'beginAt', label: '开始时间', required: false, type: 'datetime' },
    {
      key: 'tags',
      label: '标签',
      required: false,
      type: 'multiSelect',
      props: {
        maxSelected: 2,
        options: [
          { label: '甲', value: 'a' },
          { label: '乙', value: 'b' },
          { label: '丙', value: 'c' },
        ],
      },
    },
    { key: 'owner', label: '负责人', required: false, type: 'user', props: { multiple: false } },
    { key: 'proof', label: '附件', required: false, type: 'attachment', props: { multiple: false } },
  ],
}

function Host({ mode, permissions, initial = {} }: {
  mode: WfFormMode
  permissions?: WfFormFieldPerm[] | null
  initial?: WfFormValues
}) {
  const [values, setValues] = useState<WfFormValues>(initial)
  const [ok, setOk] = useState<string>('—')
  const formRef = useRef<WfBuiltinFormHandle>(null)
  return (
    <AntdApp>
      <WfBuiltinForm
        ref={formRef}
        schema={schema}
        value={values}
        mode={mode}
        permissions={permissions}
        onChange={setValues}
      />
      <button type="button" onClick={() => setOk(String(formRef.current?.validate()))}>do-validate</button>
      <output data-testid="values">{JSON.stringify(values)}</output>
      <output data-testid="ok">{ok}</output>
    </AntdApp>
  )
}

const values = () => JSON.parse(screen.getByTestId('values').textContent || '{}') as WfFormValues

describe('WfBuiltinForm', () => {
  it('10 种控件都至少渲染一次', () => {
    render(<Host mode="start" />)
    for (const label of ['标题', '天数', '原因', '类型', '金额', '开始日期', '开始时间', '标签', '负责人', '附件']) {
      expect(screen.getByText(label)).toBeTruthy()
    }
  })

  it('发起态写值经 onChange 上抛,按字段键归位', () => {
    render(<Host mode="start" />)
    fireEvent.change(screen.getByLabelText('标题'), { target: { value: '年假申请' } })
    expect(values()).toEqual({ title: '年假申请' })
  })

  it('金额输入 -5 显示越界错误,清空必填事由在失焦时显示错误', () => {
    function Resubmit() {
      const [formValues, setFormValues] = useState<WfFormValues>({ amount: 8000, reason: '差旅' })
      const resubmitSchema: WfFormSchema = {
        version: 1,
        fields: [
          { key: 'amount', label: '金额', required: true, type: 'money', props: { min: 0 } },
          { key: 'reason', label: '事由', required: true, type: 'textarea', props: { rows: 3 } },
        ],
      }
      return (
        <AntdApp>
          <WfBuiltinForm schema={resubmitSchema} value={formValues} mode="start" onChange={setFormValues} />
          <output data-testid="resubmit-values">{JSON.stringify(formValues)}</output>
        </AntdApp>
      )
    }
    render(<Resubmit />)
    fireEvent.change(screen.getByLabelText('金额'), { target: { value: '-5' } })
    expect(screen.getByText('字段值超出范围')).toBeTruthy()
    expect(JSON.parse(screen.getByTestId('resubmit-values').textContent || '{}').amount).toBe(-5)

    const reason = screen.getByLabelText('事由')
    fireEvent.change(reason, { target: { value: '' } })
    expect(screen.queryByText('请填写此字段')).toBeNull()
    fireEvent.blur(reason)
    expect(screen.getByText('请填写此字段')).toBeTruthy()
  })

  it('必填为空时 validate 失败并把错误落到该字段', () => {
    render(<Host mode="start" />)
    fireEvent.click(screen.getByRole('button', { name: 'do-validate' }))
    expect(screen.getByTestId('ok').textContent).toBe('false')
    expect(screen.getByText('请填写此字段')).toBeTruthy()
  })

  it('发起态忽略字段权限:readonly 权限也照样可编辑', () => {
    render(<Host mode="start" permissions={[{ field: 'title', access: 'readonly' }]} />)
    expect(screen.getByLabelText('标题')).toHaveProperty('disabled', false)
  })

  it('办理态 hidden 字段不渲染、readonly 字段禁用且改不动', () => {
    render(
      <Host
        mode="approve"
        initial={{ title: '原值', days: 3 }}
        permissions={[{ field: 'days', access: 'hidden' }, { field: 'title', access: 'readonly' }]}
      />,
    )
    expect(screen.queryByLabelText('天数')).toBeNull()
    const title = screen.getByLabelText('标题')
    expect(title).toHaveProperty('disabled', true)
    expect(screen.getByLabelText('原因')).toHaveProperty('disabled', true)
    fireEvent.change(title, { target: { value: '偷改' } })
    expect(values()).toEqual({ title: '原值', days: 3 })
  })

  it('办理态未授权的字段只读,显式 editable 才可改', () => {
    render(<Host mode="approve" permissions={[{ field: 'title', access: 'editable' }]} />)
    expect(screen.getByLabelText('标题')).toHaveProperty('disabled', false)
    expect(screen.getByLabelText('原因')).toHaveProperty('disabled', true)
  })

  it('办理态只校验 editable 字段:必填但 readonly 的空字段不拦提交', () => {
    render(<Host mode="approve" permissions={[{ field: 'title', access: 'readonly' }]} />)
    fireEvent.click(screen.getByRole('button', { name: 'do-validate' }))
    expect(screen.getByTestId('ok').textContent).toBe('true')
  })

  it('查看态所有控件禁用,不因缺权限变成可编辑', () => {
    render(<Host mode="view" initial={{ title: '只读回放' }} />)
    expect(screen.getByLabelText('标题')).toHaveProperty('disabled', true)
    expect(screen.getByLabelText('原因')).toHaveProperty('disabled', true)
  })

  it('附件只存文件 Id,且十进制 string 雪花原样保留(不过 Number 掉精度)', () => {
    render(<Host mode="start" />)
    fireEvent.click(screen.getByRole('button', { name: 'fake-upload' }))
    expect(values()).toEqual({ proof: '1500000000000000001' })
    expect(screen.getByText('1500000000000000001')).toBeTruthy()
  })

  it('单附件已满后不再给上传入口,移除标签后回到可上传', () => {
    render(<Host mode="start" />)
    fireEvent.click(screen.getByRole('button', { name: 'fake-upload' }))
    expect(screen.queryByRole('button', { name: 'fake-upload' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /close/i }))
    expect(values()).toEqual({ proof: null })
    expect(screen.getByRole('button', { name: 'fake-upload' })).toBeTruthy()
  })

  it('多选字段按 maxSelected 限制选择数量', () => {
    render(<Host mode="start" />)
    const selector = document.querySelector('#wf-field-tags')?.closest('.ant-select')?.querySelector('.ant-select-content')
    expect(selector).toBeTruthy()
    for (const option of ['甲', '乙', '丙']) {
      fireEvent.mouseDown(selector!)
      fireEvent.click(document.querySelector(`.ant-select-dropdown:not(.ant-select-dropdown-hidden) [title="${option}"]`)!)
    }
    expect(values().tags).toEqual(['a', 'b'])
  })
})
