import { afterEach, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App, type PropType, type VNodeChild } from 'vue'
import type { ImportPreview, ImportRow } from '@/types/api'
import ImportWizard, { type ImportWizardApi } from './index.vue'

vi.mock('@/utils/error', () => ({ translateError: String }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('@/components/AppIcon.vue', () => ({ default: defineComponent({ render: () => null }) }))
vi.mock('@/components/DictSelect/index.vue', () => ({ default: defineComponent({ render: () => null }) }))
vi.mock('naive-ui', () => {
  const Box = defineComponent({ setup: (_, { slots }) => () => h('div', [slots.default?.(), slots.footer?.(), slots.trigger?.()]) })
  const Button = defineComponent({
    props: ['loading', 'disabled'],
    setup: (props, { slots }) => () => h('button', { disabled: props.disabled || props.loading }, slots.default?.()),
  })
  const Input = defineComponent({
    props: ['value', 'disabled', 'onUpdateValue'], emits: ['update:value'],
    setup: (props, { emit }) => () => h('input', { value: props.value, disabled: props.disabled, onInput: (e: Event) => { const value = (e.target as HTMLInputElement).value; emit('update:value', value); props.onUpdateValue?.(value) } }),
  })
  const Upload = defineComponent({
    emits: ['update:fileList'],
    setup: (_, { emit }) => () => h('button', { onClick: () => emit('update:fileList', [{ file: new File(['x'], 'test.xlsx') }]) }, 'upload'),
  })
  const Table = defineComponent({
    props: { columns: Array as PropType<{ render?: (row: ImportRow) => VNodeChild }[]>, data: Array as PropType<ImportRow[]> },
    setup: (props) => () => h('div', props.data?.flatMap(row => props.columns?.map(col => col.render?.(row)))),
  })
  return Object.assign(Object.fromEntries(['NModal', 'NSteps', 'NStep', 'NUploadDragger', 'NText', 'NSpace', 'NSelect', 'NSwitch', 'NRadioGroup', 'NRadio', 'NAlert', 'NTooltip', 'NTag'].map(key => [key, Box])), {
    NButton: Button, NInput: Input, NUpload: Upload, NDataTable: Table,
    useMessage: () => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn() }),
  })
})
let app: App | undefined
const flush = async () => { await Promise.resolve(); await nextTick(); await Promise.resolve(); await nextTick() }
afterEach(() => { app?.unmount(); document.body.replaceChildren() })
const preview: ImportPreview = { headers: ['Name'], mapping: { Name: 'name' }, columns: [{ key: 'name', title: 'Name' }], rows: [{ index: 1, cells: { name: 'Alice' }, errors: [] }], total: 1, errorRows: 0, columnErrors: [] }

it('重验期间锁定编辑，返回后恢复编辑并提交新输入', async () => {
  let resolve!: (value: ImportPreview) => void
  const api: ImportWizardApi = {
    downloadTemplate: vi.fn(), preview: vi.fn(async () => structuredClone(preview)),
    validate: vi.fn(() => new Promise<ImportPreview>(done => { resolve = done })),
    commit: vi.fn(async () => ({ total: 1, inserted: 1, updated: 0, skipped: 0, failed: 0, failures: [] })),
    errorReport: vi.fn(),
  }
  const host = document.createElement('div'); document.body.append(host)
  app = createApp(ImportWizard, { api, show: true }); app.mount(host)
  const click = async (label: string) => { Array.from(host.querySelectorAll('button')).find(b => b.textContent?.trim() === label)!.click(); await flush() }
  await click('upload'); await click('import.next'); await click('import.next')
  await click('import.revalidate')
  expect(host.querySelector('input')!.disabled).toBe(true)
  resolve(structuredClone(preview)); await flush()
  const input = host.querySelector('input')!
  expect(input.disabled).toBe(false)
  input.value = 'Bob'; input.dispatchEvent(new Event('input')); await flush()
  await click('import.commit')
  expect(api.commit).toHaveBeenCalledWith(expect.arrayContaining([expect.objectContaining({ cells: { name: 'Bob' } })]), 0)
})
