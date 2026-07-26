import { describe, it, expect } from 'vitest'
import '@/locales' // 真 i18n(默认 zh-CN),不 mock
import { ApiError } from '@/api'
import { translateError } from './error'

describe('translateError', () => {
  it('ApiError.msgKey 命中 i18n → 本地化文案', () => {
    const err = new ApiError(40004, 'error.auth.passwordWrong')
    expect(translateError(err)).toBe('账号或密码错误')
  })

  it('数字 ErrorCode(CellError) → 按码查 i18n', () => {
    expect(translateError(46005)).toBe('该单元格为必填项')
    expect(translateError(46001)).toContain('Excel')
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

  it('msgKey 是子树路径且没有 message → 退回 msgKey 本身(ApiError 构造把它当 message)', () => {
    const err = new ApiError(50000, 'error.auth')
    // `not.toContain('returned an object')` 太弱:它耦合 i18next 那句英文 debug 文案的**确切措辞**,
    // 且放过了别的错误输出(回落成空、回落成 debug 变体)。直接钉死落点更结实 ——
    // ApiError 无 message 时把 msgKey 当 message,`te('error.auth')` 为假 → 走 message 分支 → 'error.auth'。
    // 这不是自指:期望值 'error.auth' 是我按构造函数的已知行为写死的字面量,不取自被测代码。
    expect(translateError(err)).toBe('error.auth')
  })
})
