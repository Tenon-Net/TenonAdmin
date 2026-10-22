import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue'
import ApiSelect from './index.vue'

vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('@/utils/error', () => ({ translateError: (error: Error) => error.message }))
vi.mock('naive-ui', () => ({
  NButton: defineComponent({ setup: (_, { slots }) => () => h('button', slots.default?.()) }),
  NText: defineComponent({ setup: (_, { slots }) => () => h('span', slots.default?.()) }),
  NSelect: defineComponent({
    props: ['options', 'loading'],
    emits: ['search'],
    setup: (props, { emit, slots }) => () => h('div', [
      h('input', { onInput: (e: Event) => emit('search', (e.target as HTMLInputElement).value) }),
      h('output', JSON.stringify(props.options)),
      slots.empty?.(),
    ]),
  }),
}))
let app: App | undefined
const flush = async () => { await Promise.resolve(); await nextTick() }
afterEach(() => { app?.unmount(); document.body.replaceChildren(); vi.useRealTimers() })

function mount(fetch: (keyword: string) => Promise<{ label: string; value: string }[]>) {
  const reloadOn = ref(1)
  const host = document.createElement('div')
  document.body.append(host)
  app = createApp(defineComponent({ setup: () => () => h(ApiSelect, { fetch, reloadOn: reloadOn.value }) }))
  app.mount(host)
  return { host, reloadOn }
}
function search(host: HTMLElement, keyword: string) {
  const input = host.querySelector('input')!
  input.value = keyword
  input.dispatchEvent(new Event('input'))
}

describe('ApiSelect 请求生命周期', () => {
  it('筛选变化会取消旧搜索的防抖任务', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn(async (keyword: string) => [{ label: keyword, value: keyword }])
    const { host, reloadOn } = mount(fetch)
    await flush()
    search(host, 'old')
    reloadOn.value = 2
    await flush()
    await vi.advanceTimersByTimeAsync(300)
    expect(fetch.mock.calls.map(([keyword]) => keyword)).toEqual(['', ''])
  })

  it('输入变化立即使在途响应失效，不等待防抖结束', async () => {
    vi.useFakeTimers()
    let resolve!: (rows: { label: string; value: string }[]) => void
    const fetch = vi.fn(() => new Promise<{ label: string; value: string }[]>((done) => { resolve = done }))
    const { host } = mount(fetch)
    search(host, 'new')
    resolve([{ label: 'stale', value: 'stale' }])
    await flush()
    expect(host.querySelector('output')!.textContent).not.toContain('stale')
  })
})

it('加载失败有明确错误和重试入口', async () => {
  const fetch = vi.fn().mockRejectedValueOnce(new Error('服务不可用')).mockResolvedValueOnce([{ label: 'Alice', value: '1' }])
  const { host } = mount(fetch)
  await flush()
  expect(host.textContent).toContain('服务不可用')
  host.querySelector('button')!.click()
  await flush()
  expect(host.textContent).not.toContain('服务不可用')
  expect(host.querySelector('output')!.textContent).toContain('Alice')
})
