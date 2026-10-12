import { createContext, useContext, type ReactNode } from 'react'

const CachedPageActiveContext = createContext(true)

export function CachedPageActiveProvider({ active, children }: { active: boolean; children: ReactNode }) {
  return <CachedPageActiveContext value={active}>{children}</CachedPageActiveContext>
}

/** 缓存页面感知当前是否可见；普通路由树外调用时默认活跃。 */
export const useCachedPageActive = () => useContext(CachedPageActiveContext)
