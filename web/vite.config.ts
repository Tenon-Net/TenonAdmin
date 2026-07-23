/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// 版本号 = package.json 的 version,构建期注入前端(登录页页脚展示)。打包即固化,不走后端配置。
const appVersion = JSON.parse(readFileSync(fileURLToPath(new URL('./package.json', import.meta.url)), 'utf-8')).version

// CORS 默认 deny-all,浏览器只与 :5173 通信,/api 与 /openapi 由 dev proxy 反代到后端。
// 后端 dev 端口默认 5100(避开 macOS AirPlay 占的 5000);dev.sh 经 TENON_API_TARGET 覆盖。
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

export default defineConfig({
  plugins: [vue()],
  define: { __APP_VERSION__: JSON.stringify(appVersion) },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      // 改图标包 bug 时:`NIP_LOCAL=1 npm run dev` → 直连兄弟仓库 src、HMR;不设则吃已发布 dist。
      // 完整回路(改 → 发补丁版 → bump)见 src/lib/icons.ts 顶部注释。
      ...(process.env.NIP_LOCAL
        ? { 'tenon-naive-iconify-picker': fileURLToPath(new URL('../../tenon-naive-iconify-picker/src/index.ts', import.meta.url)) }
        : {}),
      // 表格包同款回路:`NPT_LOCAL=1 npm run dev` → 直连兄弟仓库 src、HMR。
      ...(process.env.NPT_LOCAL
        ? { 'tenon-naive-pro-table': fileURLToPath(new URL('../../tenon-naive-pro-table/src/index.ts', import.meta.url)) }
        : {}),
    },
    // 别名生效时强制单份 peer,防双 Vue 实例(invalid hook / useThemeVars 失效);平时无害。
    dedupe: ['vue', 'naive-ui', '@iconify/vue'],
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/openapi': { target: apiTarget, changeOrigin: true },
      '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR 实时通知 Hub;ws:true 反代 WebSocket 升级
    },
  },
  test: {
    environment: 'happy-dom',
    include: ['src/**/*.spec.ts'], // 必须限定 src:web/e2e 的 Playwright 用例也叫 *.spec.ts,默认 glob 会误吞
    globals: false,
    pool: 'forks',
    restoreMocks: true,
    unstubGlobals: true,
  },
})
