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

  /**
   * **这条是 B4 里 `te()` 存在的全部理由**,Vue 侧没有对应用例(那边 `te` 是 vue-i18n 自带的,
   * 天然就是这个语义,没人需要证明它)。这边 `te()` 是手写的,所以要有条用例守着。
   *
   * 判别值:`error.auth` 是**子树**。`i18n.exists()` 对它说 true,而 `t()` 返回
   * `"key 'error.auth (zh-CN)' returned an object instead of string."` —— 一句英文 debug 文本。
   * 用 `exists()` 的话,后端发来一个恰好是子树路径的 msgKey 就会把那句话弹给用户。
   *
   * 变异:把 `locales/index.ts` 的 `te()` 换回裸 `i18n.exists(key)`,这条必红。
   */
  it('msgKey 是子树路径 → 退回后端原文,不把 i18next 的 debug 文本当文案', () => {
    const err = new ApiError(50000, 'error.auth', undefined, '服务端给的原文')
    expect(translateError(err)).toBe('服务端给的原文')
    expect(translateError(err)).not.toContain('returned an object')
  })

  it('msgKey 是子树路径且没有 message → 通用兜底(仍然不是 debug 文本)', () => {
    const err = new ApiError(50000, 'error.auth')
    // ApiError 的构造在没有 message 时把 msgKey 当 message 用,所以这里落到 message 分支。
    // 断言的重点是**不含 debug 文本**;具体落哪个分支是实现细节。
    expect(translateError(err)).not.toContain('returned an object')
  })
})
