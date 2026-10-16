import { expect, test, type Page } from '@playwright/test'
import { apiAdminToken } from './api'
import { enterSystemApp, login } from './helpers'

// #58:条件分支里有空条件组(没有任何条件)时,设计器的「保存」与「发布」都必须在前端被拦截:
// 一个 update / publish 请求都不能发出,并提示条件组为空。只统计真实发出的请求,不拦截、不 abort。
const apiBase = () => process.env.TENON_E2E_API_BASE ?? 'http://127.0.0.1:5101'
const UPDATE_PATH = '/api/v1/workflow/definition/update'
const PUBLISH_PATH = '/api/v1/workflow/definition/publish'

async function createDraftWithEmptyGroup(page: Page, token: string, name: string): Promise<number> {
  const model = {
    version: 1,
    root: {
      id: 'start', type: 'start', name: '发起',
      next: {
        id: 'branch1', type: 'branch', name: '分支',
        conditions: [
          { id: 'arm', name: '空条件组', isDefault: false, expr: { logic: 'and', children: [] } },
          { id: 'fallback', name: '默认', isDefault: true },
        ],
        next: {
          id: 'approval', type: 'approval', name: '审批',
          props: { assignee: { provider: 'user', params: { userIds: [1] } }, mode: 'any' },
        },
      },
    },
  }
  const res = await (await page.request.post(`${apiBase()}/api/v1/workflow/definition/add`, {
    headers: { Authorization: `Bearer ${token}` }, data: { name, model },
  })).json()
  expect(res.code).toBe(0)
  return Number(res.data)
}

/** 按 pathname 精确统计浏览器发出的 POST;响应码只用于诊断输出(例如后端兜底返回 48002)。 */
function trackDefinitionWrites(page: Page) {
  const counts = { update: 0, publish: 0 }
  const responseCodes: string[] = []
  const kindOf = (url: string) => {
    const path = new URL(url).pathname
    return path === UPDATE_PATH ? 'update' : path === PUBLISH_PATH ? 'publish' : null
  }
  page.on('request', (r) => {
    const kind = kindOf(r.url())
    if (kind && r.method() === 'POST') counts[kind]++
  })
  page.on('response', async (r) => {
    const kind = kindOf(r.url())
    if (!kind || r.request().method() !== 'POST') return
    const body = await r.json().catch(() => null)
    responseCodes.push(`${kind}:${r.status()}:${body?.code ?? 'unparsed'}`)
  })
  return { counts, responseCodes }
}

async function openDesigner(page: Page, request: Parameters<typeof apiAdminToken>[0]) {
  const id = await createDraftWithEmptyGroup(page, await apiAdminToken(request), `空条件组-${Date.now()}`)
  await login(page)
  await enterSystemApp(page)
  const tracker = trackDefinitionWrites(page)
  await page.goto(`/workflow/definition/designer?id=${id}`)
  await expect(page.locator('.wf-card.is-branch').first()).toBeVisible({ timeout: 15_000 })
  return tracker
}

async function expectBlocked(page: Page, tracker: ReturnType<typeof trackDefinitionWrites>) {
  // 先等提示或超时(给可能迟到的请求留出时间),再精确断言一次 update / publish 都没发出,最后断言提示可见。
  const hint = page.getByText(/条件组为空|condition group is empty/i).first()
  await hint.waitFor({ state: 'visible', timeout: 10_000 }).catch(() => {})
  await page.waitForTimeout(1_500)
  console.log(`[#58] definition writes=${JSON.stringify(tracker.counts)} responses=${JSON.stringify(tracker.responseCodes)}`)
  expect(tracker.counts.update, `update 请求应为 0,响应:${tracker.responseCodes.join(',')}`).toBe(0)
  expect(tracker.counts.publish, `publish 请求应为 0,响应:${tracker.responseCodes.join(',')}`).toBe(0)
  await expect(hint).toBeVisible()
}

test('设计器:空条件组点「保存」被前端拦截,不发 update 请求', async ({ page, request }) => {
  const tracker = await openDesigner(page, request)
  await page.getByRole('button', { name: /^保\s*存$|^Save$/i }).click()
  await expectBlocked(page, tracker)
})

test('设计器:空条件组点「发布」被前端拦截,不发 update/publish 请求', async ({ page, request }) => {
  const tracker = await openDesigner(page, request)
  await page.getByRole('button', { name: /^发\s*布$|^Publish$/i }).click()
  await expectBlocked(page, tracker)
})
