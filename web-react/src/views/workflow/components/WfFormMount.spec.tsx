import { useRef, useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App as AntdApp } from 'antd'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import '@/locales'
import type { WfFormSchema } from '@/workflow/schema'
import {
  WfFormMount,
  normalizeViewPath,
  type WfFormComponentProps,
  type WfFormGlob,
  type WfFormMountHandle,
} from './WfFormMount'

vi.mock('@/components/AppIcon', () => ({ AppIcon: () => null }))
vi.mock('@/api', () => ({
  userApi: { page: vi.fn().mockResolvedValue({ items: [], total: 0 }) },
}))

afterEach(cleanup)

const schema: WfFormSchema = {
  version: 1,
  fields: [{ key: 'title', label: '标题', required: true, type: 'text', props: { maxLength: 256 } }],
}

function Consumer({ mode, businessKey }: WfFormComponentProps) {
  return <div>consumer:{mode}:{businessKey}</div>
}

const consumerGlob: WfFormGlob = {
  '/src/views/biz/leave/form.tsx': () => Promise.resolve({ default: Consumer }),
}

function Host(props: {
  formComponent?: string | null
  formSchema?: WfFormSchema | null
  initialJson?: string | null
  viewGlob?: WfFormGlob
}) {
  const [json, setJson] = useState<string | null>(props.initialJson ?? null)
  const [ok, setOk] = useState<string>('—')
  const mountRef = useRef<WfFormMountHandle>(null)
  return (
    <AntdApp>
      <WfFormMount
        ref={mountRef}
        formComponent={props.formComponent}
        formSchema={props.formSchema}
        mode="start"
        businessKey="BK-1"
        variablesJson={json}
        viewGlob={props.viewGlob ?? {}}
        onVariablesChange={setJson}
      />
      <button type="button" onClick={() => setOk(String(mountRef.current?.validate()))}>do-validate</button>
      <output data-testid="json">{json ?? 'null'}</output>
      <output data-testid="ok">{ok}</output>
    </AntdApp>
  )
}

describe('normalizeViewPath', () => {
  it('剥掉 views 前缀、前导斜杠与 .tsx 后缀', () => {
    expect(normalizeViewPath(' views/biz/leave/form.tsx ')).toBe('biz/leave/form')
    expect(normalizeViewPath('/biz/leave/form')).toBe('biz/leave/form')
    expect(normalizeViewPath('biz\\leave\\form')).toBe('biz/leave/form')
  })
})

describe('WfFormMount', () => {
  it('有内置 schema 时渲染内置表单并回显变量', () => {
    render(<Host formSchema={schema} initialJson='{"title":"年假"}' />)
    expect(screen.getByLabelText('标题')).toHaveProperty('value', '年假')
  })

  it('内置表单改值后回抛序列化 JSON', () => {
    render(<Host formSchema={schema} />)
    fireEvent.change(screen.getByLabelText('标题'), { target: { value: '出差' } })
    expect(screen.getByTestId('json').textContent).toBe('{"title":"出差"}')
  })

  it('变量 JSON 非法时给错误提示并拒绝校验通过', () => {
    render(<Host formSchema={schema} initialJson="{oops" />)
    expect(screen.getByText('表单变量格式无效，请联系管理员修复数据。')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'do-validate' }))
    expect(screen.getByTestId('ok').textContent).toBe('false')
  })

  it('变量 JSON 根节点不是对象时同样拒绝', () => {
    render(<Host formSchema={schema} initialJson="[1,2]" />)
    expect(screen.getByText('表单变量格式无效，请联系管理员修复数据。')).toBeTruthy()
  })

  it('formComponent 缺组件时给出带路径的告警,不打断主流程', () => {
    render(<Host formComponent="biz/missing/form" />)
    expect(screen.getByText('未找到业务表单组件:biz/missing/form')).toBeTruthy()
  })

  it('formComponent 命中 glob 时挂载消费者组件并透传 props', async () => {
    render(<Host formComponent="biz/leave/form" viewGlob={consumerGlob} />)
    expect(await screen.findByText('consumer:start:BK-1')).toBeTruthy()
  })

  it('双配置属非法定义:保留消费者挂载点,内置表单让位', async () => {
    render(<Host formComponent="biz/leave/form" formSchema={schema} viewGlob={consumerGlob} />)
    expect(await screen.findByText('consumer:start:BK-1')).toBeTruthy()
    expect(screen.queryByLabelText('标题')).toBeNull()
  })

  it('既无 schema 也无 formComponent 时什么都不渲染,validate 放行', () => {
    render(<Host />)
    fireEvent.click(screen.getByRole('button', { name: 'do-validate' }))
    expect(screen.getByTestId('ok').textContent).toBe('true')
  })
})
