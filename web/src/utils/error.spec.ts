import { describe, it, expect } from 'vitest'
import '@/locales' // 真 i18n(默认 zh-CN),不 mock
import { ApiError } from '@/api'
import { translateError } from './error'

describe('translateError', () => {
  it('ApiError.msgKey 命中 i18n → 本地化文案', () => {
    const err = new ApiError(40004, 'error.auth.passwordWrong')
    expect(translateError(err)).toBe('账号或密码错误')
  })

  it('msgKey 未命中但有 message → message,普通 Error → message', () => {
    const withUnknownKey = new ApiError(99999, 'error.does.not.exist', undefined, 'fallback message text')
    expect(translateError(withUnknownKey)).toBe('fallback message text')
    expect(translateError(new Error('plain error message'))).toBe('plain error message')
  })

  it('未知值 → 兜底键', () => {
    expect(translateError(null)).toBe('操作失败,请稍后重试')
    expect(translateError('random string')).toBe('操作失败,请稍后重试')
  })
})
