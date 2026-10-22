import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, type App } from 'vue'

vi.mock('@/api', () => ({
  authApi: {
    smsLoginSend: vi.fn(),
    smsChallengeResend: vi.fn(),
    smsLogin: vi.fn(),
    smsChallengeLogin: vi.fn(),
  },
}))

import { authApi } from '@/api'
import { useLoginSms } from './useLoginSms'

const sendMock = vi.mocked(authApi.smsLoginSend)
const resendMock = vi.mocked(authApi.smsChallengeResend)
let app: App<Element> | undefined

beforeEach(() => {
  vi.useFakeTimers()
  vi.clearAllMocks()
})

afterEach(() => {
  app?.unmount()
  app = undefined
  vi.useRealTimers()
})

describe('useLoginSms 生命周期', () => {
  it.each([
    ['短信登录发码', 'send'] as const,
    ['短信挑战重发', 'resend'] as const,
  ])('%s 在途时卸载，不会在响应迟到后新建倒计时', async (_name, operation) => {
    let resolveRequest!: (value: { expiresSeconds: number; resendSeconds: number }) => void
    const pending = new Promise<{ expiresSeconds: number; resendSeconds: number }>((resolve) => {
      resolveRequest = resolve
    })
    if (operation === 'send') sendMock.mockReturnValueOnce(pending)
    else resendMock.mockReturnValueOnce(pending)

    let sms!: ReturnType<typeof useLoginSms>
    const Host = defineComponent(() => {
      sms = useLoginSms()
      return () => h('div')
    })
    app = createApp(Host)
    app.mount(document.createElement('div'))
    const intervalSpy = vi.spyOn(window, 'setInterval')
    sms.smsModel.phone = '13800000000'
    sms.challenge.challengeId = 'challenge-1'
    const request = operation === 'send'
      ? sms.sendLoginCode({ captchaId: 'captcha-1', captchaCode: '1234' })
      : sms.resendChallenge()

    app.unmount()
    app = undefined
    resolveRequest({ expiresSeconds: 300, resendSeconds: 60 })
    await request
    await vi.advanceTimersByTimeAsync(1000)

    expect(sms.countdown.value).toBe(0)
    expect(intervalSpy).not.toHaveBeenCalled()
  })
})
