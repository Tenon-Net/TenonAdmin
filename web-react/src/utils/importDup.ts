import { DuplicateStrategy, type CellError, type ImportRow } from '@/types/api'

/**
 * 46010「库里已存在」。
 *
 * 它**不是硬错误**:后端 `ImportRunner.CommitAsync` 把它排除在 hardErrors 之外,再按重复策略分流
 * (Skip 跳过 / Overwrite 更新 / Error 才算失败)。所以只有 Error 策略下它才该按错误呈现 ——
 * 其余两种策略下,一批完全会被正常处理的行如果满屏标红,用户会以为必须逐格改完才能提交。
 *
 * 这里镜像的是后端那一行判定,后端改了这里要跟着改(与 `utils/error.ts` 的 CODE_MSG_KEY 同款镜像债)。
 * 与 `web/` 同名文件有意重复(零共享,坑 7)。
 */
export const IMPORT_DUPLICATE_IN_DB = 46010

/** 该单元格错误是否需要用户动手改。 */
export function isHardError(e: CellError, strategy: DuplicateStrategy): boolean {
  return e.code !== IMPORT_DUPLICATE_IN_DB || strategy === DuplicateStrategy.Error
}

/** 该行里需要用户动手改的错误(不含按策略自动处理的「已存在」)。 */
export function hardErrorsOf(row: ImportRow, strategy: DuplicateStrategy): CellError[] {
  return row.errors.filter((e) => isHardError(e, strategy))
}

/** 该行只是「已存在」——会被策略正常跳过/更新,不需要改。 */
export function isDuplicateOnly(row: ImportRow, strategy: DuplicateStrategy): boolean {
  return (
    hardErrorsOf(row, strategy).length === 0 &&
    row.errors.some((e) => e.code === IMPORT_DUPLICATE_IN_DB)
  )
}
