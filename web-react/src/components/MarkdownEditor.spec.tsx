import { describe, it, expect, beforeEach, vi } from 'vitest'

vi.mock('@/api', () => ({ fileApi: { upload: vi.fn() } }))
import { fileApi } from '@/api'
import { uploadImages } from './MarkdownEditor'

beforeEach(() => {
  vi.mocked(fileApi.upload).mockReset()
})

describe('uploadImages', () => {
  it('逐个上传 → callback 收各自 viewUrl(按序)', async () => {
    vi.mocked(fileApi.upload).mockImplementation(async (f) => ({ viewUrl: `url:${(f as File).name}` }) as never)
    const cb = vi.fn()
    await uploadImages([new File([], 'a.png'), new File([], 'b.png')], cb)
    expect(cb).toHaveBeenCalledWith(['url:a.png', 'url:b.png'])
  })
  it('viewUrl 缺失 → 退空串(?? 兜底)', async () => {
    vi.mocked(fileApi.upload).mockResolvedValue({} as never)
    const cb = vi.fn()
    await uploadImages([new File([], 'a.png')], cb)
    expect(cb).toHaveBeenCalledWith([''])
  })
})
