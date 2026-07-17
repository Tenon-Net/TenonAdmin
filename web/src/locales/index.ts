import { createI18n } from 'vue-i18n'
import zhCN from './zh-CN'
import enUS from './en-US'

/**
 * 消费者 i18n 扩展接缝:丢一个 `locales/ext/<locale>/<模块>.ts`(默认导出该模块的键)即自动并入,
 * 无需编辑本文件,更不必编辑 zh-CN.ts / en-US.ts —— 那两个是上游自留地(各 52 次改动),
 * 消费者一旦写进去,每次 merge upstream 都撞冲突。文件名即顶层命名空间(`sample.ts` → `t('sample.xxx')`)。
 *
 * glob 模式必须是字面量(Vite 要静态分析),所以一次抓全部 locale 再按前缀分组,而不是拼 `./ext/${locale}/*.ts`。
 *
 * 合并是<b>递归深合并</b>而非整体覆盖。这一点是必需的,不是讲究:后端 msgKey 是嵌套语义键
 * (`error.dict.typeNotFound`,见 ErrorCode.GetMsgKey),消费者给自己的错误码加文案要写成 `{ doc: { titleDuplicated } }`;
 * 而一旦有人想改写内置文案(`{ auth: { passwordWrong: '...' } }`),浅合并会把整个 auth 子树连同
 * captchaExpired/accountLocked 一起静默抹掉,且只在真报那个错时才暴露。深合并让「补一个键」永远只是补一个键。
 */
const extMods = import.meta.glob<ExtModule>('./ext/*/*.ts', { eager: true })

type ExtModule = { default: Record<string, unknown> }
type Messages = Record<string, Record<string, unknown>>

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

/** glob 结果按 locale 前缀筛出、按命名空间(文件名)深合并进 base。`mods` 参数化只为可测(见 index.spec.ts)。 */
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

export const i18n = createI18n({
  legacy: false,
  locale: 'zh-CN',
  fallbackLocale: 'en-US',
  messages: {
    'zh-CN': withExt(zhCN, extMods, 'zh-CN'),
    'en-US': withExt(enUS, extMods, 'en-US'),
  },
})

/** 供非 setup 上下文(工具函数)使用的翻译器。 */
export const t = i18n.global.t
