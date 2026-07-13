import { defineConfig } from '@playwright/test'

// E2E 打真实前后端(dev 环境)。后端须已在 :5100 跑(dev.bat),否则登录/菜单接口无从谈起。
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  fullyParallel: false, // 同一个超管账号 + 同一份库,串行避免会话/标签互踩
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
