import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// 设计令牌:B2 的主题桥要 getComputedStyle 读回这些变量喂给 ConfigProvider。
// **必须写在 `./App` 之前**。对渲染时读取来说位置无所谓(所有 import 求值完才轮到模块体),
// 但 ESM 的求值顺序就是书写顺序 —— 写在后面的话,`App` 及其整条传递依赖图先求值完,
// 期间任何**模块求值期**的 getComputedStyle 都读到空值。而空值喂给 antd ConfigProvider 的
// 失败方式是颜色悄悄退回 antd 默认,不抛错。顺带让令牌在 CSS 级联里先于任何组件样式。
import '@/styles/tokens.css'
// 副作用:建 i18next 实例并接上 store 订阅。与 tokens.css 并列放在这里,理由相同 ——
// **不是**因为"写在 import App 之前所以先执行"(那只是顺带),而是模块求值整体早于渲染,
// 且这两件事都是全局单例的初始化,归属 main 而不是某个组件。
import '@/locales'
import App from './App'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
