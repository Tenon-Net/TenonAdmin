import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import { ApiError, fileApi } from '@/api'
import zhCN from '@/locales/zh-CN'
import { createWfFormField } from '@/workflow/formSchema'
import type { WfFormField, WfFormSchema } from '@/workflow/schema'
import WfBuiltinForm from './WfBuiltinForm.vue'

const messageError = vi.hoisted(() => vi.fn())
const messageWarning = vi.hoisted(() => vi.fn())
const triggerBlobDownload = vi.hoisted(() => vi.fn())

vi.mock('naive-ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('naive-ui')>()
  return {
    ...actual,
    useMessage: () => ({ error: messageError, warning: messageWarning, success: vi.fn() }),
  }
})
vi.mock('@/utils/download', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/utils/download')>()
  return { ...actual, triggerBlobDownload }
})

vi.mock('@/components/UserSelect/index.vue', () => ({
  default: defineComponent({ render: () => h('div', { 'data-testid': 'user-select' }) }),
}))
/** 上传桩回什么 Id 由用例定:后端 long 雪花在 JSON 里既可能是 number 也可能是十进制 string。 */
let uploadedId: number | string = 12
let uploadedFileSize = 1
vi.mock('@/components/FileUpload/index.vue', () => ({
  default: defineComponent({
    props: { max: Number, onBeforeUpload: Function },
    emits: ['uploaded'],
    setup: (props, { emit }) => () => h('button', {
      'data-testid': 'file-upload',
      'data-max': String(props.max ?? ''),
      onClick: () => {
        const file = new File([new Uint8Array(uploadedFileSize)], 'attachment.bin')
        const allowed = props.onBeforeUpload?.({ file: { file } })
        if (allowed instanceof Promise) {
          void allowed.then((result) => { if (result !== false) emit('uploaded', { id: uploadedId }) })
        } else if (allowed !== false) {
          emit('uploaded', { id: uploadedId })
        }
      },
    }, 'upload'),
  }),
}))

let app: App<Element> | undefined
afterEach(() => {
  app?.unmount()
  app = undefined
  uploadedId = 12
  uploadedFileSize = 1
  messageError.mockClear()
  messageWarning.mockClear()
  triggerBlobDownload.mockClear()
  vi.restoreAllMocks()
  document.body.replaceChildren()
})

describe('WfBuiltinForm', () => {
  it('renders all ten controls and validates start values', async () => {
    const schema: WfFormSchema = {
      version: 1,
      fields: [
        field('text', 'subject', { maxLength: 20 }, true),
        field('textarea', 'detail', { rows: 3 }),
        field('number', 'count', { precision: 0 }),
        field('money', 'amount'),
        field('date', 'day'),
        field('datetime', 'when'),
        field('select', 'kind', { options: [{ label: 'A', value: 'a' }] }),
        field('multiSelect', 'tags', { options: [{ label: 'A', value: 'a' }], maxSelected: 1 }),
        field('user', 'user', { multiple: false }),
        field('attachment', 'file', { multiple: false, maxCount: 1 }),
      ],
    }
    const values = ref<Record<string, unknown>>({
      subject: 'Subject', detail: 'Details', count: 1, amount: 12.5,
      day: '2026-09-09', when: '2026-09-09T10:00:00+08:00', kind: 'a', tags: ['a'], user: 7, file: 9,
    })
    const runtime = ref<{ validate: () => boolean } | null>(null)
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfBuiltinForm, {
        ref: runtime,
        schema,
        modelValue: values.value,
        mode: 'start',
        'onUpdate:modelValue': (next: Record<string, unknown>) => { values.value = next },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)
    await nextTick()

    expect(host.querySelectorAll('.n-form-item').length).toBe(10)
    expect(host.querySelector('[data-testid="user-select"]')).toBeTruthy()
    expect(host.querySelector('[data-testid="file-upload"]')).toBeNull()
    expect(host.textContent).toContain('9')
    expect(runtime.value?.validate()).toBe(true)

    values.value = { ...values.value, subject: '' }
    await nextTick()
    expect(runtime.value?.validate()).toBe(false)
    await nextTick()
    expect(host.textContent).toContain('请填写此字段')
  })

  it('applies approval permissions and defaults missing permissions to editable', async () => {
    const schema: WfFormSchema = {
      version: 1,
      fields: [
        field('text', 'hidden', undefined, true),
        field('text', 'readonly', undefined, true),
        field('text', 'editable', undefined, true),
      ],
    }
    const values = ref<Record<string, unknown>>({})
    const runtime = ref<{ validate: () => boolean } | null>(null)
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfBuiltinForm, {
        ref: runtime,
        schema,
        modelValue: values.value,
        mode: 'approve',
        permissions: [
          { field: 'hidden', access: 'hidden' },
          { field: 'readonly', access: 'readonly' },
        ],
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)
    await nextTick()

    const inputs = host.querySelectorAll<HTMLInputElement>('input')
    expect(host.querySelectorAll('.n-form-item')).toHaveLength(2)
    expect(inputs[0]?.disabled).toBe(true)
    expect(inputs[1]?.disabled).toBe(false)
    expect(runtime.value?.validate()).toBe(false)

    values.value = { editable: 'ok' }
    await nextTick()
    expect(runtime.value?.validate()).toBe(true)
  })

  it('shows editable attachment ids, limits uploads to remaining slots, and removes ids', async () => {
    const schema: WfFormSchema = {
      version: 1,
      fields: [field('attachment', 'files', { multiple: true, maxCount: 2 })],
    }
    const values = ref<Record<string, unknown>>({ files: [11] })
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfBuiltinForm, {
        schema,
        modelValue: values.value,
        mode: 'approve',
        permissions: [{ field: 'files', access: 'editable' }],
        'onUpdate:modelValue': (next: Record<string, unknown>) => { values.value = next },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)
    await nextTick()

    expect(host.textContent).toContain('11')
    expect(host.querySelector('[data-testid="file-upload"]')?.getAttribute('data-max')).toBe('1')

    host.querySelector<HTMLButtonElement>('[data-testid="file-upload"]')!.click()
    await nextTick()
    expect(values.value).toEqual({ files: [11, 12] })
    expect(host.querySelector('[data-testid="file-upload"]')).toBeNull()

    host.querySelector<HTMLButtonElement>('.n-tag__close')!.click()
    await nextTick()
    expect(values.value).toEqual({ files: [12] })
    expect(host.textContent).toContain('12')
    expect(host.querySelector('[data-testid="file-upload"]')?.getAttribute('data-max')).toBe('1')
  })

  it('keeps a decimal-string snowflake attachment id intact instead of coercing it', async () => {
    uploadedId = '1500000000000000001'
    const schema: WfFormSchema = {
      version: 1,
      fields: [field('attachment', 'file', { multiple: false, maxCount: 1 })],
    }
    const values = ref<Record<string, unknown>>({})
    const runtime = ref<{ validate: () => boolean } | null>(null)
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfBuiltinForm, {
        ref: runtime,
        schema,
        modelValue: values.value,
        mode: 'start',
        'onUpdate:modelValue': (next: Record<string, unknown>) => { values.value = next },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)
    await nextTick()

    host.querySelector<HTMLButtonElement>('[data-testid="file-upload"]')!.click()
    await nextTick()
    expect(values.value).toEqual({ file: '1500000000000000001' })
    expect(host.textContent).toContain('1500000000000000001')
    expect(runtime.value?.validate()).toBe(true)
  })

  it('rejects an attachment over the field limit before upload', async () => {
    uploadedFileSize = 1024 * 1024 + 1
    const schema: WfFormSchema = {
      version: 1,
      fields: [field('attachment', 'file', { maxSizeMb: 1 })],
    }
    const values = ref<Record<string, unknown>>({})
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfBuiltinForm, {
        schema,
        modelValue: values.value,
        mode: 'start',
        'onUpdate:modelValue': (next: Record<string, unknown>) => { values.value = next },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)

    host.querySelector<HTMLButtonElement>('[data-testid="file-upload"]')!.click()
    await nextTick()
    await nextTick()

    expect(values.value).toEqual({})
    expect(messageError).toHaveBeenCalledWith('文件超出大小限制')
  })

  it('clears a single attachment with an explicit null value', async () => {
    const schema: WfFormSchema = {
      version: 1,
      fields: [field('attachment', 'file', { multiple: false, maxCount: 1 })],
    }
    const values = ref<Record<string, unknown>>({ file: 11 })
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfBuiltinForm, {
        schema,
        modelValue: values.value,
        mode: 'approve',
        permissions: [{ field: 'file', access: 'editable' }],
        'onUpdate:modelValue': (next: Record<string, unknown>) => { values.value = next },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)
    await nextTick()

    host.querySelector<HTMLButtonElement>('.n-tag__close')!.click()
    await nextTick()
    expect(values.value).toEqual({ file: null })
  })

  it('clears date values with an explicit null value', async () => {
    const schema: WfFormSchema = {
      version: 1,
      fields: [field('date', 'day'), field('datetime', 'when')],
    }
    const values = ref<Record<string, unknown>>({
      day: '2026-09-09',
      when: '2026-09-09T10:00:00+08:00',
    })
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfBuiltinForm, {
        schema,
        modelValue: values.value,
        mode: 'start',
        'onUpdate:modelValue': (next: Record<string, unknown>) => { values.value = next },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)
    await nextTick()

    host.querySelectorAll<HTMLElement>('.n-input').forEach((input) => {
      input.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }))
    })
    await nextTick()
    const clearButtons = host.querySelectorAll<HTMLElement>('[data-clear]')
    expect(clearButtons).toHaveLength(2)
    clearButtons[0]!.click()
    await nextTick()
    expect(values.value).toEqual({ day: null, when: '2026-09-09T10:00:00+08:00' })

    clearButtons[1]!.click()
    await nextTick()
    expect(values.value).toEqual({ day: null, when: null })
  })

  it('keeps view mode readonly when permissions are missing', async () => {
    const schema: WfFormSchema = { version: 1, fields: [field('text', 'subject')] }
    const values = ref<Record<string, unknown>>({ subject: 'original' })
    const host = document.createElement('div')
    document.body.append(host)
    app = createApp(defineComponent({
      setup: () => () => h(WfBuiltinForm, {
        schema,
        modelValue: values.value,
        mode: 'view',
        permissions: null,
        'onUpdate:modelValue': (next: Record<string, unknown>) => { values.value = next },
      }),
    }))
    app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
    app.mount(host)
    await nextTick()

    const input = host.querySelector<HTMLInputElement>('input')!
    expect(input.disabled).toBe(true)

    input.value = 'changed'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await nextTick()
    expect(values.value).toEqual({ subject: 'original' })
  })

  it('reports a forbidden download without dropping the attachment id', async () => {
    vi.spyOn(fileApi, 'fetchDownload').mockRejectedValue(new ApiError(41001, 'error.perm.denied', undefined, 'denied'))
    const host = mountAttachment('11')
    clickText(host, '下载')
    await nextTick()
    await nextTick()
    expect(host.textContent).toContain('无权限访问')
    expect(host.textContent).toContain('11')
    expect(messageError).toHaveBeenCalledWith('无权限访问')
  })

  it('keeps a single in-flight download and saves the server file name', async () => {
    let release: (value: { blob: Blob; fileName: string; contentType: string }) => void = () => {}
    const blob = new Blob(['pdf'], { type: 'application/pdf' })
    const fetchDownload = vi.spyOn(fileApi, 'fetchDownload').mockImplementation(
      () => new Promise((resolve) => { release = resolve }),
    )
    const host = mountAttachment('11')
    clickText(host, '下载')
    clickText(host, '下载')
    await nextTick()
    expect(fetchDownload).toHaveBeenCalledTimes(1)
    expect(fetchDownload).toHaveBeenCalledWith('11')
    release({ blob, fileName: '请假.pdf', contentType: 'application/pdf' })
    await nextTick()
    await nextTick()
    expect(triggerBlobDownload).toHaveBeenCalledWith(blob, '请假.pdf')
  })

  it('explains when a file cannot be previewed instead of rendering it', async () => {
    vi.spyOn(fileApi, 'fetchDownload').mockResolvedValue({
      blob: new Blob(['PK'], { type: 'application/zip' }),
      fileName: 'leave.zip',
      contentType: 'application/zip',
    })
    const host = mountAttachment(9)
    clickText(host, '预览')
    await nextTick()
    await nextTick()
    expect(host.textContent).toContain('此类型不能在线预览，请下载后查看')
    expect(triggerBlobDownload).not.toHaveBeenCalled()
    expect(document.body.querySelector('iframe, img.wf-attachment-preview')).toBeNull()
  })
})

function mountAttachment(id: number | string) {
  const host = document.createElement('div')
  document.body.append(host)
  const schema: WfFormSchema = {
    version: 1,
    fields: [field('attachment', 'file', { multiple: false, maxCount: 1 })],
  }
  app = createApp(defineComponent({
    setup: () => () => h(WfBuiltinForm, {
      schema,
      modelValue: { file: id },
      mode: 'view',
    }),
  }))
  app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
  app.mount(host)
  return host
}

function clickText(host: ParentNode, text: string) {
  const button = Array.from(host.querySelectorAll('button')).find((item) => item.textContent?.includes(text))
  expect(button).toBeTruthy()
  button!.dispatchEvent(new MouseEvent('click', { bubbles: true }))
}

function field(
  type: WfFormField['type'],
  key: string,
  props?: WfFormField['props'],
  required = false,
): WfFormField {
  return { ...createWfFormField(type, key), label: key, required, props } as WfFormField
}
