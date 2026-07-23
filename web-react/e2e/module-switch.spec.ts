import { test, expect } from '@playwright/test'
import { login, openAppPicker } from './helpers'

test('切换应用后首页和菜单随应用变化，切回后内容仍可渲染', async ({ page }) => {
  test.setTimeout(60_000)
  await login(page)
  const cards = await openAppPicker(page)
  expect(await cards.count()).toBeGreaterThanOrEqual(2)

  const firstCardId = await cards.nth(0).getAttribute('data-testid')
  expect(firstCardId).toBeTruthy()
  await cards.nth(0).click()
  await expect(page.locator('.shell-page > *').first()).toBeVisible()
  const firstHome = new URL(page.url()).pathname

  await openAppPicker(page)
  await page.locator('[data-testid^="module-card-"]').nth(1).click()
  await expect(page.locator('.shell-page > *').first()).toBeVisible()
  expect(new URL(page.url()).pathname).not.toBe(firstHome)

  await openAppPicker(page)
  await page.locator(`[data-testid="${firstCardId}"]`).click()
  await expect(page.locator('.shell-page > *').first()).toBeVisible()
  expect(new URL(page.url()).pathname).toBe(firstHome)
})
