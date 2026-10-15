import { expect, test, type Page } from '@playwright/test'
import { apiAdminToken } from './api'
import { enterBusinessApp, login } from './helpers'

// #53:接收人停在「待我审批」时,后端推送 workflow-task-assigned 后列表应自动刷新,不需要手动刷新页面。
const apiBase = () => process.env.TENON_E2E_API_BASE ?? 'http://127.0.0.1:5101'

async function assignTaskToAdmin(page: Page, token: string, key: string) {
  const headers = { Authorization: `Bearer ${token}` }
  const model = {
    version: 1,
    root: {
      id: 'start', type: 'start', name: '发起',
      next: {
        id: 'ap', type: 'approval', name: '实时待办节点',
        props: { assignee: { provider: 'user', params: { userIds: [1] } }, mode: 'any' }, next: null,
      },
    },
  }
  const add = await (await page.request.post(`${apiBase()}/api/v1/workflow/definition/add`, {
    headers, data: { name: `实时待办-${key}`, model },
  })).json()
  expect(add.code).toBe(0)
  const pub = await (await page.request.post(`${apiBase()}/api/v1/workflow/definition/publish`, {
    headers, data: { id: add.data },
  })).json()
  expect(pub.code).toBe(0)
  const started = await (await page.request.post(`${apiBase()}/api/v1/workflow/instance/start`, {
    headers, data: { definitionId: add.data, businessKey: key, variablesJson: '{}' },
  })).json()
  expect(started.code).toBe(0)
}

test('待我审批:收到 workflow-task-assigned 推送后自动刷新列表(不手动刷新)', async ({ page, request }) => {
  // 页面初始化期间可能先建后弃一条连接:只认「最新一条」连接完成握手,避免推送发到尚未就绪的新连接上。
  let wsSeq = 0
  let handshakeSeq = -1
  const assignedFrames: string[] = []
  page.on('websocket', (ws) => {
    if (!ws.url().includes('/hub/realtime')) return
    const seq = ++wsSeq
    ws.on('framereceived', (f) => {
      const text = String(f.payload)
      if (text.startsWith('{}')) handshakeSeq = seq
      if (text.includes('workflow-task-assigned')) assignedFrames.push(text)
    })
  })
  await login(page)
  await enterBusinessApp(page)
  await page.goto('/workflow/todo')
  await expect.poll(() => wsSeq > 0 && handshakeSeq === wsSeq, { timeout: 15_000 }).toBe(true)
  await page.waitForTimeout(1_000)
  await expect.poll(() => handshakeSeq === wsSeq, { timeout: 15_000 }).toBe(true)

  let navigations = 0
  page.on('framenavigated', (f) => { if (f === page.mainFrame()) navigations++ })
  let todoRequests = 0
  page.on('request', (r) => { if (r.url().includes('/api/v1/workflow/task/todo')) todoRequests++ })

  const key = `RT-${Date.now()}`
  await assignTaskToAdmin(page, await apiAdminToken(request), key)

  await expect(page.getByRole('row').filter({ hasText: key })).toBeVisible({ timeout: 15_000 })
  expect(assignedFrames.length).toBeGreaterThan(0)
  expect(todoRequests).toBeGreaterThan(0)
  expect(navigations).toBe(0)
})
