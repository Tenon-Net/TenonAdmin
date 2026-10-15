import { expect, test, type Page } from '@playwright/test'
import { apiAdminToken } from './api'
import { enterSystemApp, login } from './helpers'

// #58:条件分支里有空条件组(没有任何条件)时,设计器点「发布」应被拦截并提示,不能发布成功。
const apiBase = () => process.env.TENON_E2E_API_BASE ?? 'http://127.0.0.1:5101'

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

test('设计器:空条件组的分支不能发布,并提示条件组为空', async ({ page, request }) => {
  const name = `空条件组-${Date.now()}`
  const id = await createDraftWithEmptyGroup(page, await apiAdminToken(request), name)

  await login(page)
  await enterSystemApp(page)
  const publishCodes: number[] = []
  page.on('response', async (r) => {
    if (r.url().includes('/api/v1/workflow/definition/publish') && r.request().method() === 'POST') {
      publishCodes.push((await r.json().catch(() => ({ code: -1 }))).code)
    }
  })
  await page.goto(`/workflow/definition/designer?id=${id}`)
  await expect(page.locator('.wf-card.is-branch, .wf-node-branch, [data-node-type="branch"]').first()).toBeVisible({ timeout: 15_000 })

  await page.getByRole('button', { name: /^发\s*布$|^Publish$/i }).click()
  await expect(page.getByText(/条件组为空|condition group is empty/i).first()).toBeVisible({ timeout: 10_000 })
  await page.waitForTimeout(1_000)
  expect(publishCodes).not.toContain(0)
})
