import { describe, expect, it } from 'vitest'
import {
  addWfFormField,
  createWfFormField,
  moveWfFormField,
  projectWfRuntimeModel,
  removeWfFormField,
  serializeWfFormSchema,
  validateWfFormSchema,
} from './formSchema'
import { WF_FORM_FIELD_TYPES, type WfFormField, type WfFormSchema } from './schema'

describe('workflow form schema', () => {
  it('projects ai decision nodes for runtime replay without making them insertable', () => {
    const model = projectWfRuntimeModel({
      version: 1,
      root: {
        id: 'start',
        type: 'start',
        name: '发起',
        next: { id: 'ai', type: 'aiDecision', name: 'AI 判断', next: null },
      },
    })

    expect(model?.root.next?.type).toBe('aiDecision')
  })

  it('creates all ten controls with backend-aligned editable props', () => {
    expect(WF_FORM_FIELD_TYPES.map((type) => [type, createWfFormField(type).props])).toEqual([
      ['text', { maxLength: 256 }],
      ['textarea', { maxLength: 4000, rows: 4 }],
      ['number', { precision: 0 }],
      ['money', {}],
      ['date', {}],
      ['datetime', {}],
      ['select', { options: [{ label: '选项 1', value: 'option1' }] }],
      ['multiSelect', { options: [{ label: '选项 1', value: 'option1' }], maxSelected: 1 }],
      ['user', { multiple: false }],
      ['attachment', { multiple: false, maxCount: 1, maxSizeMb: 10 }],
    ])
  })

  it('adds unique keys, moves without mutation, and removes the last field as null', () => {
    const first = addWfFormField(null, 'text')
    const second = addWfFormField(first, 'text')
    const third = addWfFormField(second, 'date')
    const moved = moveWfFormField(third, 2, -1)

    expect(third.fields.map((field) => field.key)).toEqual(['text1', 'text2', 'date1'])
    expect(moved.fields.map((field) => field.key)).toEqual(['text1', 'date1', 'text2'])
    expect(moveWfFormField(moved, 0, -1)).toBe(moved)
    expect(removeWfFormField(first, 0)).toBeNull()
  })

  it('trims display strings while preserving field keys and datetime offsets', () => {
    const schema: WfFormSchema = {
      version: 1,
      fields: [{
        ...createWfFormField('datetime', ' startedAt '),
        label: ' 开始时间 ',
        placeholder: ' 请选择 ',
        props: { min: '2026-01-01T08:00:00+08:00' },
      }],
    }

    const serialized = serializeWfFormSchema(schema)
    expect(serialized?.fields[0]).toMatchObject({
      key: ' startedAt ',
      label: '开始时间',
      placeholder: '请选择',
      props: { min: '2026-01-01T08:00:00+08:00' },
    })
    expect(schema.fields[0]!.label).toBe(' 开始时间 ')
  })

  it.each([
    ['bad key', field('1bad', '名称', 'text')],
    ['blank label', field('valid', '  ', 'text')],
    ['unknown prop', { ...field('valid', '名称', 'text'), props: { unknown: true } }],
    ['impossible date', { ...field('valid', '日期', 'date'), props: { min: '2026-02-30' } }],
    ['impossible datetime', { ...field('valid', '时间', 'datetime'), props: { min: '2026-02-30T10:00:00+08:00' } }],
    ['invalid datetime offset', { ...field('valid', '时间', 'datetime'), props: { min: '2026-01-01T10:00:00+14:01' } }],
    ['datetime without timezone', { ...field('valid', '时间', 'datetime'), props: { min: '2026-01-01T10:00:00' } }],
    ['reversed range', { ...field('valid', '数字', 'number'), props: { min: 2, max: 1 } }],
    ['duplicate options', { ...field('valid', '选项', 'select'), props: { options: [{ label: 'A', value: 'x' }, { label: 'B', value: 'x' }] } }],
    ['single user max', { ...field('valid', '人员', 'user'), props: { multiple: false, maxSelected: 2 } }],
    ['MIME attachment accept', { ...field('valid', '附件', 'attachment'), props: { accept: 'image/*' } }],
  ])('rejects %s', (_name, invalid) => {
    expect(validateWfFormSchema({ version: 1, fields: [invalid] }).length).toBeGreaterThan(0)
  })

  it('distinguishes duplicate keys, oversized field lists, and valid offset ranges', () => {
    const duplicate = field('same', 'A', 'text')
    const issues = validateWfFormSchema({ version: 1, fields: [duplicate, { ...duplicate, label: 'B' }] })
    expect(issues).toContainEqual({ code: 'keyDuplicate', path: 'fields.1.key', fieldIndex: 1 })
    expect(validateWfFormSchema({ version: 1, fields: Array.from({ length: 51 }, (_, index) => field(`f${index}`, 'F', 'text')) }))
      .toContainEqual({ code: 'fieldCountInvalid', path: 'fields' })
    expect(validateWfFormSchema({
      version: 1,
      fields: [{ ...field('time', '时间', 'datetime'), props: { min: '2026-01-01T10:00:00+08:00', max: '2026-01-01T03:00:00Z' } }],
    })).toEqual([])
  })
})

function field(key: string, label: string, type: WfFormField['type']): WfFormField {
  return { ...createWfFormField(type, key), label }
}
