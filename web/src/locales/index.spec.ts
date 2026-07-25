import { describe, expect, it } from 'vitest'
import { withExt } from './index'

const mod = (keys: Record<string, unknown>) => ({ default: keys })

describe('withExt', () => {
  // 形状照内置真实结构:error 是嵌套语义键(后端 msgKey = error.dict.typeNotFound),不是扁平数字码。
  const base = {
    user: { title: '用户', name: '姓名' },
    error: {
      _fallback: '操作失败',
      auth: { passwordWrong: '账号或密码错误', captchaExpired: '验证码已过期' },
    },
  }

  it('无 ext 文件时原样返回', () => {
    expect(withExt(base, {}, 'zh-CN')).toEqual(base)
  })

  it('新命名空间按文件名并入', () => {
    const out = withExt(base, { './ext/zh-CN/sampleDoc.ts': mod({ title: '标题' }) }, 'zh-CN')
    expect(out.sampleDoc).toEqual({ title: '标题' })
    expect(out.user).toEqual(base.user)
  })

  // 消费者给自己的错误码加文案是文档里明写的常规操作,写法是嵌套的 { doc: { titleDuplicated } }
  // ——必须并进内置 error,不能把 _fallback / auth 顶掉。
  it('自定义错误码并入内置 error,内置键存活', () => {
    const out = withExt(base, { './ext/zh-CN/error.ts': mod({ doc: { titleDuplicated: '文档标题重复' } }) }, 'zh-CN')
    expect(out.error).toEqual({
      _fallback: '操作失败',
      auth: { passwordWrong: '账号或密码错误', captchaExpired: '验证码已过期' },
      doc: { titleDuplicated: '文档标题重复' },
    })
  })

  // 深层覆写:改写内置一条错误文案,不能连坐同一子树的其他键(浅合并会在这里静默抹掉 captchaExpired)。
  it('深层覆写只动那一个键,兄弟键不连坐', () => {
    const out = withExt(base, { './ext/zh-CN/error.ts': mod({ auth: { passwordWrong: '账号或密码不正确' } }) }, 'zh-CN')
    expect(out.error.auth).toEqual({ passwordWrong: '账号或密码不正确', captchaExpired: '验证码已过期' })
    expect(out.error._fallback).toBe('操作失败')
  })

  it('同键时 ext 覆盖内置', () => {
    const out = withExt(base, { './ext/zh-CN/user.ts': mod({ title: '员工' }) }, 'zh-CN')
    expect(out.user).toEqual({ title: '员工', name: '姓名' })
  })

  it('对象覆盖字符串时 ext 侧胜出,不炸', () => {
    const out = withExt(base, { './ext/zh-CN/user.ts': mod({ title: { a: 'x' } }) }, 'zh-CN')
    expect(out.user.title).toEqual({ a: 'x' })
  })

  it('只吃当前 locale 的文件', () => {
    const mods = {
      './ext/zh-CN/sampleDoc.ts': mod({ title: '标题' }),
      './ext/en-US/sampleDoc.ts': mod({ title: 'Title' }),
    }
    expect(withExt(base, mods, 'en-US').sampleDoc).toEqual({ title: 'Title' })
    expect(withExt(base, mods, 'zh-CN').sampleDoc).toEqual({ title: '标题' })
  })

  it('不修改传入的 base(含深层)', () => {
    withExt(base, { './ext/zh-CN/error.ts': mod({ auth: { passwordWrong: 'x' } }) }, 'zh-CN')
    expect(base.error.auth).toEqual({ passwordWrong: '账号或密码错误', captchaExpired: '验证码已过期' })
  })

  // 数组不是"普通对象",不能往下钻:isPlainObject 一旦漏掉 !Array.isArray(v),
  // ['a','b','c'] 与 ['x'] 会按下标逐项合并成 ['x','b','c'] —— 而上面 8 条一条都不红(实测)。
  // 反向 port 自 web-react/src/locales/index.spec.ts:73:deepMerge 两边逐字相同(自包含之后是有意重复),
  // 所以同一个失败模式两个模板都存在,用例也得各写一份。用行内 base,不碰上面共享的那个。
  it('数组不当作对象往下钻(否则会按下标逐项合并出四不像)', () => {
    const out = withExt({ m: { tags: ['a', 'b', 'c'] } }, { './ext/zh-CN/m.ts': mod({ tags: ['x'] }) }, 'zh-CN')
    expect(out.m).toEqual({ tags: ['x'] })
  })
})
