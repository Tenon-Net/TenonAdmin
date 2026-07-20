import { useLayoutEffect, useState } from 'react'
import { theme, type ThemeConfig } from 'antd'
import { buildAntdTheme, type ThemeOpts } from './antd-theme'

// 注:Vue 版还有个 `grayscale`(哀悼模式)。这里**故意不带** —— 灰阶只是 `<html>` 上的一个 CSS filter,
// 不改 antd 主题,混进本 hook 的依赖数组只会让"切灰阶"白重建整棵 ConfigProvider。
// B8 已把它落到独立的 `theme/useDocumentGrayscale.ts`(App 里调),消费它的 `[data-gray]` 规则在
// `styles/chrome.css`。**别在这里重复实现。**

/**
 * 主题落地。对应 Vue 版的 `composables/useTheme.ts`,三件事同源:
 *  1) 把 `data-theme` / `data-density` / `data-gray` 打到 `<html>`(裸 CSS 与 tokens 跟着翻);
 *  2) 把 accent 派生的 `--color-primary*` 写到 `<html>`(令消费 tokens 的裸 CSS 换色);
 *  3) 重建 antd 的 `ThemeConfig`。
 *
 * **① 必须排在 ②③ 前面。** `getComputedStyle` 读的是**当前** `data-theme` 下的值:先读后翻的话,
 * 暗色主题会拿到亮色的变量,而且永远慢一拍 —— 切到暗色显示亮色、再切回亮色显示暗色。
 *
 * 用 `useLayoutEffect` 而不是 `useEffect`:后者在浏览器绘制**之后**才跑,切主题会闪一帧旧配色。
 *
 * B3 落 Zustand 后,入参改从 `app` store 取;这一层的逻辑不变。
 */
export function useAntdTheme(opts: ThemeOpts): ThemeConfig {
  const { dark, accent, density } = opts
  // 初值故意是空对象,**不在这里先算一遍**:首次渲染时 `data-theme` 还没打上,算出来的必然是亮色变量。
  // 今天初值 dark=false 恰好对得上,但 B3 接上持久化偏好后,偏好为暗色的用户首帧会得到
  // 「亮色变量 + darkAlgorithm」这种四不像,整棵 ConfigProvider 白渲染一遍再被下面覆盖。
  // (不会闪 —— useLayoutEffect 的 setConfig 在绘制前同步 flush —— 但那是浪费,且读起来像是有意为之。)
  const [config, setConfig] = useState<ThemeConfig>({})

  useLayoutEffect(() => {
    const el = document.documentElement

    // ① 先翻属性 —— 下面两步都依赖 getComputedStyle 读到**新**主题下的值
    el.setAttribute('data-theme', dark ? 'dark' : '')
    el.setAttribute('data-density', density)

    // ② 重建 antd 配置(新对象,触发 ConfigProvider 重渲染)
    const next = buildAntdTheme({ dark, accent, density })

    // ③ 把 **antd 最终解析出来的**主色写回 CSS 变量,供不经 antd 的裸 CSS 消费(布局壳、登录页那些手写样式)。
    //
    // 这里**不能**直接写共享层的种子色。分工是:共享层只管「accent → 种子」,色阶归各 UI 库 ——
    // 而 antd 的 `darkAlgorithm` 会拿种子再生成一整条暗色调色板,`colorPrimary` 是派生结果而非种子。
    // 实测 accent #646CFF:暗色种子 #8086FF,antd 实际用 #7075DC。若这里写种子,同一页面上
    // 「antd 按钮」和「用 var(--color-primary) 的元素」会是**两种紫** —— 亮色下恰好相等,只有暗色露馅。
    // 以 antd 为准,模板内部才自洽。两个模板暗色主色因此不同,这是有意的:各 UI 库有各自的主题体系。
    const t = theme.getDesignToken(next)
    el.style.setProperty('--color-primary', t.colorPrimary)
    el.style.setProperty('--color-primary-hover', t.colorPrimaryHover)
    el.style.setProperty('--color-primary-pressed', t.colorPrimaryActive)
    el.style.setProperty('--color-primary-light', t.colorPrimaryBg)

    setConfig(next)
  }, [dark, accent, density])

  return config
}
