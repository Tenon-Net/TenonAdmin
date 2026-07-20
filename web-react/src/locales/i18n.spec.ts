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

  it('嵌套键按 `.` 取字(后端 msgKey 就是这个形状)', async () => {
    // 自己设语言,不吃上一个用例 afterEach 留下的 zh-CN:单跑(`-t "嵌套键"`)时那个前提不存在。
    await i18n.changeLanguage('zh-CN')
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

  it('**走回退链** —— 键只存在于 en-US 时,zh-CN 下也为真', async () => {
    // 现实场景:消费者只丢了 `ext/en-US/error.ts` 而没配 zh-CN 对应项。此时 `t()` 会回落取到英文,
    // 那么 `te()` 就必须说 true,否则错误提示那条路径(`te(msgKey) ? t(msgKey) : message`)
    // 会明明有文案却退回后端原文。
    //
    // 两边实测同结论(隔离调用,只调 te 不调 t,免得分不清谁在回落):
    //   vue-i18n `te` 不传 locale → 走 `fallbackWithLocaleChain`(vue-i18n.mjs:636-639)
    //   i18next  `exists` → 走 `resolve` 遍历 `this.languages`(含 fallbackLng)
    //
    // 注意这条与上面那条子树用例**互不覆盖**:把 te 退回裸 `exists()`,子树那条红、这条仍绿。
    //
    // 键是当场注入的,不去 en-US.ts 里挑一个「恰好 zh-CN 没有」的:两份文案本就该是镜像,
    // 哪天有人把缺口补齐,挑出来的那个键就两边都有了 —— 这条用例会**静默失去判别力**而依旧全绿。
    i18n.addResource('en-US', 'translation', '__enOnly', 'EN only')
    await i18n.changeLanguage('zh-CN')
    expect(i18n.exists('__enOnly', { lng: 'zh-CN', fallbackLng: false })).toBe(false) // zh-CN 自己确实没有
    expect(te('__enOnly')).toBe(true) // 但走回退链取得到 → 必须为真
  })
})

/**
 * 与 vue-i18n 的 `te` **有意不对齐的那一格**,写成用例是为了让「有意」和「忘了」长得不一样。
 * vue-i18n 的 te 认 string / message AST / message function 三种;这里只认 string。
 * 方向是安全的(挡住把函数塞进 React 渲染),消费者真在 `ext/` 里写了函数值就退回后端原文。
 * 哪天要对齐,把这条用例反过来即可 —— 它此刻记录的是**当前契约**,不是「正确答案」。
 */
describe('te():已知的一格分道 —— message function', () => {
  it('函数值为假(vue-i18n 那边是真)', () => {
    i18n.addResource('zh-CN', 'translation', '__fnProbe', (() => 'x') as unknown as string)
    expect(i18n.exists('__fnProbe')).toBe(true)
    expect(te('__fnProbe')).toBe(false)
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
