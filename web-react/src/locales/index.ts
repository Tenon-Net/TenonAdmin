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
export const resources = {
  'zh-CN': { translation: withExt(zhCN, extMods, 'zh-CN') },
  'en-US': { translation: withExt(enUS, extMods, 'en-US') },
}
