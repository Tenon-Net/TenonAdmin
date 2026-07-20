import i18next from 'i18next'
import { initReactI18next } from 'react-i18next'
import { useAppStore } from '@/stores/app'
import zhCN from './zh-CN'
import enUS from './en-US'

/**
 * 消费者 i18n 扩展接缝:丢一个 `locales/ext/<locale>/<模块>.ts`(默认导出该模块的键)即自动并入,
 * 无需编辑本文件,更不必编辑 zh-CN.ts / en-US.ts —— 那两个是上游自留地,消费者一旦写进去,
 * 每次 merge upstream 都撞冲突。文件名即顶层命名空间(`sample.ts` → `t('sample.xxx')`)。
 *
 * 合并规则原先抽在 `web-shared/locales/ext.ts` 供两个模板共用,共享层方向推翻后**内联在这里**。
 * 也就是说这段逻辑与 `web/src/locales/index.ts` 里那份是**有意重复**的:两个模板各自自包含,
 * 代价就是这类文件要改两遍。改动这里的合并语义时,顺手看一眼 Vue 侧要不要跟——但**不建同步脚本、
 * 不建"两边必须一致"的 CI 闸门**,两个模板本就该能有意分叉。
 */
export type ExtModule = { default: Record<string, unknown> }
export type Messages = Record<string, Record<string, unknown>>

const isPlainObject = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v)

/** 递归合并:两边都是普通对象才往下钻,否则 ext 侧胜出。不改动任何入参。 */
function deepMerge(base: Record<string, unknown>, ext: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = { ...base }
  for (const [key, extVal] of Object.entries(ext)) {
    const baseVal = out[key]
    out[key] = isPlainObject(baseVal) && isPlainObject(extVal) ? deepMerge(baseVal, extVal) : extVal
  }
  return out
}

/**
 * glob 结果按 locale 前缀筛出、按命名空间(文件名)深合并进 base。`mods` 参数化只为可测。
 *
 * 合并是**递归深合并**而非整体覆盖。这一点是必需的,不是讲究:后端 msgKey 是嵌套语义键
 * (`error.dict.typeNotFound`,见 ErrorCode.GetMsgKey),消费者给自己的错误码加文案要写成
 * `{ doc: { titleDuplicated } }`;而一旦有人想改写内置文案(`{ auth: { passwordWrong: '...' } }`),
 * 浅合并会把整个 auth 子树连同 captchaExpired/accountLocked 一起静默抹掉,且只在真报那个错时才暴露。
 * 深合并让「补一个键」永远只是补一个键。
 */
export function withExt(base: Messages, mods: Record<string, ExtModule>, locale: string): Messages {
  const prefix = `./ext/${locale}/`
  const merged: Messages = { ...base }
  for (const [path, mod] of Object.entries(mods)) {
    if (!path.startsWith(prefix)) continue
    // 这是本文件唯一面向**外部输入**的地方(`ext/` 是消费者的地盘),所以类型说什么不算数。
    // 消费者把 `export default {...}` 写成 `export const foo = {...}` 时 `mod.default` 是 undefined,
    // 而 glob 是 eager 的、在模块顶层求值 —— 不加这道守卫就是 `deepMerge` 当场抛
    // `Cannot convert undefined or null to object`,表现为**整站白屏**,控制台那句话还完全指不到
    // 是哪个文件写错了。所以点名路径再跳过,别静默。
    if (!isPlainObject(mod?.default)) {
      console.warn(`[i18n] 跳过 ${path}:扩展文案必须写成 \`export default { ... }\`(拿到的是 ${typeof mod?.default})`)
      continue
    }
    const ns = path.slice(prefix.length, -'.ts'.length)
    merged[ns] = deepMerge(merged[ns] ?? {}, mod.default)
  }
  return merged
}

// glob 模式必须是字面量(Vite 要静态分析),所以一次抓全部 locale 再按前缀分组。
const extMods = import.meta.glob<ExtModule>('./ext/*/*.ts', { eager: true })

/**
 * 并入消费者扩展后的最终文案,**已经是 i18next 的 `resources` 形状**,B4 直接 `init({ resources })`。
 *
 * 那一层 `translation` 不能省。i18next 把 `resources` 的第二层当**命名空间**:少了它,`error`/`common`
 * 会被当成 ns,而默认 ns(`translation`)不存在 —— i18next 对缺键的处理是**返回键名本身**,于是
 * `t('error.auth.passwordWrong')` 原样吐出这串点分英文。不抛错、不告警、四件套全绿,整站文案变成键名。
 * 本模板只有一个 ns,所以整份文案挂在 `translation` 下。
 */
// **不导出**:唯一的消费者就是下面那行 init。导出它只会诱使 spec 去断言"资源对象里有这个键",
// 而那是在回声 —— 该断的是 `t()` 取不取得到字,那才是使用者会遇到的东西。
const resources = {
  'zh-CN': { translation: withExt(zhCN, extMods, 'zh-CN') },
  'en-US': { translation: withExt(enUS, extMods, 'en-US') },
}

/**
 * i18next 的三处默认值都与本仓库的文案不兼容,且**失败方式都很安静**,所以逐条写死在这里:
 *
 * 1. `interpolation.prefix/suffix` —— i18next 默认占位符是 `{{name}}`,而文案是照 vue-i18n 的
 *    `{name}` 写的。不改的话 `t('workbench.welcome')` 原样吐出 `你好,{name}` ——
 *    不抛错、不告警,只是页面上永远挂着一个花括号。
 * 2. `escapeValue: false` —— i18next 默认把插值 HTML 转义(它面向 innerHTML);React 本来就转义,
 *    再来一遍会把用户名里的 `&`/`<` 变成 `&amp;`/`&lt;` 显示出来。
 * 3. `nsSeparator: false` —— 只有一个命名空间。留着默认的 `:` 的话,任何含冒号的键会被切成
 *    「命名空间:键」两半而查不到 —— 而**权限码正是 `GET:/api/v1/x` 这个形状**。
 */
export const i18n = i18next.use(initReactI18next)
// `void`:init 返回 Promise,同步资源下它已经完成,但不加 void 的话 lint 会当成漏 await 的浮动 Promise。
void i18n.init({
  resources,
  // 首帧就用持久化下来的语言,不要「先中文再跳成英文」。persist 在建 store 时同步水合,这里读得到。
  lng: useAppStore.getState().locale,
  fallbackLng: 'en-US',
  nsSeparator: false,
  interpolation: { prefix: '{', suffix: '}', escapeValue: false },
})

// 语言跟随 app store。写在模块级而不是组件的 useEffect 里:i18next 实例本就是模块级单例,
// 且非组件上下文(工具函数里的 `t`)也要跟着切。
const unsubscribe = useAppStore.subscribe((s, prev) => {
  if (s.locale !== prev.locale) void i18n.changeLanguage(s.locale)
})
// dev 下 Vite 每次热更新都重跑本模块、再挂一条订阅,旧的指向旧闭包且无人摘除,随编辑次数线性累积。
// (`stores/app.ts` 的 matchMedia 监听是同一个模式。)生产构建里这段被摇掉。
if (import.meta.hot) import.meta.hot.dispose(() => unsubscribe())

/** 供非组件上下文(工具函数)使用的翻译器。 */
export const t = i18n.t.bind(i18n)

/**
 * 「这个键有没有对应的**文案**」—— 语义对齐 Vue 侧的 `i18n.global.te()`,**不是** `i18n.exists()`。
 *
 * 两者在**子树键**上结论相反,而这个差别会直接烧到用户脸上:
 *   `i18n.exists('error.auth')`  → **true**(子树存在)
 *   `t('error.auth')`            → `"key 'error.auth (zh-CN)' returned an object instead of string."`
 * 而 vue-i18n 的 `te` 只在解析结果是 string / message AST / message function 时才为真
 * (`@intlify/core-base` 的 `te`:子树解析成普通对象,三者都不是 → false)。
 *
 * 消费者是错误提示那条路径(Vue 侧 `utils/error.ts:11` 的 `te(msgKey) ? t(msgKey) : message`)。
 * 后端 msgKey 只要恰好是个子树路径,用 `exists()` 的话就会把上面那句**英文 debug 文本**当成文案弹给用户。
 * 后端今天不会发这种 key,但那不是我们能约束的东西 —— 判据按"能不能被打穿"写,不按"今天会不会"。
 */
export function te(key: string): boolean {
  return i18n.exists(key) && typeof i18n.t(key, { returnObjects: true }) === 'string'
}
