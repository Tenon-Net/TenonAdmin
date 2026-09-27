import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'
import zhCN from '@/locales/zh-CN'

const { bindStart } = vi.hoisted(() => ({
  bindStart: vi.fn(),
}))

vi.mock('naive-ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('naive-ui')>()
  return {
    ...actual,
    NQrCode: defineComponent({
      props: { value: String },
      setup: (props) => () => h('div', { 'data-testid': 'mfa-qr', 'data-uri': props.value }),
    }),
    useMessage: () => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn() }),
  }
})
vi.mock('vue-router', () => ({
  useRoute: () => ({ query: { account: 'alice' } }),
  useRouter: () => ({ replace: vi.fn() }),
}))
vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    mfaApi: { bindStart, bindComplete: vi.fn(), recovery: vi.fn(), clear: vi.fn() },
  }
})
vi.mock('@/stores/user', () => ({ useUserStore: () => ({ userInfo: null, clear: vi.fn() }) }))
vi.mock('@/stores/auth', () => ({ useAuthStore: () => ({ reset: vi.fn() }) }))
vi.mock('@/router', () => ({ resetRouter: vi.fn() }))

import MfaPage from './index.vue'

let app: App<Element> | undefined

afterEach(() => {
  app?.unmount()
  app = undefined
  document.body.replaceChildren()
  bindStart.mockReset()
})

async function mountPage() {
  const host = document.createElement('div')
  document.body.append(host)
  app = createApp(MfaPage)
  app.use(createI18n({ legacy: false, locale: 'zh-CN', messages: { 'zh-CN': zhCN } }))
  app.mount(host)
  await nextTick()
  return host
}

async function fillPasswordAndStart(host: HTMLElement, password = 'secret') {
  const pwd = host.querySelector('input[type="password"]') as HTMLInputElement | null
  expect(pwd).toBeTruthy()
  pwd!.value = password
  pwd!.dispatchEvent(new Event('input', { bubbles: true }))
  await nextTick()
  const start = [...host.querySelectorAll('button')].find((b) => b.textContent?.includes('开始设置'))
  expect(start).toBeTruthy()
  ;(start as HTMLButtonElement).click()
  await Promise.resolve()
  await nextTick()
  await nextTick()
}

describe('MFA bind page', () => {
  it('shows the returned seed in a labeled readonly field after bindStart succeeds', async () => {
    bindStart.mockResolvedValueOnce({
      bindChallengeId: 'chal-1',
      otpauthUri: 'otpauth://totp/Tenon:alice?secret=JBSWY3DPEHPK3PXP',
      seed: 'JBSWY3DPEHPK3PXP',
    })
    const host = await mountPage()
    await fillPasswordAndStart(host)

    expect(bindStart).toHaveBeenCalledWith({ account: 'alice', currentPassword: 'secret' })
    expect([...host.querySelectorAll('button')].some((b) => b.textContent?.includes('完成设置'))).toBe(true)
    const seed = host.querySelector('.mfa-seed input') as HTMLInputElement | null
    expect(seed).toBeTruthy()
    expect(seed!.readOnly).toBe(true)
    expect(seed!.value).toBe('JBSWY3DPEHPK3PXP')
    expect(host.querySelector('.mfa-totp-code input')).toBeTruthy()
    expect(host.querySelector('.mfa-seed')).toBeTruthy()
  })

  it('stays on the password step and does not render a seed when bindStart fails', async () => {
    bindStart.mockRejectedValueOnce(new Error('no totp'))
    const host = await mountPage()
    await fillPasswordAndStart(host)

    expect(host.querySelector('.mfa-seed')).toBeNull()
    expect([...host.querySelectorAll('button')].some((b) => b.textContent?.includes('开始设置'))).toBe(true)
    expect([...host.querySelectorAll('button')].some((b) => b.textContent?.includes('完成设置'))).toBe(false)
  })
})
