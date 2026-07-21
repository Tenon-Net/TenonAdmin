import { describe, it, expect, beforeEach, vi } from 'vitest'

// sha256 走 crypto.subtle;stub 掉避免依赖测试环境的 webcrypto(hash 值不影响逻辑,chunkInit 已 mock)。
vi.stubGlobal('crypto', { subtle: { digest: async () => new Uint8Array(32).buffer } })
vi.mock('@/api', () => ({ fileApi: { chunkInit: vi.fn(), chunkUpload: vi.fn(), chunkComplete: vi.fn(), upload: vi.fn() } }))
import { fileApi } from '@/api'
import { uploadChunked } from './chunkUpload'

const CHUNK = 5 * 1024 * 1024
// 假 File:size 是纯数字(不真分配大内存);arrayBuffer 给 hash 一点小内容;slice 返回占位 blob。
const fakeFile = (size: number, name = 'f.bin'): File =>
  ({ name, size, arrayBuffer: async () => new ArrayBuffer(8), slice: () => new Blob([new Uint8Array(1)]) }) as unknown as File

beforeEach(() => {
  vi.mocked(fileApi.chunkInit).mockReset()
  vi.mocked(fileApi.chunkUpload).mockReset().mockResolvedValue(undefined as never)
  vi.mocked(fileApi.chunkComplete).mockReset()
})

describe('uploadChunked', () => {
  it('秒传命中 → 直接返回 init.file + 进度 100,不传分片/不 complete', async () => {
    const file = { id: 1 } as never
    vi.mocked(fileApi.chunkInit).mockResolvedValue({ uploaded: true, file } as never)
    const prog: number[] = []
    const out = await uploadChunked(fakeFile(CHUNK), (p) => prog.push(p))
    expect(out).toBe(file)
    expect(prog.at(-1)).toBe(100)
    expect(fileApi.chunkUpload).not.toHaveBeenCalled()
    expect(fileApi.chunkComplete).not.toHaveBeenCalled()
  })

  it('断点续传:跳过已收分片,只传缺失片,末尾 complete,进度收 100', async () => {
    // 11MB → ceil(11/5)=3 片;已收 [0] → 只传 1、2。
    vi.mocked(fileApi.chunkInit).mockResolvedValue({ uploaded: false, uploadId: 'u1', receivedIndexes: [0] } as never)
    const completed = { done: true } as never
    vi.mocked(fileApi.chunkComplete).mockResolvedValue(completed)
    const prog: number[] = []
    const out = await uploadChunked(fakeFile(11 * 1024 * 1024), (p) => prog.push(p))
    const idxs = vi.mocked(fileApi.chunkUpload).mock.calls.map((c) => c[1] as number).sort()
    expect(idxs).toEqual([1, 2]) // 跳过 0
    expect(vi.mocked(fileApi.chunkUpload).mock.calls[0][0]).toBe('u1') // uploadId 透传
    expect(fileApi.chunkComplete).toHaveBeenCalledOnce()
    expect(out).toBe(completed)
    expect(prog.at(-1)).toBe(100)
    expect(Math.max(...prog.slice(0, -1))).toBeLessThanOrEqual(99) // complete 前封顶 99
  })

  it('全部分片已收 → 不再传,直接 complete', async () => {
    vi.mocked(fileApi.chunkInit).mockResolvedValue({ uploaded: false, uploadId: 'u1', receivedIndexes: [0, 1, 2] } as never)
    vi.mocked(fileApi.chunkComplete).mockResolvedValue({} as never)
    await uploadChunked(fakeFile(11 * 1024 * 1024))
    expect(fileApi.chunkUpload).not.toHaveBeenCalled()
    expect(fileApi.chunkComplete).toHaveBeenCalledOnce()
  })

  it('空文件也至少 1 片(Math.max(1, ceil))', async () => {
    vi.mocked(fileApi.chunkInit).mockResolvedValue({ uploaded: false, uploadId: 'u1', receivedIndexes: [] } as never)
    vi.mocked(fileApi.chunkComplete).mockResolvedValue({} as never)
    await uploadChunked(fakeFile(0)) // 0 字节 → ceil(0)=0,靠 max(1,…) 兜成 1 片
    expect(vi.mocked(fileApi.chunkInit).mock.calls[0][0].chunkCount).toBe(1)
    expect(fileApi.chunkUpload).toHaveBeenCalledTimes(1) // 该唯一片要真传
  })
})
