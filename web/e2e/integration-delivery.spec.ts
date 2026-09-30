import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { login, enterApp, expectAppMessage, SYSTEM_APP } from './helpers'
import { apiAdminToken, apiCreateViewOnlyUser } from './api'

/**
 * 第三方接入(Vue):出站调用与发送任务的管理闭环。后端是消费者示例 IntegrationSample(真实适配器),
 * 对方是本地可控第三方 IntegrationMockPartner;由 playwright.integration.config.ts 启动,业务数据经示例的后台接口产生。
 * 覆盖:失败定位 → 允许的恢复 → 最终结果;结果未知且不可安全重试的记录不能被按钮(或直调接口)绕过;
 * 受理 ≠ 成功;出站记录与目标清单不显示秘密;无操作权限的用户看不到操作按钮;
 * 页面栅栏过期(49046)时关掉操作弹窗并刷新,不带着新栅栏静默重发。
 */

const apiBase = () => process.env.TENON_E2E_API_BASE!
const mockBase = () => process.env.TENON_E2E_MOCK_BASE!
const SECRET = 'partner-secret'
const ROOT_MENU_ID = 50000
const DELIVERY_MENU_ID = 50060
const DELIVERY_PAGE_BUTTON = 50061
const DELIVERY_SUMMARY_BUTTON = 50062
const DELIVERY_DETAIL_BUTTON = 50063

/** DeliveryStatus 数值。 */
const Status = { AwaitingConfirmation: 2, Succeeded: 3, NeedsReconciliation: 4, Failed: 6 } as const
/** 投递详情接口(不含 /page、/summary 与 /{id}/动作)。 */
const DETAIL_PATH = /\/api\/v1\/integration\/delivery\/\d+$/

async function script(request: APIRequestContext, route: string, behaviors: string[]) {
  const res = await request.post(`${mockBase()}/_mock/script`, { data: { route, behaviors } })
  expect(res.ok()).toBeTruthy()
}

async function createTicket(request: APIRequestContext, token: string, title: string, channel = 'modern') {
  const res = await request.post(`${apiBase()}/api/v1/sample/partner-ticket`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { title, partnerCode: 'acme', amount: 88, channel },
  })
  const body = (await res.json()) as { code: number; data: { id: string | number; deliveryKey: string } }
  expect(body.code).toBe(0)
  return body.data
}

async function deliveryByKey(request: APIRequestContext, token: string, key: string) {
  const res = await request.get(`${apiBase()}/api/v1/integration/delivery/page`, {
    headers: { Authorization: `Bearer ${token}` },
    params: { DeliveryKey: key, Current: 1, Size: 1 },
  })
  const body = (await res.json()) as { data: { items: { id: string | number; status: number; remoteReference?: string }[] } }
  return body.data.items[0]
}

/** 等后台投递任务(每 5 秒)把记录推进到目标状态。 */
async function waitStatus(request: APIRequestContext, token: string, key: string, status: number) {
  await expect
    .poll(async () => (await deliveryByKey(request, token, key))?.status, { timeout: 45_000, intervals: [1_000] })
    .toBe(status)
  return deliveryByKey(request, token, key)
}

function deliveryDrawer(page: Page): Locator {
  return page.locator('.n-drawer').filter({ has: page.getByTestId('delivery-key') })
}

async function openDelivery(page: Page, key: string): Promise<Locator> {
  await page.goto(`/integration/delivery?deliveryKey=${encodeURIComponent(key)}`)
  const row = page.locator('.n-data-table-tr').filter({ hasText: key })
  await expect(row).toHaveCount(1, { timeout: 15_000 })
  await row.getByTestId('delivery-detail').click()
  const drawer = deliveryDrawer(page)
  await expect(drawer.getByTestId('delivery-key')).toHaveText(key)
  return drawer
}

async function refreshDrawer(drawer: Locator) {
  await drawer.getByRole('button', { name: /^刷新$|^Refresh$/ }).click()
}

function actionModal(page: Page) {
  return page.locator('.n-modal').filter({ has: page.getByTestId('delivery-action-note') })
}

test.describe('第三方接入 · 出站调用与发送任务(Vue)', () => {
  test.describe.configure({ mode: 'serial' })

  test('失败定位 → 重试恢复 → 成功;出站记录可追溯且不显示秘密', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    await script(request, 'tickets', ['reject'])
    const ticket = await createTicket(request, token, `e2e-fail-${Date.now().toString(36)}`)
    await waitStatus(request, token, ticket.deliveryKey, Status.Failed)

    await login(page)
    await enterApp(page, SYSTEM_APP)
    const drawer = await openDelivery(page, ticket.deliveryKey)
    await expect(drawer.getByTestId('delivery-status')).toHaveText(/失败|Failed/)
    await expect(drawer.getByTestId('delivery-hint')).toContainText(/拒绝|rejected/)
    // 按钮可点与否只看服务端判定:失败可重试/取消,不可「确认已成功」
    await expect(drawer.getByTestId('delivery-action-retry')).toBeEnabled()
    await expect(drawer.getByTestId('delivery-action-confirm-succeeded')).toBeDisabled()

    await drawer.getByTestId('delivery-action-retry').click()
    await actionModal(page).getByTestId('delivery-action-note').locator('textarea').fill('对方已调整校验规则')
    await actionModal(page).getByRole('button', { name: /^重试$|^Retry$/ }).click()
    await expectAppMessage(page, /操作已执行|Action completed/)
    await expect(drawer.getByTestId('delivery-status')).toHaveText(/待处理|Pending/)

    await waitStatus(request, token, ticket.deliveryKey, Status.Succeeded)
    await refreshDrawer(drawer)
    await expect(drawer.getByTestId('delivery-status')).toHaveText(/成功|Succeeded/)
    const attempts = drawer.getByTestId('delivery-attempts').locator('.n-data-table-tr')
    await expect(attempts.filter({ hasText: /对方拒绝|Rejected/ })).toHaveCount(1)
    await expect(attempts.filter({ hasText: /人工操作|Manual action/ })).toHaveCount(1)
    await expect(attempts.filter({ hasText: /对方已调整校验规则/ })).toHaveCount(1)

    // 出站记录:从投递详情带 deliveryId 深链进入,两次发送都在,且刷新后筛选保持
    await drawer.getByRole('button', { name: /查看出站调用|View outbound calls/ }).click()
    await expect(page).toHaveURL(/\/integration\/outbound-log\?deliveryId=/)
    const calls = page.locator('.n-data-table .n-data-table-tr').filter({ hasText: /\/api\/tickets/ })
    await expect(calls).toHaveCount(2, { timeout: 10_000 })
    await page.reload()
    await expect(page.locator('.n-data-table .n-data-table-tr').filter({ hasText: /\/api\/tickets/ })).toHaveCount(2, { timeout: 10_000 })

    // 目标清单只回答「已配置」,不显示秘密
    await page.getByTestId('outbound-targets').click()
    const targets = page.locator('.n-drawer').filter({ hasText: /已配置的出站目标|Configured outbound targets/ })
    await expect(targets.locator('.n-data-table-tr').filter({ hasText: 'partner' })).toContainText(/已配置|Configured/)
    expect(await page.content()).not.toContain(SECRET)
  })

  test('结果未知且对方不去重:不能重试,须写明核实说明后确认', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    await script(request, 'tickets-nodedupe', ['drop'])
    const ticket = await createTicket(request, token, `e2e-unknown-${Date.now().toString(36)}`, 'legacy')
    const row = await waitStatus(request, token, ticket.deliveryKey, Status.NeedsReconciliation)

    // 直调接口也绕不过服务端判定
    const bypass = await request.post(`${apiBase()}/api/v1/integration/delivery/${row.id}/retry`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {},
    })
    expect(((await bypass.json()) as { code: number }).code).toBe(49045)

    await login(page)
    await enterApp(page, SYSTEM_APP)
    const drawer = await openDelivery(page, ticket.deliveryKey)
    await expect(drawer.getByTestId('delivery-status')).toHaveText(/待核对|Needs reconciliation/)
    await expect(drawer.getByTestId('delivery-hint')).toContainText(/不会自动重发|will not be resent/)
    await expect(drawer.getByTestId('delivery-action-retry')).toBeDisabled()
    await expect(drawer.getByTestId('delivery-action-query')).toBeDisabled()

    await drawer.getByTestId('delivery-action-confirm-succeeded').click()
    await actionModal(page).getByRole('button', { name: /^确认已成功$|^Confirm succeeded$/ }).click()
    await expectAppMessage(page, /须填写处理说明|require a resolution note/)
    await expect(actionModal(page)).toBeVisible()   // 缺说明不关闭
    await actionModal(page).getByTestId('delivery-action-note').locator('textarea').fill('已在对方后台核实工单已创建')
    await actionModal(page).getByRole('button', { name: /^确认已成功$|^Confirm succeeded$/ }).click()
    await expectAppMessage(page, /操作已执行|Action completed/)
    await expect(drawer.getByTestId('delivery-status')).toHaveText(/成功|Succeeded/)
    await expect(drawer.getByTestId('delivery-action-cancel')).toBeDisabled()   // 终态:再无可用操作
  })

  test('受理不等于成功:对方完成后经轮询确认', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    await script(request, 'tickets', ['accept'])
    const ticket = await createTicket(request, token, `e2e-accept-${Date.now().toString(36)}`)
    const accepted = await waitStatus(request, token, ticket.deliveryKey, Status.AwaitingConfirmation)

    await login(page)
    await enterApp(page, SYSTEM_APP)
    const drawer = await openDelivery(page, ticket.deliveryKey)
    await expect(drawer.getByTestId('delivery-status')).toHaveText(/已受理待确认|Accepted, awaiting confirmation/)
    await expect(drawer.getByTestId('delivery-hint')).toContainText(/不是成功|not success/)

    // 对方处理完成 → 投递器下一次轮询确认成功
    const done = await request.post(`${mockBase()}/_mock/tickets/${accepted.remoteReference}/complete`, { data: { status: 'done' } })
    expect(done.ok()).toBeTruthy()
    await waitStatus(request, token, ticket.deliveryKey, Status.Succeeded)
    await refreshDrawer(drawer)
    await expect(drawer.getByTestId('delivery-status')).toHaveText(/成功|Succeeded/)
    await expect(drawer.getByTestId('delivery-attempts').locator('.n-data-table-tr').filter({ hasText: /查询|Query/ }).first()).toBeVisible()
  })

  test('只有查看权限的用户看不到操作按钮', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    await script(request, 'tickets', ['reject'])
    const ticket = await createTicket(request, token, `e2e-view-${Date.now().toString(36)}`)
    await waitStatus(request, token, ticket.deliveryKey, Status.Failed)

    const viewer = await apiCreateViewOnlyUser(request, token, {
      prefix: 'dlv',
      name: '投递只读',
      menuIds: [ROOT_MENU_ID, DELIVERY_MENU_ID, DELIVERY_PAGE_BUTTON, DELIVERY_SUMMARY_BUTTON, DELIVERY_DETAIL_BUTTON],
    })

    await login(page, viewer.account, viewer.password)
    const menuItem = page.locator('.sidenav .n-menu-item-content').filter({ hasText: /^发送任务$|^Delivery Tasks$/ })
    await expect(menuItem).toBeVisible({ timeout: 15_000 })
    await menuItem.click()
    await expect(page).toHaveURL(/\/integration\/delivery/)
    const row = page.locator('.n-data-table-tr').filter({ hasText: ticket.deliveryKey })
    await expect(row).toHaveCount(1, { timeout: 10_000 })
    await row.getByTestId('delivery-detail').click()
    const drawer = deliveryDrawer(page)
    await expect(drawer.getByTestId('delivery-status')).toHaveText(/失败|Failed/)
    await expect(drawer.locator('[data-testid^="delivery-action-"]')).toHaveCount(0)
    await expect(drawer.getByRole('button', { name: /查看出站调用|View outbound calls/ })).toHaveCount(0)
  })

  test('页面栅栏过期(49046):关掉操作弹窗并刷新详情,不带新栅栏静默重发', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    await script(request, 'tickets', ['reject'])
    const ticket = await createTicket(request, token, `e2e-fence-${Date.now().toString(36)}`)
    await waitStatus(request, token, ticket.deliveryKey, Status.Failed)

    // 首次取详情时把栅栏改旧,等价于「页面打开后记录已被投递器或他人改动」;之后的请求原样放行
    let staleServed = false
    await page.route((url) => DETAIL_PATH.test(url.pathname), async (route) => {
      const response = await route.fetch()
      if (staleServed || route.request().method() !== 'GET') return route.fulfill({ response })
      staleServed = true
      const body = (await response.json()) as { data: { fence: number | string } }
      body.data.fence = Number(body.data.fence) + 1000
      await route.fulfill({ response, json: body })
    })

    await login(page)
    await enterApp(page, SYSTEM_APP)
    const drawer = await openDelivery(page, ticket.deliveryKey)
    await drawer.getByTestId('delivery-action-retry').click()
    const refreshed = page.waitForResponse((r) => DETAIL_PATH.test(new URL(r.url()).pathname) && r.request().method() === 'GET')
    await actionModal(page).getByRole('button', { name: /^重试$|^Retry$/ }).click()
    await expectAppMessage(page, /投递记录已变化|delivery record has changed/)
    await expect(actionModal(page)).toHaveCount(0)   // 弹窗关闭:操作人按刷新后的状态重新判断
    await refreshed
    await expect(drawer.getByTestId('delivery-status')).toHaveText(/失败|Failed/)
    await expect(drawer.getByTestId('delivery-action-retry')).toBeEnabled()
    expect((await deliveryByKey(request, token, ticket.deliveryKey)).status).toBe(Status.Failed)   // 重试没有执行
  })
})
