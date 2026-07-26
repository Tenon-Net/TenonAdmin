import { t, te } from '@/locales'
import { ApiError } from '@/api'

/**
 * 数值 ErrorCode → msgKey(镜像后端 [MsgKey];CellError 只带码不带文案,§13.2)。
 * 只列导入预览会在单元格/列错误里出现的码 + 常见下载失败码;其余走兜底。
 * 与 web/src/utils/error.ts 有意重复(零共享硬约束,excel-ledger 坑 7);改码值时两份都得改。
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
 * 纯函数,不依赖 antd —— 视图拿到字符串后自行 `App.useApp().message` 弹出。
 *
 * **这是 `te()` 的唯一消费者**,也是 B4 里那条判据存在的理由:后端 msgKey 只要恰好是个**子树路径**,
 * `i18n.exists()` 会说 true 而 `t()` 返回 `"key 'error.auth' returned an object instead of string."`
 * —— 一句英文 debug 文本,会被这里当成文案弹给用户。`te()` 对子树返回 false,于是退回后端原文。
 * (语义对齐 Vue 侧 `i18n.global.te`;唯一有意不对齐的是 message function,见 `locales/index.ts`。)
 */
export function translateError(err: unknown): string {
  if (typeof err === 'number') {
    const key = CODE_MSG_KEY[err]
    if (key && te(key)) return t(key)
    return t('error._fallback')
  }
  if (err instanceof ApiError) {
    if (err.msgKey && te(err.msgKey)) return t(err.msgKey)
    // 无 msgKey 时用数字码兜底(下载失败信封偶发只有 code)
    if (!err.msgKey && CODE_MSG_KEY[err.code] && te(CODE_MSG_KEY[err.code])) {
      return t(CODE_MSG_KEY[err.code])
    }
    if (err.message) return err.message
  }
  if (err instanceof Error && err.message) return err.message
  return t('error._fallback')
}
