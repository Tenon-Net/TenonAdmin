import { expect, test } from '@playwright/test'
import { apiAdminToken, apiCreateViewOnlyUser } from './api'
import { login } from './helpers'

// 使用真实种子和普通账号，避免超管旁路掩盖设计器创建后读取定义的 403。
test('普通定义管理员创建、重载、发布和重新打开设计器', async ({ page, request }) => {
  const user = await apiCreateViewOnlyUser(request, await apiAdminToken(request), {
    prefix: 'wf-perm', name: '流程定义管理员',
    menuIds: [48000, 48001, 48023, 48002, 48003, 48004, 48005, 48006, 48007],
  })
  await login(page, user.account, user.password)
  await page.goto('/workflow/definition/designer')
  const name = `权限回归-${Date.now()}`
  await page.getByPlaceholder(/流程名称|Workflow name/i).fill(name)
  const detailResponse = page.waitForResponse((r) => /\/api\/v1\/workflow\/definition\/\d+$/.test(r.url()))
  await page.getByRole('button', { name: /新建草稿|Create draft/i }).click()
  const detail = await detailResponse
  expect(detail.status()).toBe(200)
  expect((await detail.json()).code).toBe(0)
  await expect(page.locator('.wf-card.is-root')).toBeVisible()
  const id = new URL(page.url()).searchParams.get('id')!
  await page.reload()
  await expect(page.locator('.wf-card.is-root')).toBeVisible()
  // #49 起只有开始节点的流程不能发布：设计器先在前端拦下并提示，不应发出发布请求。
  let publishRequests = 0
  page.on('request', (r) => { if (r.url().endsWith('/workflow/definition/publish')) publishRequests++ })
  await page.getByRole('button', { name: /^发布$|^Publish$/i }).click()
  await expect(page.getByText(/流程至少要有一个审批、Webhook 或 AI 决策节点|Add an approval, webhook, or AI decision node/i)).toBeVisible()
  expect(publishRequests).toBe(0)
  // 补一个 Webhook 执行节点再发布：不依赖成员选择器，普通定义管理员即可完成；URL 只写入定义，发布不会调用。
  await page.locator('.wf-tree > .wf-chain > .wf-chain-node').first().locator(':scope > .wf-add .wf-add-btn').click()
  await page.locator('.n-popover:visible').getByRole('button', { name: /^Webhook$/ }).click()
  const drawer = page.locator('.n-drawer:visible')
  await drawer.getByPlaceholder('https://example.com/hooks/workflow').fill('https://example.com/hooks/qa-permission')
  await drawer.getByRole('button', { name: /保存|Save/i }).click()
  await expect(drawer).toBeHidden()
  const published = page.waitForResponse((r) => r.url().endsWith('/workflow/definition/publish'))
  await page.getByRole('button', { name: /^发布$|^Publish$/i }).click()
  expect((await (await published).json()).code).toBe(0)
  await page.goto('/workflow/definition')
  const row = page.locator('.n-data-table-tr').filter({ hasText: name })
  await row.getByRole('button', { name: /设计|Design/i }).click()
  await expect(page).toHaveURL(new RegExp(`designer\\?id=${id}$`))
  await expect(page.locator('.wf-card.is-root')).toBeVisible()
})
