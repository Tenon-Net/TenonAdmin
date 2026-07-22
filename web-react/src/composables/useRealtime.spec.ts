import { describe, it, expect, vi } from 'vitest'
import { noticeBus } from './useRealtime'

// SignalR 连接依赖真 Hub,happy-dom 无从测(同 Chart 命令式渲染,判据落纯逻辑)。
// 这里只钉 noticeBus 这条替 VueUse useEventBus 的模块级 pub/sub —— 长连接与角标解耦的接缝。
describe('noticeBus', () => {
  it('on → emit 调用监听器;退订后不再调用', () => {
    const fn = vi.fn()
    const off = noticeBus.on(fn)
    noticeBus.emit()
    expect(fn).toHaveBeenCalledTimes(1)
    off()
    noticeBus.emit()
    expect(fn).toHaveBeenCalledTimes(1) // 退订后 emit 不再增
  })

  it('多订阅者各自收到', () => {
    const a = vi.fn()
    const b = vi.fn()
    const offA = noticeBus.on(a)
    const offB = noticeBus.on(b)
    noticeBus.emit()
    expect(a).toHaveBeenCalledTimes(1)
    expect(b).toHaveBeenCalledTimes(1)
    offA()
    offB()
  })
})
