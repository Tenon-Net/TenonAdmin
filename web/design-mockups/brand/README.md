# Tenon 品牌图标 · 交付说明

主色 **#646CFF**。图形为「透榫」——竖榫贯入横梁，圆角图块。

## 文件清单

矢量（首选，任意尺寸清晰）
- `tenon-logo.svg` — 完整应用图标（紫底白榫），固定配色
- `tenon-logo-dark.svg` — 深色背景版本（用于深色模式）
- `tenon-mark.svg` — 仅图形，使用 `currentColor`，颜色随 CSS `color` 变化

位图（PNG，透明外的固定紫底）
- `favicon-16.png` / `favicon-32.png` / `favicon-48.png` — 浏览器标签
- `icon-64.png` / `icon-128.png` / `icon-192.png` / `icon-512.png` — 通用/PWA
- `apple-touch-icon.png` — 180×180，iOS 主屏

其他
- `favicon.ico` — 老浏览器兼容（内含 16/32/48）
- `site.webmanifest` — PWA 清单（按需修改路径）

## 接入方式

把需要的文件放进前端的 `public/` 目录，然后在 `<head>` 内：

```html
<link rel="icon" href="/favicon.ico" sizes="any">
<link rel="icon" type="image/svg+xml" href="/tenon-logo.svg">
<link rel="icon" type="image/png" sizes="32x32" href="/favicon-32.png">
<link rel="icon" type="image/png" sizes="16x16" href="/favicon-16.png">
<link rel="apple-touch-icon" sizes="180x180" href="/apple-touch-icon.png">
<link rel="manifest" href="/site.webmanifest">
<meta name="theme-color" content="#646CFF">
```

## 内联 / React

图形版用 `currentColor`，颜色和大小都用 CSS 控制：

```jsx
export const TenonMark = (props) => (
  <svg viewBox="0 0 120 120" width="1em" height="1em" aria-label="Tenon" {...props}>
    <rect x="16" y="27" width="88" height="26" rx="6" fill="currentColor" fillOpacity="0.5" />
    <rect x="47" y="27" width="26" height="76" rx="6" fill="currentColor" />
  </svg>
);
// <TenonMark style={{ color: '#646CFF', fontSize: 32 }} />
```
