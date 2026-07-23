import { expect, type Page } from '@playwright/test'

export const ADMIN_ACCOUNT = process.env.TENON_E2E_ACCOUNT ?? 'superAdmin'
export const ADMIN_PASSWORD = process.env.TENON_E2E_PASSWORD ?? 'Aa123456'

export async function login(page: Page, account = ADMIN_ACCOUNT, password = ADMIN_PASSWORD) {
  await page.goto('/login')
  await page.getByPlaceholder(/账号|account/i).fill(account)
  await page.getByPlaceholder(/密码|password/i).first().fill(password)

  const captchaSvg = page.locator('[data-testid="captcha-img"] svg')
  if (await captchaSvg.isVisible().catch(() => false)) {
    const code = (await captchaSvg.locator('text').allInnerTexts()).join('')
    await page.getByPlaceholder(/验证码|captcha/i).fill(code)
  }

  await page.getByRole('button', { name: /登\s*录|sign\s*in/i }).click()
  await expect(page).not.toHaveURL(/\/login/, { timeout: 15_000 })
}

export async function openAppPicker(page: Page) {
  if (!/\/module/.test(page.url())) {
    // 点击会立即卸载整个布局壳,普通 click 的稳定性重试会抓住已分离的旧按钮。
    await page.getByRole('button', { name: /切换应用|switch app/i }).dispatchEvent('click')
  }
  await expect(page).toHaveURL(/\/module/)
  const cards = page.locator('[data-testid^="module-card-"]')
  await expect(cards.first()).toBeVisible()
  return cards
}

export async function enterApp(page: Page, title: RegExp) {
  const cards = await openAppPicker(page)
  const card = cards.filter({ hasText: title })
  await expect(card).toHaveCount(1)
  await card.click()
  await expect(page).not.toHaveURL(/\/module/, { timeout: 10_000 })
}

export async function enterSystemApp(page: Page) {
  await enterApp(page, /系统|System/i)
}
