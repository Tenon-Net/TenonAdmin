import { defineConfig } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { tmpdir } from 'node:os'
import { join, sep } from 'node:path'
import { resolvePortPair } from './e2e/portPair.mjs'

/**
 * 收口配置:固定 127.0.0.1 + 本 run 端口;reuseExistingServer 恒 false。
 * 忽略 TENON_WEB_BASE。CI 必须注入唯一 TENON_E2E_*_PORT。
 */
const { apiPort, webPort } = await resolvePortPair({ apiMin: 21000, webMin: 32000, span: 4000 })
const webUrl = `http://127.0.0.1:${webPort}`
const apiUrl = `http://127.0.0.1:${apiPort}`
process.env.TENON_E2E_API_BASE = apiUrl
const adminPassword = process.env.TENON_E2E_PASSWORD ?? 'Aa123456'
const databaseFile = join(tmpdir(), `tenon-admin-e2e-vue-${randomUUID()}.db`)
const backendOutput = `${join(tmpdir(), `tenon-admin-e2e-vue-build-${randomUUID()}`)}${sep}`

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
      command: `node ./node_modules/vite/bin/vite.js --host 127.0.0.1 --port ${webPort} --strictPort`,
      url: webUrl,
      reuseExistingServer: false,
      timeout: 60_000,
      env: { TENON_API_TARGET: apiUrl },
    },
  ],
})
