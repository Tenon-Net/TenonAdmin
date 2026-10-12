import { nextTick } from 'vue'

interface PageScrollTab {
  path: string
  noCache?: boolean
}

/** 滚动策略跟随标签缓存策略：缓存页恢复，noCache 与已关闭页面一律归零。 */
export function shouldPreservePageScroll(tabs: readonly PageScrollTab[], key: string) {
  return tabs.some((tab) => tab.path === key && !tab.noCache)
}

/** 页面滚动记忆只保存允许缓存且仍打开的标签；容器的读写由布局层负责。 */
export function createPageScrollMemory() {
  const positions = new Map<string, number>()

  return {
    save(key: string, scrollTop: number, preserveScroll = true) {
      if (!preserveScroll) {
        positions.delete(key)
        return
      }
      positions.set(key, scrollTop)
    },
    read(key: string, preserveScroll = true) {
      if (!preserveScroll) {
        positions.delete(key)
        return 0
      }
      return positions.get(key) ?? 0
    },
    reset(key: string) {
      positions.delete(key)
    },
    prune(openKeys: readonly string[]) {
      const open = new Set(openKeys)
      for (const key of positions.keys()) {
        if (!open.has(key)) positions.delete(key)
      }
    },
  }
}

/** before-enter 触发时新节点尚未插入；等本轮 DOM 提交后、浏览器绘制前恢复共享容器。 */
export async function restorePageScrollBeforePaint(restore: () => void) {
  await nextTick()
  restore()
}
