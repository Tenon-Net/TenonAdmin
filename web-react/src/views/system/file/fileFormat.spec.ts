import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { formatSize, triggerBlobDownload } from './fileFormat'

describe('formatSize', () => {
  it('分档 + 边界:1024 进档、非整保留 1 位小数', () => {
    expect(formatSize(0)).toBe('0 B')
    expect(formatSize(1023)).toBe('1023 B')
    expect(formatSize(1024)).toBe('1.0 KB') // 边界:1024 是 KB 不是 B(钉住 < 1024)
    expect(formatSize(1536)).toBe('1.5 KB') // 非整保留 1 位(钉住 toFixed(1))
    expect(formatSize(1024 * 1024)).toBe('1.0 MB') // 边界:1MB 进 MB 档
    expect(formatSize(1024 * 1024 * 1024)).toBe('1.0 GB')
    expect(formatSize(3 * 1024 * 1024 * 1024)).toBe('3.0 GB')
  })
})

describe('triggerBlobDownload', () => {
  let createObjSpy: ReturnType<typeof vi.spyOn>
  let revokeSpy: ReturnType<typeof vi.spyOn>
  let anchor: HTMLAnchorElement | null

  beforeEach(() => {
    anchor = null
    // happy-dom 未必实现 createObjectURL/revokeObjectURL,先兜底再 spy。
    if (!URL.createObjectURL) URL.createObjectURL = () => ''
    if (!URL.revokeObjectURL) URL.revokeObjectURL = () => {}
    createObjSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
    revokeSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
    const realCreate = document.createElement.bind(document)
    vi.spyOn(document, 'createElement').mockImplementation(((tag: string) => {
      const el = realCreate(tag)
      if (tag === 'a') {
        anchor = el as HTMLAnchorElement
        vi.spyOn(el as HTMLElement, 'click').mockImplementation(() => {}) // happy-dom 里 a.click() 会试图导航,屏蔽
      }
      return el
    }) as typeof document.createElement)
  })
  afterEach(() => vi.restoreAllMocks())

  it('createObjectURL → <a download=文件名> 点击 → 移除并 revoke', () => {
    const blob = new Blob(['x'])
    triggerBlobDownload(blob, 'report.pdf')
    expect(createObjSpy).toHaveBeenCalledWith(blob)
    expect(anchor).not.toBeNull()
    expect(anchor!.download).toBe('report.pdf') // 漏设 download → 空串,红
    expect(anchor!.click).toHaveBeenCalled()
    expect(revokeSpy).toHaveBeenCalledWith('blob:mock-url') // 漏 revoke → 泄漏,红
    expect(document.body.contains(anchor!)).toBe(false) // 用完从 DOM 移除
  })
})
