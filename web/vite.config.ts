import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// CORS 默认 deny-all,浏览器只与 :5173 通信,/api 与 /openapi 由 dev proxy 反代到后端 :5000。
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      // 改图标包 bug 时:`NIP_LOCAL=1 npm run dev` → 直连兄弟仓库 src、HMR;不设则吃已发布 dist。
      ...(process.env.NIP_LOCAL
        ? { 'tenon-naive-iconify-picker': fileURLToPath(new URL('../../tenon-naive-iconify-picker/src/index.ts', import.meta.url)) }
        : {}),
    },
    // 别名生效时强制单份 peer,防双 Vue 实例(invalid hook / useThemeVars 失效);平时无害。
    dedupe: ['vue', 'naive-ui', '@iconify/vue'],
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5000', changeOrigin: true },
      '/openapi': { target: 'http://localhost:5000', changeOrigin: true },
    },
  },
})
