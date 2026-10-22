import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api', () => ({ authApi: { captcha: vi.fn() }, ApiError: class extends Error {} }))

import { authApi } from '@/api'
import { useLoginCaptcha } from './useLoginCaptcha'

const captchaMock = vi.mocked(authApi.captcha)

beforeEach(() => vi.clearAllMocks())

describe('useLoginCaptcha', () => {
  it('加载失败清掉旧票据并暴露可重试错误，成功重试后才恢复 ready', async () => {
    const captcha = useLoginCaptcha()
    captchaMock.mockRejectedValueOnce(new Error('captcha offline'))

    await captcha.configure(true)

    expect(captcha.ready.value).toBe(false)
    expect(captcha.error.value).toBe('captcha offline')
    expect(captcha.requestFields()).toEqual({ captchaId: '', captchaCode: '' })

    captchaMock.mockResolvedValueOnce({ captchaId: 'cap-2', svg: '<svg/>', type: 'char' })
    await captcha.load()

    expect(captcha.ready.value).toBe(true)
    expect(captcha.error.value).toBe('')
    expect(captcha.requestFields()).toEqual({ captchaId: 'cap-2', captchaCode: '' })
  })

  it('一次性票据消费后清输入并换新票据', async () => {
    const captcha = useLoginCaptcha()
    captchaMock
      .mockResolvedValueOnce({ captchaId: 'cap-1', svg: '<svg/>', type: 'char' })
      .mockResolvedValueOnce({ captchaId: 'cap-2', svg: '<svg/>', type: 'math' })
    await captcha.configure(true)
    captcha.captchaCode.value = '1234'

    await captcha.refreshAfterUse()

    expect(captcha.captchaCode.value).toBe('')
    expect(captcha.requestFields()).toEqual({ captchaId: 'cap-2', captchaCode: '' })
  })

  it('禁用时使在途响应失效，不让旧验证码重新写回', async () => {
    let resolveRequest!: (value: { captchaId: string; svg: string; type: string }) => void
    captchaMock.mockReturnValueOnce(new Promise((resolve) => { resolveRequest = resolve }))
    const captcha = useLoginCaptcha()
    const configuring = captcha.configure(true)

    await captcha.configure(false)
    resolveRequest({ captchaId: 'stale', svg: '<svg/>', type: 'char' })
    await configuring

    expect(captcha.enabled.value).toBe(false)
    expect(captcha.ready.value).toBe(true)
    expect(captcha.captchaId.value).toBe('')
    expect(captcha.captchaSvg.value).toBe('')
  })
})
