// 分片 / 断点续传上传编排(设计 §4 分片上传):切片 → 算整文件 SHA-256 → init(秒传/断点)→ 并发传缺失片 → complete。
import { fileApi } from '@/api'
import type { FileUploadOutput } from '@/types/api'

/** 默认分片大小 5MB。 */
const CHUNK_SIZE = 5 * 1024 * 1024
/** 并发上传的分片数。 */
const CONCURRENCY = 3

/**
 * 算整文件 SHA-256(hex,小写),与后端合并后重算比对。
 * ponytail: 一次性读入内存做全量 hash,正确优先;超大文件想省内存可改分块 + WASM 流式 hash(需引库),当前不引依赖。
 */
async function sha256Hex(file: File): Promise<string> {
  const buf = await file.arrayBuffer()
  const digest = await crypto.subtle.digest('SHA-256', buf)
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('')
}

/**
 * 分片上传一个文件,返回文件记录。
 * @param onProgress 进度回调(0–100)。秒传直接 100;否则按已传分片比例(留末段给 complete)。
 */
export async function uploadChunked(file: File, onProgress?: (percent: number) => void): Promise<FileUploadOutput> {
  const fileHash = await sha256Hex(file)
  const chunkCount = Math.max(1, Math.ceil(file.size / CHUNK_SIZE))

  const init = await fileApi.chunkInit({ fileHash, fileName: file.name, totalSize: file.size, chunkCount })
  if (init.uploaded && init.file) {
    onProgress?.(100)
    return init.file // 秒传命中
  }
  const uploadId = init.uploadId!
  const done = new Set(init.receivedIndexes ?? [])

  // 待传分片下标(跳过断点续传已收的)
  const pending: number[] = []
  for (let i = 0; i < chunkCount; i++) if (!done.has(i)) pending.push(i)

  let uploaded = done.size
  const report = () => onProgress?.(Math.min(99, Math.round((uploaded / chunkCount) * 100)))
  report()

  // 限并发的 worker 池:共享游标,各自领片上传
  let cursor = 0
  async function worker() {
    while (cursor < pending.length) {
      const i = pending[cursor++]
      const blob = file.slice(i * CHUNK_SIZE, Math.min((i + 1) * CHUNK_SIZE, file.size))
      await fileApi.chunkUpload(uploadId, i, blob)
      uploaded++
      report()
    }
  }
  await Promise.all(Array.from({ length: Math.min(CONCURRENCY, pending.length) }, worker))

  const out = await fileApi.chunkComplete({ uploadId, fileHash, fileName: file.name, chunkCount })
  onProgress?.(100)
  return out
}
