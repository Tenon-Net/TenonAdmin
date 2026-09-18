import { useState } from 'react'
import { describe, expect, it, vi, afterEach } from 'vitest'
import { App } from 'antd'
import { cleanup, render, screen, fireEvent, waitFor } from '@testing-library/react'
import '@/locales'
import { cloneModel, createApprovalNode, createDefaultModel, createWebhookNode } from '@/workflow/model'
import type { WfModel } from '@/workflow/schema'
import { WfConfigDrawer } from './WfConfigDrawer'

vi.mock('@/components/AppIcon', () => ({ AppIcon: () => null }))
vi.mock('@/api', () => ({
  userApi: { page: vi.fn().mockResolvedValue({ items: [], total: 0 }) },
  roleApi: { page: vi.fn().mockResolvedValue({ items: [], total: 0 }) },
  positionApi: { page: vi.fn().mockResolvedValue({ items: [], total: 0 }) },
  orgApi: { list: vi.fn().mockResolvedValue([]) },
}))

afterEach(cleanup)

function Host({ initial, nodeId, onModelChange }: { initial: WfModel; nodeId: string; onModelChange: (m: WfModel) => void }) {
  const [model, setModel] = useState(initial)
  return (
    <App>
      {/* 模拟树侧的无关编辑:换一个新的 model 对象,但选中节点不变。 */}
      <button type="button" onClick={() => setModel(cloneModel(model))}>touch-model</button>
      <WfConfigDrawer
        open
        model={model}
        nodeId={nodeId}
        onOpenChange={() => {}}
        onModelChange={(next) => {
          setModel(next)
          onModelChange(next)
        }}
      />
    </App>
  )
}

function modelWith(node: ReturnType<typeof createApprovalNode>): WfModel {
  const model = createDefaultModel()
  model.root.next = node
  return model
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

/** 展开「高级」折叠区(低频项都收在里面)。 */
function openAdvanced() {
  fireEvent.click(document.querySelector('.wf-advanced .ant-collapse-header')!)
}

describe('WfConfigDrawer', () => {
  it('保存把节点名写回模型', async () => {
    const onModelChange = vi.fn()
    const model = modelWith(createApprovalNode({ id: 'ap1', name: '经理审批' }))
    render(<Host initial={model} nodeId="ap1" onModelChange={onModelChange} />)

    fireEvent.change(await screen.findByLabelText('节点名称'), { target: { value: '总监审批' } })
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))

    await waitFor(() => expect(onModelChange).toHaveBeenCalled())
    expect(onModelChange.mock.calls[0]![0].root.next.name).toBe('总监审批')
  })

  it('模型对象换新但节点未变时不冲掉正在编辑的表单', async () => {
    const model = modelWith(createApprovalNode({ id: 'ap1', name: '经理审批' }))
    render(<Host initial={model} nodeId="ap1" onModelChange={() => {}} />)

    const input = await screen.findByLabelText('节点名称')
    fireEvent.change(input, { target: { value: '改到一半' } })
    fireEvent.click(screen.getByRole('button', { name: 'touch-model' }))

    expect((input as HTMLInputElement).value).toBe('改到一半')
  })

  it('Webhook URL 非法时拒绝保存并给出提示', async () => {
    const onModelChange = vi.fn()
    const model = modelWith(createWebhookNode({ id: 'wh1' }))
    render(<Host initial={model} nodeId="wh1" onModelChange={onModelChange} />)

    fireEvent.change(await screen.findByLabelText('Webhook URL'), { target: { value: 'ftp://nope' } })
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))

    // 内联报错 + message.warning 两处都提示,故按 all 断言存在而不是唯一。
    await waitFor(() => expect(screen.getAllByText('请输入有效的 http/https URL').length).toBeGreaterThan(0))
    expect(onModelChange).not.toHaveBeenCalled()
  })

  it('Webhook 默认只露 5 项,请求头与最大尝试次数收在「高级」里', async () => {
    const model = modelWith(createWebhookNode({ id: 'wh1' }))
    render(<Host initial={model} nodeId="wh1" onModelChange={() => {}} />)
    await screen.findByLabelText('Webhook URL')

    // 节点名称 + URL + 请求方法 + 超时 + 失败策略;其余都在高级区。
    expect(document.querySelectorAll('.ant-form > .ant-form-item')).toHaveLength(5)
    expect(document.querySelector('.wf-webhook-headers')).toBeNull()

    openAdvanced()
    await waitFor(() => expect(document.querySelector('.wf-webhook-headers')).not.toBeNull())
    expect(itemByLabel('最大尝试次数')).toBeTruthy()
  })

  it('Webhook 六个字段一起写回模型', async () => {
    const onModelChange = vi.fn()
    const model = modelWith(createWebhookNode({ id: 'wh1' }))
    render(<Host initial={model} nodeId="wh1" onModelChange={onModelChange} />)

    fireEvent.change(await screen.findByLabelText('Webhook URL'), {
      target: { value: 'https://example.com/hooks/m3a2' },
    })
    await chooseOption(itemByLabel('请求方法'), 'PATCH')
    fireEvent.change(itemByLabel('超时（秒）').querySelector('input')!, { target: { value: '45' } })
    await chooseOption(itemByLabel('失败策略'), '转人工处理')

    openAdvanced()
    await waitFor(() => expect(screen.getByRole('button', { name: '添加请求头' })).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: '添加请求头' }))
    fireEvent.change(await screen.findByPlaceholderText('名称'), { target: { value: 'X-M3A2' } })
    fireEvent.change(screen.getByPlaceholderText('值'), { target: { value: 'acceptance' } })
    fireEvent.change(itemByLabel('最大尝试次数').querySelector('input')!, { target: { value: '5' } })

    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))

    await waitFor(() => expect(onModelChange).toHaveBeenCalled())
    expect(onModelChange.mock.calls.at(-1)![0].root.next.props).toMatchObject({
      webhookUrl: 'https://example.com/hooks/m3a2',
      webhookMethod: 'PATCH',
      webhookTimeoutSeconds: 45,
      webhookOnFailure: 'manual',
      webhookHeaders: { 'X-M3A2': 'acceptance' },
      maxAttempts: 5,
    })
  })
})
