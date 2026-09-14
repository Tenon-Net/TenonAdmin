import { describe, expect, it } from 'vitest'
import { createWfFormField } from './formSchema'
import {
  parseWfFormValues,
  parseWfFormValuesResult,
  mergeWfFormPermissions,
  serializeWfFormValues,
  validateWfFormValues,
  type WfFormValues,
} from './formRuntime'
import type { WfFormField, WfFormSchema } from './schema'

describe('workflow form runtime values', () => {
  it('round-trips typed values and rejects non-object JSON roots', () => {
    const values: WfFormValues = {
      amount: 12.5,
      when: '2026-09-09T10:00:00+08:00',
      users: [7, 8],
      empty: null,
    }
    const json = serializeWfFormValues(values)

    expect(parseWfFormValues(json)).toEqual(values)
    expect(parseWfFormValues('[]')).toEqual({})
    expect(parseWfFormValues('{bad')).toEqual({})
    expect(parseWfFormValuesResult('{bad')).toEqual({ values: {}, error: 'invalidJson' })
    expect(parseWfFormValuesResult('[]')).toEqual({ values: {}, error: 'objectRequired' })
    expect(serializeWfFormValues({ missing: undefined })).toBeNull()
  })

  it('validates all ten control value shapes and their boundaries', () => {
    const schema: WfFormSchema = {
      version: 1,
      fields: [
        field('text', 'subject', { maxLength: 10 }),
        field('textarea', 'detail', { maxLength: 20, rows: 4 }),
        field('number', 'count', { min: 1, max: 10, precision: 2 }),
        field('money', 'amount', { min: 0, max: 100 }),
        field('date', 'day', { min: '2026-01-01', max: '2026-12-31' }),
        field('datetime', 'when', { min: '2026-01-01T00:00:00Z', max: '2026-12-31T23:59:59Z' }),
        field('select', 'kind', { options: [{ label: 'A', value: 'a' }] }),
        field('multiSelect', 'tags', { options: [{ label: 'A', value: 'a' }, { label: 'B', value: 'b' }], maxSelected: 2 }),
        field('user', 'users', { multiple: true, maxSelected: 2 }),
        field('attachment', 'files', { multiple: true, maxCount: 2 }),
      ],
    }
    const values: WfFormValues = {
      subject: 'hello', detail: 'body', count: 2.5, amount: 10.25,
      day: '2026-09-09', when: '2026-09-09T10:00:00+08:00', kind: 'a', tags: ['a', 'b'],
      users: [7, '8'], files: [101, 102],
    }

    expect(validateWfFormValues(schema, values)).toEqual([])
    expect(validateWfFormValues(schema, { ...values, amount: 10.256 })).toContainEqual({ key: 'amount', code: 'valueInvalid' })
    expect(validateWfFormValues(schema, { ...values, tags: ['a', 'a'] })).toContainEqual({ key: 'tags', code: 'optionInvalid' })
    expect(validateWfFormValues(schema, { ...values, users: [0] })).toContainEqual({ key: 'users', code: 'userInvalid' })
    expect(validateWfFormValues(schema, { ...values, files: [101, 102, 103] })).toContainEqual({ key: 'files', code: 'attachmentInvalid' })
  })

  it('reports required fields before type checks', () => {
    const schema: WfFormSchema = { version: 1, fields: [field('text', 'name', undefined, true)] }
    expect(validateWfFormValues(schema, {})).toEqual([{ key: 'name', code: 'required' }])
  })

  it('merges parallel permissions from most restrictive to least restrictive', () => {
    expect(mergeWfFormPermissions(
      [{ field: 'secret', access: 'editable' }, { field: 'readonly', access: 'readonly' }],
      [{ field: 'secret', access: 'hidden' }, { field: 'readonly', access: 'editable' }],
    )).toEqual([
      { field: 'secret', access: 'hidden' },
      { field: 'readonly', access: 'readonly' },
    ])
  })
})

function field(
  type: WfFormField['type'],
  key: string,
  props?: WfFormField['props'],
  required = false,
): WfFormField {
  return { ...createWfFormField(type, key), label: key, required, props } as WfFormField
}
