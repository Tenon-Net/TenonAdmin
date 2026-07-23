import { defineConfig } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { tmpdir } from 'node:os'
import { join, sep } from 'node:path'

const webUrl = 'http://localhost:5174'
const apiUrl = process.env.TENON_E2E_API_BASE ?? 'http://localhost:5101'
const adminPassword = process.env.TENON_E2E_PASSWORD ?? 'Aa123456'
// 每次运行使用独立 SQLite 库,避免授权类用例污染下一次运行。
const databaseFile = join(tmpdir(), `tenon-admin-e2e-${randomUUID()}.db`)
// 独立输出目录避免与开发中的 MinimalHost 争用 bin/Debug 下的 DLL。
const backendOutput = `${join(tmpdir(), `tenon-admin-e2e-build-${randomUUID()}`)}${sep}`

export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: webUrl,
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: `dotnet run --no-launch-profile --project ../backend/samples/MinimalHost -p:BaseOutputPath=${backendOutput}`,
      url: `${apiUrl}/health`,
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        ASPNETCORE_URLS: apiUrl,
        ASPNETCORE_ENVIRONMENT: 'Development',
        TenonAdmin__Database__ConnectionString: `Data Source=${databaseFile}`,
        TenonAdmin__Seed__AdminPassword: adminPassword,
      },
    },
    {
      // 直接启动 Vite,让 Playwright 持有实际服务进程,避免 npm 子进程在 Windows 上残留占端口。
      command: 'node ./node_modules/vite/bin/vite.js --port 5174 --strictPort',
      url: webUrl,
      reuseExistingServer: false,
      timeout: 60_000,
      env: { TENON_API_TARGET: apiUrl },
    },
  ],
})
