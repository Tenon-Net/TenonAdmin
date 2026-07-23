import { test, expect } from '@playwright/test'
import { login } from './helpers'

test('受限账号只能进入已授权页面,且服务端拒绝未授权接口', async ({ browser, request }) => {
  test.setTimeout(60_000)
  const adminContext = await browser.newContext()
  const adminPage = await adminContext.newPage()
  await login(adminPage)

  const adminToken = await adminPage.evaluate(() => {
    const persisted = JSON.parse(localStorage.getItem('user') ?? '{}')
    return persisted.state.accessToken as string
  })
  const grant = await request.put('/api/v1/sys/role/menu', {
    headers: { Authorization: `Bearer ${adminToken}` },
    data: { roleId: 2, menuIds: [108] },
  })
  expect(grant.ok()).toBeTruthy()
  await adminContext.close()

  // 新上下文保证不会沿用超管的 zustand 内存态、存储或请求中间件令牌。
  const restrictedContext = await browser.newContext()
  const page = await restrictedContext.newPage()
  await login(page, '全部数据', '123456')
  await expect(page).toHaveURL(/\/workbench$/, { timeout: 15_000 })
  await expect(page.locator('.dash-stat-val').first()).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText(/用户管理|User management/i)).toHaveCount(0)

  const restrictedToken = await page.evaluate(() => {
    const persisted = JSON.parse(localStorage.getItem('user') ?? '{}')
    return persisted.state.accessToken as string
  })
  const denied = await request.get('/api/v1/sys/user/page?Current=1&Size=10', {
    headers: { Authorization: `Bearer ${restrictedToken}` },
  })
  expect(denied.status()).toBe(403)

  await page.goto('/system/user')
  await expect(page.getByText(/页面不存在|Page not found/i)).toBeVisible()
  await restrictedContext.close()
})
