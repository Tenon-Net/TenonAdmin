import { describe, it, expect, afterEach } from 'vitest'
import { i18n, t, te } from './index'
import { useAppStore } from '@/stores/app'

/**
 * i18next 接线(合并规则在 index.spec.ts)。
 *
 * 这里**一律断行为、不断资源对象** —— 断 `resources['zh-CN'].translation` 里有没有某个键,
 * 是把喂进去的东西再读一遍(回声),`t()` 取不取得到字才是使用者会遇到的事。`resources` 因此不导出。
 */
afterEach(async () => {
  useAppStore.setState({ locale: 'zh-CN' })
  await i18n.changeLanguage('zh-CN')
})

describe('资源装载', () => {
  it('两种语言都真的取得到字', async () => {
    await i18n.changeLanguage('zh-CN')
    expect(t('common.confirm')).toBe('确定')
    await i18n.changeLanguage('en-US')
    expect(t('common.confirm')).toBe('Confirm')
  })

  it('嵌套键按 `.` 取字(后端 msgKey 就是这个形状)', () => {
    expect(t('error.auth.passwordWrong')).toBe('账号或密码错误')
  })

  it('回落语言是 en-US(断行为:换到没有资源的语言,取到英文而不是键名)', async () => {
    await i18n.changeLanguage('fr-FR')
    expect(t('common.confirm')).toBe('Confirm')
  })
})

describe('i18next 三处默认值改写(坏了都不抛错)', () => {
  it('占位符是单花括号 `{name}`', () => {
    // i18next 默认是 `{{name}}`。不改的话这里原样吐出「你好,{name}」—— 不抛错、不告警,
    // 只是页面上永远挂着一个花括号。
    expect(t('workbench.welcome', { name: '张三' })).toBe('你好,张三')
  })

  it('不做 HTML 转义(React 自己会转;再转一遍是把 & 显示成 &amp;)', () => {
    expect(t('workbench.welcome', { name: 'A&B' })).toContain('A&B')
  })

  it('含冒号的键不被当成「命名空间:键」切开', () => {
    // `nsSeparator` 默认是 `:`,而**权限码正是 `GET:/api/v1/x` 这个形状**。
    // 被切开的话查的是不存在的命名空间,i18next 返回键名本身。
    const key = 'GET:/api/v1/ping'
    expect(t(key)).toBe(key) // 缺键回落成键名 —— 但必须是**完整**的键名,没被切掉前半截
    expect(t(key)).not.toBe('/api/v1/ping')
  })
})

describe('te():与 Vue 侧 te 语义对齐,而不是 i18next 的 exists', () => {
  it('叶子键为真', () => {
    expect(te('error.auth.passwordWrong')).toBe(true)
  })

  it('缺失键为假', () => {
    expect(te('压根没有这个键')).toBe(false)
  })

  it('**子树键为假** —— 这正是与 exists() 分道的那一格', () => {
    // `i18n.exists('error.auth')` 是 true,而 `t('error.auth')` 返回
    // "key 'error.auth (zh-CN)' returned an object instead of string." —— 一句英文 debug 文本。
    // 错误提示那条路径若用 exists(),后端发来一个恰好是子树路径的 msgKey 就会把它弹给用户。
    expect(i18n.exists('error.auth')).toBe(true) // i18next 的说法
    expect(te('error.auth')).toBe(false) // 我们要的说法(= vue-i18n 的说法)
  })
})

describe('语言跟随 app store', () => {
  it('改 store 的 locale 就换语言(不用组件参与)', async () => {
    expect(t('common.confirm')).toBe('确定')
    // 只动 store,**不碰 i18n** —— 要验的就是那条模块级订阅。订阅里是 fire-and-forget,所以轮询等。
    useAppStore.setState({ locale: 'en-US' })
    await expect.poll(() => i18n.language).toBe('en-US')
    expect(t('common.confirm')).toBe('Confirm')
  })

  it('写入同一个 locale 不触发换语言(订阅里有 prev 比对)', async () => {
    await i18n.changeLanguage('zh-CN')
    useAppStore.setState({ locale: 'zh-CN' })
    await new Promise((r) => setTimeout(r, 20))
    expect(i18n.language).toBe('zh-CN')
  })
})
