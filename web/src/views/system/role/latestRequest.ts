/**
 * 只允许最近一次请求提交结果。旧请求的成功和失败都会被静默丢弃，
 * 适合用户快速切换授权目标时保护抽屉状态。
 */
export function createLatestRequest() {
  let latest = 0

  return {
    async run<T>(request: () => Promise<T>, apply: (value: T) => void): Promise<boolean> {
      const token = ++latest
      try {
        const value = await request()
        if (token !== latest) return false
        apply(value)
        return true
      } catch (error) {
        if (token !== latest) return false
        throw error
      }
    },
  }
}
