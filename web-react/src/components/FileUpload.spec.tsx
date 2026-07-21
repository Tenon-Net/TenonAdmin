import { describe, it, expect, beforeEach, vi } from 'vitest'
import '@/locales'

vi.mock('@/utils/chunkUpload', () => ({ uploadChunked: vi.fn() }))
// ApiError 也要给:translateError(错误路径)会 `err instanceof ApiError`,整体 mock @/api 否则丢了它。
vi.mock('@/api', () => ({ fileApi: { upload: vi.fn() }, ApiError: class ApiError extends Error {} }))
import { uploadChunked } from '@/utils/chunkUpload'
import { fileApi } from '@/api'
import { performUpload } from './FileUpload'

const file = new File([new Uint8Array(1)], 'f.bin')

beforeEach(() => {
  vi.mocked(uploadChunked).mockReset()
  vi.mocked(fileApi.upload).mockReset()
})

describe('performUpload', () => {
  it('chunked → 走 uploadChunked(非直传),onUploaded/onSuccess 收 out,loading 先 true 后 false', async () => {
    const out = { id: 1 } as never
    vi.mocked(uploadChunked).mockResolvedValue(out)
    const onUploaded = vi.fn(), onSuccess = vi.fn(), onLoadingChange = vi.fn()
    await performUpload(file, true, { onUploaded, onSuccess, onLoadingChange })
    expect(uploadChunked).toHaveBeenCalledOnce()
    expect(fileApi.upload).not.toHaveBeenCalled()
    expect(onUploaded).toHaveBeenCalledWith(out)
    expect(onSuccess).toHaveBeenCalledWith(out)
    expect(onLoadingChange.mock.calls.map((c) => c[0])).toEqual([true, false])
  })

  it('非 chunked → 走 fileApi.upload(非分片)', async () => {
    vi.mocked(fileApi.upload).mockResolvedValue({ id: 2 } as never)
    const onUploaded = vi.fn()
    await performUpload(file, false, { onUploaded })
    expect(fileApi.upload).toHaveBeenCalledOnce()
    expect(uploadChunked).not.toHaveBeenCalled()
    expect(onUploaded).toHaveBeenCalledWith({ id: 2 })
  })

  it('失败 → onFailMessage(译错) + onError,loading 仍收尾 false', async () => {
    vi.mocked(fileApi.upload).mockRejectedValue(new Error('boom'))
    const onError = vi.fn(), onFailMessage = vi.fn(), onLoadingChange = vi.fn(), onUploaded = vi.fn()
    await performUpload(file, false, { onError, onFailMessage, onLoadingChange, onUploaded })
    expect(onFailMessage).toHaveBeenCalledOnce()
    expect(onError).toHaveBeenCalledOnce()
    expect(onUploaded).not.toHaveBeenCalled() // 失败不回成功
    expect(onLoadingChange.mock.calls.at(-1)?.[0]).toBe(false) // finally 收尾
  })

  it('chunked 进度经 onProgress 透传', async () => {
    vi.mocked(uploadChunked).mockImplementation(async (_f, onP) => {
      onP?.(42)
      return { id: 3 } as never
    })
    const onProgress = vi.fn()
    await performUpload(file, true, { onProgress })
    expect(onProgress).toHaveBeenCalledWith(42)
  })
})
