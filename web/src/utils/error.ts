import { i18n, t } from '@/locales'
import { ApiError } from '@/api'

/**
 * 数值 ErrorCode → msgKey(镜像后端 [MsgKey];CellError 只带码不带文案,§13.2)。
 * 只列导入预览会在单元格/列错误里出现的码 + 常见下载失败码;其余走兜底。
 */
const CODE_MSG_KEY: Record<number, string> = {
  41002: 'error.perm.demoReadOnly',
  44001: 'error.file.empty',
  44002: 'error.file.tooLarge',
  44003: 'error.file.extNotAllowed',
  46001: 'error.excel.providerMissing',
  46002: 'error.import.fileEmpty',
  46003: 'error.import.rowLimitExceeded',
  46004: 'error.import.columnMissing',
  46005: 'error.import.cellRequired',
  46006: 'error.import.cellDictInvalid',
  46007: 'error.import.cellRefNotFound',
  46008: 'error.import.cellFormatInvalid',
  46009: 'error.import.duplicateInFile',
  46010: 'error.import.duplicateInDb',
  46011: 'error.import.orgOutOfScope',
  46012: 'error.export.tooManyRows',
  46013: 'error.export.columnInvalid',
}

/**
 * 把任意错误翻成用户可读文案:
 *   - 数字 ErrorCode(CellError.code) → 按码查 i18n
 *   - ApiError.msgKey 命中 i18n → 本地化文案;否则退回 message;再退回通用兜底。
 * 纯函数,不依赖 Naive —— 视图拿到字符串后自行 useMessage 弹出。
 */
export function translateError(err: unknown): string {
  if (typeof err === 'number') {
    const key = CODE_MSG_KEY[err]
    if (key && i18n.global.te(key)) return t(key)
    return t('error._fallback')
  }
  if (err instanceof ApiError) {
    if (err.msgKey && i18n.global.te(err.msgKey)) return t(err.msgKey)
    // 无 msgKey 时用数字码兜底(下载失败信封偶发只有 code)
    if (!err.msgKey && CODE_MSG_KEY[err.code] && i18n.global.te(CODE_MSG_KEY[err.code])) {
      return t(CODE_MSG_KEY[err.code])
    }
    if (err.message) return err.message
  }
  if (err instanceof Error && err.message) return err.message
  return t('error._fallback')
}
