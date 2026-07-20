import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'

// R4 会在这里补 `import '@/styles/tokens.css'` —— 设计令牌必须在任何组件渲染之前进文档,
// 因为 B2 的主题桥要 getComputedStyle 读回这些变量喂给 ConfigProvider。R3 只有壳,还没有令牌。

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
