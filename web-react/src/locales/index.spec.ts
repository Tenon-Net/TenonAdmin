import { describe, it, expect } from 'vitest'
import { withExt, type ExtModule, type Messages } from './index'

/**
 * 只测合并规则本身(i18next 接线在 B4 单独测)。
 *
 * `withExt` 把 `mods` 参数化就是为了这里能测 —— 真实的那份来自 `import.meta.glob`,而
 * **`ext/` 目录按设计是空的**(它是消费者的地盘),所以没有任何常驻用例能证明那个 glob 路径打得中:
 * 把 `'./ext/*​/*.ts'` 写错成 `'./extt/...'`,下面这些用例一条都不会红。**这个判据缺口是已知且未堵的**,
 * 曾用临时 fixture 当场验过一次接缝确实生效,验完删除。
 */
const mod = (o: Record<string, unknown>): ExtModule => ({ default: o })

const BASE: Messages = {
  error: { auth: { passwordWrong: '密码错误', captchaExpired: '验证码过期' } },
  common: { confirm: '确定' },
}

describe('withExt 合并规则', () => {
  it('深合并:往已有子树补键,不顶掉兄弟键', () => {
    // 这是**唯一**能区分深/浅合并的形状 —— ext 的键必须与内置键**碰撞**才观察得到差别。
    // 浅合并会把整个 auth 子树连同 captchaExpired 一起换掉,而那只在真报那个错时才暴露。
    const out = withExt(BASE, { './ext/zh-CN/error.ts': mod({ auth: { tokenExpired: '登录已过期' } }) }, 'zh-CN')
    expect(out.error).toEqual({
      auth: { passwordWrong: '密码错误', captchaExpired: '验证码过期', tokenExpired: '登录已过期' },
    })
  })

  it('同名标量键:ext 侧胜出(覆写内置文案)', () => {
    const out = withExt(BASE, { './ext/zh-CN/common.ts': mod({ confirm: '好的' }) }, 'zh-CN')
    expect(out.common).toEqual({ confirm: '好的' })
  })

  it('新命名空间:文件名即顶层键', () => {
    const out = withExt(BASE, { './ext/zh-CN/doc.ts': mod({ title: '文档' }) }, 'zh-CN')
    expect(out.doc).toEqual({ title: '文档' })
    expect(out.common).toEqual({ confirm: '确定' }) // 不影响内置
  })

  it('按 locale 前缀筛:中文的扩展不会漏进英文', () => {
    const mods = {
      './ext/zh-CN/doc.ts': mod({ title: '文档' }),
      './ext/en-US/doc.ts': mod({ title: 'Docs' }),
    }
    expect(withExt(BASE, mods, 'zh-CN').doc).toEqual({ title: '文档' })
    expect(withExt(BASE, mods, 'en-US').doc).toEqual({ title: 'Docs' })
  })

  it('不改动入参(BASE 是模块级共享的,被就地改过会串到别的用例)', () => {
    withExt(BASE, { './ext/zh-CN/error.ts': mod({ auth: { tokenExpired: 'x' } }) }, 'zh-CN')
    expect(BASE.error).toEqual({ auth: { passwordWrong: '密码错误', captchaExpired: '验证码过期' } })
  })

  it('数组不当作对象往下钻(否则会按下标逐项合并出四不像)', () => {
    const base: Messages = { m: { tags: ['a', 'b', 'c'] } }
    const out = withExt(base, { './ext/zh-CN/m.ts': mod({ tags: ['x'] }) }, 'zh-CN')
    expect(out.m).toEqual({ tags: ['x'] })
  })
})
