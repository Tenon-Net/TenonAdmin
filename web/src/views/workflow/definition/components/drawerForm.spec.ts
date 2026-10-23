import { describe, expect, it } from 'vitest'
import type { WfModel } from '@/workflow/schema'
import { buildDrawerNodeConfig, loadDrawerForm } from './drawerForm'

describe('workflow config drawer form projection', () => {
  it('loads persisted ids, defaults and condition expressions without sharing model objects', () => {
    const model: WfModel = {
      version: 1,
      formSchema: { version: 1, fields: [{ key: 'amount', label: 'Amount', type: 'money', required: true }] },
      root: {
        id: 'start', type: 'start', name: 'Start',
        next: {
          id: 'approval', type: 'approval', name: 'Review',
          props: {
            assignee: { provider: 'user', params: { userIds: ['9007199254740993'] } },
            formPerms: [{ field: 'amount', access: 'readonly' }],
          },
          next: null,
        },
      },
    }
    const node = model.root.next!
    const form = loadDrawerForm(model, node)

    expect(form.assignee.userIds).toEqual(['9007199254740993'])
    expect(form.approval.formPerms).toEqual({ amount: 'readonly' })
    form.start.formSchema!.fields[0]!.label = 'Changed'
    expect(model.formSchema?.fields[0]?.label).toBe('Amount')
  })

  it('builds approval configuration and omits editable or unknown permissions', () => {
    const model: WfModel = {
      version: 1,
      formSchema: {
        version: 1,
        fields: [
          { key: 'amount', label: 'Amount', type: 'money', required: true },
          { key: 'note', label: 'Note', type: 'text', required: false },
        ],
      },
      root: {
        id: 'approval', type: 'approval', name: 'Review',
        props: { assignee: { provider: 'position', params: { positionId: 7, orgId: 8 } }, mode: 'all', allPassRatio: 75 },
        next: null,
      },
    }
    const form = loadDrawerForm(model, model.root)
    form.approval.formPerms = { amount: 'readonly', note: 'editable', removed: 'hidden' }
    const config = buildDrawerNodeConfig(model.root, form)

    expect(config).toMatchObject({
      type: 'approval',
      assignee: { provider: 'position', params: { positionId: 7, orgId: 8 } },
      mode: 'all',
      allPassRatio: 75,
      formPerms: [{ field: 'amount', access: 'readonly' }],
    })
  })

  it('normalizes webhook headers and multi-level leader mode on save', () => {
    const webhookModel: WfModel = {
      version: 1,
      root: { id: 'hook', type: 'webhook', name: 'Hook', next: null },
    }
    const webhookForm = loadDrawerForm(webhookModel, webhookModel.root)
    webhookForm.webhook.url = 'https://example.test/hook'
    webhookForm.webhook.headers = [{ key: ' X-Key ', value: 'value' }, { key: ' ', value: 'ignored' }]
    expect(buildDrawerNodeConfig(webhookModel.root, webhookForm)).toMatchObject({
      webhookHeaders: { 'X-Key': 'value' },
    })

    const approvalModel: WfModel = {
      version: 1,
      root: { id: 'review', type: 'approval', name: 'Review', next: null },
    }
    const approvalForm = loadDrawerForm(approvalModel, approvalModel.root)
    approvalForm.assignee.provider = 'multiLeader'
    approvalForm.assignee.level = 3
    approvalForm.approval.mode = 'all'
    expect(buildDrawerNodeConfig(approvalModel.root, approvalForm)).toMatchObject({
      mode: 'seq', allPassRatio: 100, assignee: { params: { level: 3 } },
    })
  })
})
