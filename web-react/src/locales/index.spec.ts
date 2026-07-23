import { describe, it, expect, vi } from 'vitest'
import { withExt, type ExtModule, type Messages } from './index'

/**
 * 只测合并规则本身(i18next 接线在 B4 单独测)。
 *
 * 与 `web/src/locales/index.spec.ts` 的 8 条是**有意的平行实现**:两个模板各自自包含,合并逻辑
 * 各存一份,用例也各存一份。改这里的语义时顺手看一眼 Vue 侧要不要跟,但不建同步闸门。
 *
 * **glob 判据缺口(部分已堵)**:`withExt` 把 `mods` 参数化就是为了这里能测,而真实的那份来自
 * `import.meta.glob`,`ext/` 目录按设计是空的(消费者的地盘)。下面那条探针用**已提交的 README.md**
 * 钉住了 `./ext/` 这一段路径 —— 写成 `'./extt/'`、`'../ext/'` 或目录被挪走都会红。
 * **钉不住的是 `*​/*.ts` 那一段的深度与扩展名**:写成 `./ext/*.ts` 或 `./ext/**​/*.ts` 探针照样绿。
 * 这一半是结构性的:一个「按设计为空」的目录,空结果与坏 glob 在运行时不可区分。
 */
const mod = (o: Record<string, unknown>): ExtModule => ({ default: o })

const BASE: Messages = {
  error: { auth: { passwordWrong: '密码错误', captchaExpired: '验证码过期' } },
  common: { confirm: '确定' },
}

describe('withExt 合并规则', () => {
  it('没有 ext 文件时原样返回(消费者什么都不放是常态)', () => {
    expect(withExt(BASE, {}, 'zh-CN')).toEqual(BASE)
  })

  it('消费者忘写 export default 时跳过并点名,不炸整站', () => {
    // 这是唯一面向外部输入的分支。glob 是 eager 的、在模块顶层求值,所以没有守卫的话
    // `deepMerge(x, undefined)` 抛在 import 链上 = 白屏,而报错信息指不到是哪个文件写错。
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const bad = { './ext/zh-CN/oops.ts': { notDefault: { a: 1 } } as unknown as ExtModule }
    expect(() => withExt(BASE, bad, 'zh-CN')).not.toThrow()
    expect(withExt(BASE, bad, 'zh-CN')).toEqual(BASE)
    expect(warn.mock.calls.flat().join(' ')).toContain('./ext/zh-CN/oops.ts') // 必须点名到文件
    warn.mockRestore()
  })

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

describe('glob 接线', () => {
  // 用已提交的 README.md 当锚:它就在 ext/ 下,所以能钉住 `./ext/` 这一段路径写没写错。
  // 钉不住深度与扩展名 —— 见文件顶部说明。
  const probe = import.meta.glob('./ext/**/*.md', { eager: true, query: '?raw' })
  it('ext/ 目录路径本身没写错', () => {
    expect(Object.keys(probe)).toContain('./ext/README.md')
  })
})
