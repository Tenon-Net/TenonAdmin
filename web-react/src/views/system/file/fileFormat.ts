// 文件页的**纯逻辑**:字节数人类可读 + 触发浏览器下载。抽出便于 fileFormat.spec 变异钉死,
// 页面 index.tsx 只做接线(表格/上传/批删)。
/** 字节 → 人类可读(B / KB / MB / GB,进制 1024,非整数保留 1 位小数)。 */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  return `${(bytes / 1024 / 1024 / 1024).toFixed(1)} GB`
}

/**
 * 触发浏览器下载 blob:createObjectURL → 临时 <a download> 点击 → **移除并释放 URL**。
 * 抽出便于单测下载编排 —— 漏 revoke 会内存泄漏、漏 download 名会用 URL 末段当文件名,都是会静默出错的点。
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
