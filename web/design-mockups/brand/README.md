# TenonAdmin 品牌图标交付说明

本目录的图标同步自 [`Tenon-Net/Tenon-Brand`](https://github.com/Tenon-Net/Tenon-Brand)，界面默认主色为 **`#0052D4`**。图形保留「滚筒刷 × T」识别特征，位图中的渐变和立体细节不再用旧 SVG 近似重绘。

这些素材随 TenonAdmin 按 [Apache License 2.0](../../../LICENSE) 分发。

## 文件清单

- `tenon-mark.png`：透明背景版，用于侧栏、登录页等界面内品牌位。
- `favicon-16.png`、`favicon-32.png`、`favicon-48.png`：浅色底板版浏览器标签图标。
- `icon-64.png`、`icon-128.png`、`icon-192.png`、`icon-512.png`：浅色底板版通用与 PWA 图标。
- `apple-touch-icon.png`：180×180 浅色底板版 iOS 主屏图标。
- `favicon.ico`：包含 16、32、48、64、128、256 像素版本的兼容图标。
- `site.webmanifest`：PWA 图标清单。

`apple-touch-icon.png` 和 `icon-192.png` 从品牌仓库的浅色 1024 像素母图导出，其余文件直接取对应规格。固定图标使用浅色底板，避免透明边缘在浏览器和系统的不同背景上失去轮廓。

## 接入方式

把需要的文件放进前端的 `public/` 目录，然后在 `<head>` 内引用：

```html
<link rel="icon" href="/favicon.ico" sizes="any">
<link rel="icon" type="image/png" sizes="32x32" href="/favicon-32.png">
<link rel="icon" type="image/png" sizes="16x16" href="/favicon-16.png">
<link rel="apple-touch-icon" sizes="180x180" href="/apple-touch-icon.png">
<link rel="manifest" href="/site.webmanifest">
<meta name="theme-color" content="#0052D4">
```

界面内直接渲染透明 PNG，不经过 Iconify，也不根据 accent 改色：

```tsx
export function TenonLogo({ size = 32 }) {
  return <img src="/tenon-mark.png" width={size} height={size} alt="TenonAdmin" />
}
```
