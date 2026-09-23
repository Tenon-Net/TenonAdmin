/**
 * 触发浏览器下载 blob:`createObjectURL` → 临时 `<a download>` 点击 → **移除并释放 URL**。
 *
 * 抽出来是为了能单测这一步 —— 它的两种错法都是**静默**的:漏 `revokeObjectURL` 会内存泄漏,
 * 漏 `download` 名会拿 blob URL 的末段当文件名(存成一串 uuid)。两者都不报错、不影响构建,
 * 也不会被 `typecheck` / `lint` / `build` 照出来(excel-ledger 坑 12)。
 */
/**
 * 从 Content-Disposition 取文件名。优先 RFC 5987 的 `filename*`，否则 `filename`。
 * 解析失败回退调用方给出的名字，避免把 blob URL 末段存成文件名。
 */
export function fileNameFromContentDisposition(header: string | null | undefined, fallback: string): string {
  if (!header) return fallback
  const star = /filename\*\s*=\s*([^']*)''([^;]+)/i.exec(header)
  if (star?.[2]) {
    const encoded = star[2].trim().replace(/^"|"$/g, '')
    try {
      const name = decodeURIComponent(encoded)
      if (name) return name
    } catch {
      // 非法百分号编码时继续尝试普通 filename
    }
  }
  const quoted = /filename\s*=\s*"([^"]+)"/i.exec(header)
  if (quoted?.[1]) return quoted[1]
  const plain = /filename\s*=\s*([^;]+)/i.exec(header)
  const name = plain?.[1]?.trim().replace(/^"|"$/g, '')
  return name || fallback
}

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
