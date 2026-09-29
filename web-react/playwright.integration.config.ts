import { defineConfig } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { createServer } from 'node:net'
import { tmpdir } from 'node:os'
import { join, sep } from 'node:path'
import { resolvePortPair } from './e2e/portPair.mjs'

/**
 * 第三方接入「出站调用 / 可靠投递」e2e 配置:后端换成消费者示例 IntegrationSample(带真实投递适配器),
 * 另起本地可控第三方 IntegrationMockPartner。与主配置分开运行(`npm run test:e2e:integration`),主套件仍用 MinimalHost。
 */
const { apiPort, webPort } = await resolvePortPair({ apiMin: 25000, webMin: 36000, span: 4000 })

/** 第三方的端口:首次解析后写进环境变量,Playwright 工作进程重载配置时沿用同一个。 */
async function freePort(): Promise<number> {
  return await new Promise((resolve, reject) => {
    const server = createServer()
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => {
      const address = server.address()
      server.close(() => (typeof address === 'object' && address ? resolve(address.port) : reject(new Error('no port'))))
    })
  })
}
process.env.TENON_E2E_MOCK_PORT ??= String(await freePort())

const webUrl = `http://127.0.0.1:${webPort}`
const apiUrl = `http://127.0.0.1:${apiPort}`
const mockUrl = `http://127.0.0.1:${process.env.TENON_E2E_MOCK_PORT}`
process.env.TENON_E2E_API_BASE = apiUrl
process.env.TENON_E2E_MOCK_BASE = mockUrl
const adminPassword = process.env.TENON_E2E_PASSWORD ?? 'Aa123456'
const databaseFile = join(tmpdir(), `tenon-admin-e2e-react-itg-${randomUUID()}.db`)
const backendOutput = `${join(tmpdir(), `tenon-admin-e2e-react-itg-build-${randomUUID()}`)}${sep}`
const mockOutput = `${join(tmpdir(), `tenon-admin-e2e-react-mock-build-${randomUUID()}`)}${sep}`

export default defineConfig({
  testDir: './e2e',
  testMatch: /integration-delivery\.spec\.ts/,
  timeout: 90_000,
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: webUrl,
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: `dotnet run --no-launch-profile --project ../backend/samples/IntegrationMockPartner -p:BaseOutputPath=${mockOutput}`,
      url: `${mockUrl}/_mock/stats`,
      reuseExistingServer: false,
      timeout: 120_000,
      env: { MOCK_PARTNER_URL: mockUrl },
    },
    {
      command: `dotnet run --no-launch-profile --project ../backend/samples/IntegrationSample -p:BaseOutputPath=${backendOutput}`,
      url: `${apiUrl}/health`,
      reuseExistingServer: false,
      timeout: 180_000,
      env: {
        ASPNETCORE_URLS: apiUrl,
        ASPNETCORE_ENVIRONMENT: 'Development',
        TenonAdmin__Database__ConnectionString: `Data Source=${databaseFile}`,
        TenonAdmin__Seed__AdminPassword: adminPassword,
        TenonAdmin__Security__RateLimit__Enabled: 'false',
        // 合作方指向本 run 的本地第三方;秘密经环境变量注入(不进配置文件)
        TenonAdmin__Integration__Outbound__Targets__partner__BaseUrl: `${mockUrl}/api/`,
        TenonAdmin__Integration__Outbound__Targets__partner__Secret: 'partner-secret',
        TenonAdmin__Integration__Outbound__Targets__partner__TimeoutSeconds: '5',
        // 受理后的确认轮询间隔压到 5 秒(与投递扫描任务同频),用例不必等默认的 60 秒
        TenonAdmin__Integration__Delivery__ConfirmPollSeconds: '5',
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
