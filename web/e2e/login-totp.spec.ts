import { expect, test, type Page } from '@playwright/test'

const SESSION = {
  accessToken: 'e2e-totp-access',
  expiresAt: '2099-01-01T00:00:00Z',
  refreshToken: 'e2e-totp-refresh',
  refreshExpiresAt: '2099-02-01T00:00:00Z',
  userId: 42,
  account: 'totp-user',
  name: 'TOTP User',
  mustChangePassword: false,
}

const envelope = (data: unknown, code = 0, args?: Record<string, unknown>) => ({
  code,
  msg: code === 0 ? 'ok' : 'error',
  data,
  args,
})

async function mockPortalBootstrap(page: Page) {
  await page.route('**/api/v1/personal/modules', (route) =>
    route.fulfill({ json: envelope({ modules: [], defaultModuleId: null }) }))
  await page.route('**/api/v1/personal/permissions', (route) =>
    route.fulfill({ json: envelope([]) }))
  await page.route('**/api/v1/personal/profile', (route) =>
    route.fulfill({
      json: envelope({
        id: 42,
        account: 'totp-user',
        name: 'TOTP User',
        isSuperAdmin: false,
      }),
    }))
  await page.route('**/api/v1/sys/config/site-info', (route) =>
    route.fulfill({
      json: envelope({
        title: 'TenonAdmin',
        captchaEnabled: false,
        smsLoginEnabled: false,
      }),
    }))
  await page.route('**/api/v1/auth/external/providers', (route) =>
    route.fulfill({ json: envelope([]) }))
}

test('login TOTP challenge (40018) completes and enters app', async ({ page }) => {
  await mockPortalBootstrap(page)

  await page.route('**/api/v1/auth/login', async (route) => {
    if (route.request().method() !== 'POST') return route.continue()
    await route.fulfill({
      status: 400,
      json: envelope(null, 40018, { challengeId: 'chal-totp-e2e' }),
    })
  })

  let totpBody: { challengeId?: string; code?: string } | null = null
  await page.route('**/api/v1/auth/login/totp', async (route) => {
    totpBody = await route.request().postDataJSON() as { challengeId?: string; code?: string }
    await route.fulfill({ json: envelope(SESSION) })
  })

  await page.goto('/login')
  await page.getByPlaceholder(/账号|account/i).fill('totp-user')
  await page.getByPlaceholder(/密码|password/i).first().fill('Secret1!')
  await page.locator('button.hero-btn').click()

  await expect(page.getByText(/动态口令|Authenticator|二次验证|TOTP/i).first()).toBeVisible({ timeout: 10_000 })

  await page.getByPlaceholder(/动态口令|6.?位|totp/i).fill('123456')
  await page.locator('button.hero-btn').click()

  await expect(page).not.toHaveURL(/\/login/, { timeout: 15_000 })
  expect(totpBody).toEqual({ challengeId: 'chal-totp-e2e', code: '123456' })
})
