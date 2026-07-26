import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { triggerBlobDownload } from './download'

// 导入向导的「下载模板」「下载错误报告」与列表页的「导出」都走这一步(excel-ledger G6)。
// 浏览器实走验过一次,但那一趟走完就没了;这条每次 CI 都跑。
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
    triggerBlobDownload(blob, '用户导入模板.xlsx')
    expect(createObjSpy).toHaveBeenCalledWith(blob)
    expect(anchor).not.toBeNull()
    expect(anchor!.download).toBe('用户导入模板.xlsx') // 漏设 download → 空串,存成一串 uuid,红
    expect(anchor!.click).toHaveBeenCalled() // 不点 → 什么都不会下载,红
    expect(revokeSpy).toHaveBeenCalledWith('blob:mock-url') // 漏 revoke → 内存泄漏,红
    expect(document.body.contains(anchor!)).toBe(false) // 用完从 DOM 移除
  })
})
