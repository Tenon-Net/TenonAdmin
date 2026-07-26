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
 * 触发浏览器下载 —— 实现已移到 `@/utils/download`(导入向导与两个列表页也要用,不该住在文件页里)。
 * 此处保留同名再导出,文件页与 `fileFormat.spec` 的既有写法不动。
 */
export { triggerBlobDownload } from '@/utils/download'
