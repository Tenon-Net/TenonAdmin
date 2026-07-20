import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
// 设计令牌必须在任何组件渲染之前进文档:B2 的主题桥要 getComputedStyle 读回这些变量喂给 ConfigProvider。
// 放在模块顶层而不是某个组件的 effect 里,靠的是模块求值早于渲染,不是 import 的书写位置。
import '@/styles/tokens.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
