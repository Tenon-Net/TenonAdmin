import { t, te } from '@/locales'
import { ApiError } from '@/api'

/**
 * 把任意错误翻成用户可读文案:
 *   ApiError.msgKey 命中 i18n → 本地化文案;否则退回 message;再退回通用兜底。
 * 纯函数,不依赖 antd —— 视图拿到字符串后自行 `App.useApp().message` 弹出。
 *
 * **这是 `te()` 的唯一消费者**,也是 B4 里那条判据存在的理由:后端 msgKey 只要恰好是个**子树路径**,
 * `i18n.exists()` 会说 true 而 `t()` 返回 `"key 'error.auth' returned an object instead of string."`
 * —— 一句英文 debug 文本,会被这里当成文案弹给用户。`te()` 对子树返回 false,于是退回后端原文。
 * (语义对齐 Vue 侧 `i18n.global.te`;唯一有意不对齐的是 message function,见 `locales/index.ts`。)
 */
export function translateError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.msgKey && te(err.msgKey)) return t(err.msgKey)
    if (err.message) return err.message
  }
  if (err instanceof Error && err.message) return err.message
  return t('error._fallback')
}
