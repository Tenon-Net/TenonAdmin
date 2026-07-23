import { test, expect } from '@playwright/test'
import { enterSystemApp, login } from './helpers'

test('工作台展示后端汇总数据并渲染图表', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)

  const [response] = await Promise.all([
    page.waitForResponse((r) => r.url().includes('/api/v1/dashboard/summary') && r.status() === 200),
    page.goto('/workbench'),
  ])
  const summary = (await response.json()).data
  const values = await page.locator('.dash-stat-val').allInnerTexts()

  expect(values.map((value) => value.trim())).toEqual([
    String(summary.roles),
    String(summary.users),
    String(summary.perms),
    String(summary.onlineSessions),
  ])
  await expect(page.locator('canvas').first()).toBeVisible()
})
