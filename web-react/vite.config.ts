/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 版本号 = package.json 的 version,构建期注入前端(登录页页脚展示)。打包即固化,不走后端配置。
const appVersion = JSON.parse(readFileSync(fileURLToPath(new URL('./package.json', import.meta.url)), 'utf-8')).version

// CORS 默认 deny-all,浏览器只与本 dev server 通信,/api 与 /openapi 由 dev proxy 反代到后端。
// 后端 dev 端口默认 5100(避开 macOS AirPlay 占的 5000);dev.sh 经 TENON_API_TARGET 覆盖。
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

export default defineConfig({
  plugins: [react()],
  define: { __APP_VERSION__: JSON.stringify(appVersion) },
  resolve: {
    // 只有一条 alias。本模板**自包含**:不引 `web-shared/`、不引 `../web`,
    // 因此不需要给裸包指路,也不需要放行项目根之外的路径(见下面 server 段的说明)。
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    // 5174 而不是 5173:两个模板要能同时跑。B12 的验收就是拿 React 版逐条对照 Vue 版,
    // 端口撞车的话每次对照都得先停一个。
    port: 5174,
    // 这里**故意没有 `fs.allow`**。共享层时期它是必需的(源码在项目根之外),而那一条设置
    // 会**整个替换**默认白名单,写漏一项就是 dev server 连 `GET /` 都 403,且 lint/typecheck/build
    // 一个都发现不了(真踩过)。自包含之后源码全在项目根内,默认值即最紧,别再加回来。
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/openapi': { target: apiTarget, changeOrigin: true },
      '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR 实时通知 Hub;ws:true 反代 WebSocket 升级
    },
  },
  test: {
    environment: 'happy-dom',
    // 限定 src:将来 web-react/e2e 落地时 Playwright 用例也会叫 *.spec.ts,默认 glob 会误吞(web/ 已踩过)。
    // `.tsx` 不能少:组件测试必须带 JSX 才写得了,而漏掉的失败模式是**彻底静默** ——
    // 文件不匹配 glob,vitest 不报错(别的文件还在跑),CI 全绿,那些用例从来没执行过。
    include: ['src/**/*.spec.{ts,tsx}'],
    // 必须先于 store 模块加载(persist 建 store 时就取走 localStorage),见文件内注释。
    setupFiles: ['src/test-setup.ts'],
    globals: false,
    pool: 'forks',
    maxWorkers: 1,
    fileParallelism: false, // 本机 node 堆 ~3GB,串行保内存曲线可预测;CI 不分叉同配置
    restoreMocks: true,
    unstubGlobals: true,
  },
})
