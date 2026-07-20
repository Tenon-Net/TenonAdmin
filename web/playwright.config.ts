import { defineConfig } from '@playwright/test'

// E2E 打真实前后端(dev 环境)。后端须已在 :5100 跑(dev.bat),否则登录/菜单接口无从谈起。
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  // 同一个超管账号 + 同一份库,必须串行,否则会话/标签/授权互踩。
  // **真正起作用的是 workers:1**(实测从 "7 tests using 3 workers" 变成 "using 1 worker")——
  // 只有一个 worker 进程,并行无从谈起。`fullyParallel: false` 本就是 Playwright 默认值,
  // 单独设它拦不住跨文件并发(它只管文件内),留在这儿是把"必须串行"这个意图写明,不是第二道闸。
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: process.env.TENON_WEB_BASE ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  webServer: {
    command: 'npm run dev',
    url: process.env.TENON_WEB_BASE ?? 'http://localhost:5173',
    reuseExistingServer: true, // 本地已 dev.bat 起着就直接复用
    timeout: 60_000,
  },
})
