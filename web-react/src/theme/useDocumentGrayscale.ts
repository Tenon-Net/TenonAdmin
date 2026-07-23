import { useLayoutEffect } from 'react'
import { useAppStore } from '@/stores/app'

/**
 * 灰阶(哀悼模式):把 `data-gray` 打到 `<html>`,`styles/chrome.css` 的 `html[data-gray]{filter:grayscale(1)}`
 * 据此整页去色。**单独一条 effect,不进 `useAntdTheme` 的依赖** —— 灰阶只是 CSS filter、不改 antd 主题,
 * 混进去只会让"切灰阶"白重建整棵 ConfigProvider。抽成 hook(而非内联在 App)是为了能单测这个 DOM 副作用。
 */
export function useDocumentGrayscale(): void {
  const grayscale = useAppStore((s) => s.grayscale)
  useLayoutEffect(() => {
    document.documentElement.toggleAttribute('data-gray', grayscale)
  }, [grayscale])
}
