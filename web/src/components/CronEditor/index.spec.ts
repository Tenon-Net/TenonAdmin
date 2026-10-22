import { afterEach, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue'
import CronEditor from './index.vue'
const previewCron = vi.hoisted(() => vi.fn(async () => ({ occurrences: [], normalized: '', everySecond: false })))
vi.mock('@/api', () => ({ jobApi: { previewCron } }))
vi.mock('@/utils/error', () => ({ translateError: String }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('naive-ui', () => {
  const Box = defineComponent({ setup: (_, { slots }) => () => h('div', slots.default?.()) })
  const Input = defineComponent({
    props: ['value'], emits: ['update:value'],
    setup: (props, { emit }) => () => h('input', { value: props.value, onInput: (e: Event) => emit('update:value', (e.target as HTMLInputElement).value) }),
  })
  return { NTabs: Box, NTabPane: Box, NRadioGroup: Box, NRadio: Box, NInputNumber: Box, NSelect: Box, NInput: Input, NAlert: Box }
})
let app: App | undefined
afterEach(() => { app?.unmount(); document.body.replaceChildren(); vi.useRealTimers(); previewCron.mockClear() })
it('双向模型保留未完成输入，并响应父级回填', async () => {
  vi.useFakeTimers()
  const value = ref('0 * * * * ?')
  const host = document.createElement('div'); document.body.append(host)
  app = createApp(defineComponent({ setup: () => () => h(CronEditor, { modelValue: value.value, 'onUpdate:modelValue': (v: string) => { value.value = v } }) }))
  app.mount(host)
  const input = host.querySelector('input')!
  input.value = '0 *'; input.dispatchEvent(new Event('input')); await nextTick()
  expect(value.value).toBe('0 *')
  expect(input.value).toBe('0 *')
  value.value = '0 30 9 * * ?'; await nextTick()
  expect(input.value).toBe('0 30 9 * * ?')
  await vi.advanceTimersByTimeAsync(400)
  expect(previewCron).toHaveBeenLastCalledWith({ cron: value.value, count: 5 })
})
it('卸载会取消尚未执行的预览防抖', async () => {
  vi.useFakeTimers()
  const value = ref('0 * * * * ?')
  app = createApp(defineComponent({ setup: () => () => h(CronEditor, { modelValue: value.value }) }))
  app.mount(document.createElement('div'))
  value.value = '0 30 9 * * ?'; await nextTick()
  app.unmount(); app = undefined
  await vi.advanceTimersByTimeAsync(400)
  expect(previewCron).toHaveBeenCalledTimes(1)
})
