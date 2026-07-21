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
    // **必须 strictPort**。默认行为是端口被占就静默挪到下一个可用端口 —— 而本仓库同时跑两个模板,
    // 5173/5174 相邻,残留的 dev server 很容易占位。挪走之后:脚本、探针、文档里写死的 5174 会连上
    // **另一个应用**,拿到的不是错误而是别人的页面。真踩过一次(探针连到 Vue 模板,报"什么都没渲染")。
    // 宁可起不来。
    strictPort: true,
    // 这里**故意没有 `fs.allow`**。共享层时期它是必需的(源码在项目根之外),而那一条设置
    // 会**整个替换**默认白名单,写漏一项就是 dev server 连 `GET /` 都 403,且 lint/typecheck/build
    // 一个都发现不了(真踩过)。自包含之后不需要它,实测三个敏感路径(后端 appsettings.Development.json、
    // dev-jwt.key、仓库根 CLAUDE.md)经 `/@fs/` 全部 403,比原先的 `['.', '../web-shared']` 严格更紧。
    //
    // 但**默认值不是"项目根"**(这一点我原先写错了,R3 review 纠正):Vite 的默认是
    // `searchForWorkspaceRoot()` —— 逐级上溯找 `pnpm-workspace.yaml` / `lerna.json` / 带 `workspaces`
    // 字段的 `package.json`,找不到才退回起点。今天解析到 `web-react/` 自己,**仅仅因为仓库根还没有
    // package.json**。哪天仓库根引入 workspaces 或 pnpm-workspace.yaml(一仓两模板,这是很自然的下一步),
    // 白名单会**静默扩张到仓库根**,上面那三个路径立刻可读,而四件套一个都不会响。
    // 到那时必须在这里显式写回 `fs: { allow: ['.'] }`。
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/openapi': { target: apiTarget, changeOrigin: true },
      '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR 实时通知 Hub;ws:true 反代 WebSocket 升级
    },
  },
  test: {
    environment: 'happy-dom',
    // 气隙自包含硬约束:**任何测试都不得触网**。md-editor-rt 的 MdPreview/MdEditor 挂载时会往 DOM 注入
    // 指向 unpkg 的 <link>/<script>(highlight/katex/mermaid 等懒加载资产),happy-dom 会真去 fetch ——
    // 联网机上 TLS 超时拖慢整套件、离线 CI 里则是网络失败噪声。这里从根上关掉外部资源加载(一处堵住所有测试),
    // 并把「已禁用的加载」当成功返回,消掉 teardown 期的 AbortError/NetworkError 噪声。
    // 注:tokens.css 是 Vite `css:true` 内联进 <style> 的,不是外部 link,不受影响(主题桥 spec 照常读得到)。
    // (应用运行时同样会拉 unpkg —— 那是**产品级**自包含缺口,两模板都有,单列 backlog,不在此处解决。)
    environmentOptions: {
      happyDOM: { settings: { disableJavaScriptFileLoading: true, disableCSSFileLoading: true, handleDisabledFileLoadingAsSuccess: true } },
    },
    // 主题桥的 spec 靠 `getComputedStyle` 读回 tokens.css 的变量,所以 CSS 必须真处理。
    // 默认 `css: false` 会把 CSS 导入桩成空 —— **连 `?raw` 也是空串**,不报错。
    // 那样的话 `v()` 全返回空串、`defined()` 把键全滤掉,spec 里每条恒等断言都变成恒真。
    css: true,
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
