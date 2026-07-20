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
    const ns = path.slice(prefix.length, -'.ts'.length)
    merged[ns] = deepMerge(merged[ns] ?? {}, mod.default)
  }
  return merged
}

// glob 模式必须是字面量(Vite 要静态分析),所以一次抓全部 locale 再按前缀分组。
const extMods = import.meta.glob<ExtModule>('./ext/*/*.ts', { eager: true })

/** 并入消费者扩展后的最终文案。B4 把它接到 i18next 上。 */
export const messages = {
  'zh-CN': withExt(zhCN, extMods, 'zh-CN'),
  'en-US': withExt(enUS, extMods, 'en-US'),
}
