import { i18n, t } from '@/locales'
import { ApiError } from '@/api'

/**
 * 把任意错误翻成用户可读文案:
 *   ApiError.msgKey 命中 i18n → 本地化文案;否则退回 message;再退回通用兜底。
 * 纯函数,不依赖 Naive —— 视图拿到字符串后自行 useMessage 弹出。
 */
export function translateError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.msgKey && i18n.global.te(err.msgKey)) return t(err.msgKey)
    if (err.message) return err.message
  }
  if (err instanceof Error && err.message) return err.message
  return t('error._fallback')
}
