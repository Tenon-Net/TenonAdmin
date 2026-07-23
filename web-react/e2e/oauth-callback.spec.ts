import { expect, test, type Page } from '@playwright/test'

const SESSION = {
  accessToken: 'oauth-access-token',
  expiresAt: '2099-01-01T00:00:00Z',
  refreshToken: 'oauth-refresh-token',
  refreshExpiresAt: '2099-02-01T00:00:00Z',
  userId: 7,
  account: 'oauth-user',
  name: 'OAuth User',
  mustChangePassword: false,
}

const envelope = (data: unknown) => ({ code: 0, msg: 'ok', data })

async function mockPortalBootstrap(page: Page) {
  await page.route('**/api/v1/personal/modules', (route) => route.fulfill({ json: envelope({ modules: [], defaultModuleId: null }) }))
  await page.route('**/api/v1/personal/permissions', (route) => route.fulfill({ json: envelope([]) }))
  await page.route('**/api/v1/personal/profile', (route) => route.fulfill({
    json: envelope({ id: 7, account: 'oauth-user', name: 'OAuth User', isSuperAdmin: false }),
  }))
}

test('OAuth callback is public and returns mapped failures to sign-in', async ({ page }) => {
  await page.goto('/oauth/callback?error=40016')

  await expect(page.getByText(/第三方账号尚未绑定|third-party account is not bound/i)).toBeVisible()
  await expect(page).toHaveURL(/\/login$/, { timeout: 5_000 })
})

test('OAuth ticket is exchanged, persisted, and enters the portal', async ({ page }) => {
  await mockPortalBootstrap(page)
  let exchangedTicket = ''
  await page.route('**/api/v1/auth/external/exchange', async (route) => {
    exchangedTicket = (await route.request().postDataJSON() as { ticket: string }).ticket
    await route.fulfill({ json: envelope(SESSION) })
  })

  await page.goto('/oauth/callback?ticket=one-time-ticket')

  await expect(page).toHaveURL(/\/module$/, { timeout: 10_000 })
  expect(exchangedTicket).toBe('one-time-ticket')
  const storage = await page.context().storageState()
  const userState = storage.origins
    .flatMap((origin) => origin.localStorage)
    .find((entry) => entry.name === 'user')?.value
  const persisted = JSON.parse(userState ?? '{}')
  expect(persisted.state).toMatchObject({ accessToken: SESSION.accessToken, refreshToken: SESSION.refreshToken })
})

test('OAuth binding callback returns an authenticated user to account bindings', async ({ page }) => {
  await page.addInitScript((session) => {
    localStorage.setItem('user', JSON.stringify({ state: {
      accessToken: session.accessToken,
      refreshToken: session.refreshToken,
      userInfo: {
        userId: session.userId,
        account: session.account,
        name: session.name,
        mustChangePassword: session.mustChangePassword,
      },
    }, version: 0 }))
  }, SESSION)
  await mockPortalBootstrap(page)
  await page.route('**/api/v1/auth/external/providers', (route) => route.fulfill({ json: envelope([]) }))
  await page.route('**/api/v1/auth/external/bindings', (route) => route.fulfill({ json: envelope([]) }))
  await page.route('**/api/v1/sys/notice/unread-count', (route) => route.fulfill({ json: envelope(0) }))

  await page.goto('/oauth/callback?bind=github')

  await expect(page).toHaveURL(/\/personal\/bindings$/, { timeout: 10_000 })
  await expect(page.getByText(/当前未启用任何第三方登录方式|no third-party sign-in methods are enabled/i)).toBeVisible()
})
