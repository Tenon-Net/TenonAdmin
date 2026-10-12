import { nextTick } from 'vue'

/** 页面滚动记忆只保存仍打开的标签；容器的读写由布局层负责。 */
export function createPageScrollMemory() {
  const positions = new Map<string, number>()

  return {
    save(key: string, scrollTop: number) {
      positions.set(key, scrollTop)
    },
    read(key: string) {
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
