/**
 * 触发浏览器下载 blob:`createObjectURL` → 临时 `<a download>` 点击 → **移除并释放 URL**。
 *
 * 抽出来是为了能单测这一步 —— 它的两种错法都是**静默**的:漏 `revokeObjectURL` 会内存泄漏,
 * 漏 `download` 名会拿 blob URL 的末段当文件名(存成一串 uuid)。两者都不报错、不影响构建,
 * 也不会被 `typecheck` / `lint` / `build` 照出来(excel-ledger 坑 12)。
 *
 * 原先只住在 `views/system/file/fileFormat.ts` 里,导入向导与两个列表页各自又内联抄了一遍(G7);
 * 现统一到这里,`fileFormat` 保留同名再导出以免动到文件页与它的既有用例。
 */
export function triggerBlobDownload(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
