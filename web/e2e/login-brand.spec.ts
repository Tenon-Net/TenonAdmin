import { expect, test, type Page } from '@playwright/test'

const envelope = (data: unknown) => ({ code: 0, msg: 'ok', data })

const captchaSvg = `
  <svg xmlns="http://www.w3.org/2000/svg" width="120" height="40" viewBox="0 0 120 40">
    <rect width="120" height="40" fill="#fff" />
    <text x="24" y="27" fill="#111">7QN3</text>
  </svg>
`

interface LoginMocks {
  captchaEnabled?: boolean
  smsLoginEnabled?: boolean
  providers?: Array<{ code: string; displayName: string; icon?: string | null }>
}

async function mockLoginApi(page: Page, mocks: LoginMocks = {}) {
  await page.route('**/api/v1/sys/config/site', (route) => route.fulfill({
    json: envelope({
      title: 'TenonAdmin',
      subtitle: '',
      copyright: '',
      copyrightUrl: '',
      logo: '',
      captchaEnabled: mocks.captchaEnabled ?? true,
      smsLoginEnabled: mocks.smsLoginEnabled ?? false,
    }),
  }))
  await page.route('**/api/v1/auth/captcha', (route) => route.fulfill({
    json: envelope({ captchaId: 'brand-e2e-captcha', svg: captchaSvg, type: 'char' }),
  }))
  await page.route('**/api/v1/auth/external/providers', (route) => route.fulfill({
    json: envelope(mocks.providers ?? []),
  }))
}

async function useStableLightTheme(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('app', JSON.stringify({ themeScheme: 'light', locale: 'zh-CN' }))
  })
}

test('defaults to split and keeps the shared form state across theme and locale changes', async ({ page }) => {
  await mockLoginApi(page)
  await useStableLightTheme(page)

  await page.goto('/login')

  await expect(page.locator('.split')).toBeVisible()
  await expect.poll(() => page.evaluate(() => localStorage.getItem('login-skin'))).toBeNull()

  const account = page.getByPlaceholder(/请输入账号|Enter account/)
  const password = page.getByPlaceholder(/请输入密码|Enter password/)
  const captcha = page.getByPlaceholder(/请输入验证码|Enter captcha/)
  await account.fill('brand-user')
  await password.fill('keep-this-secret')
  await captcha.fill('7QN3')

  const card = page.locator('.form-card')
  const beforeTheme = await card.boundingBox()
  expect(beforeTheme).not.toBeNull()

  await page.getByRole('button', { name: /暗色|Dark/ }).click()
  await expect(page.locator('.split')).toHaveClass(/split-dark/)
  const afterTheme = await card.boundingBox()
  expect(afterTheme).not.toBeNull()
  expect(Math.abs(afterTheme!.x - beforeTheme!.x)).toBeLessThan(2)
  expect(Math.abs(afterTheme!.y - beforeTheme!.y)).toBeLessThan(2)

  await page.getByRole('button', { name: /语言|Language/ }).click()
  await expect(page.getByPlaceholder('Enter account')).toHaveValue('brand-user')
  await expect(page.getByPlaceholder('Enter password')).toHaveValue('keep-this-secret')
  await expect(page.getByPlaceholder('Enter captcha')).toHaveValue('7QN3')
})

test('keeps the enabled login methods reachable without horizontal overflow on narrow screens', async ({ page }) => {
  await mockLoginApi(page, { captchaEnabled: true, smsLoginEnabled: true })
  await useStableLightTheme(page)
  let loginRequests = 0
  await page.route('**/api/v1/auth/login', (route) => {
    loginRequests++
    return route.fulfill({ status: 400, json: { code: 40001, msg: 'error', data: null } })
  })

  for (const width of [320, 375, 414, 768]) {
    await page.setViewportSize({ width, height: 640 })
    await page.goto('/login')

    await expect(page.locator('.skin-picker')).toBeVisible()
    await expect(page.locator('.login-tools .tool-btn')).toHaveCount(2)
    await expect(page.getByPlaceholder(/请输入账号|Enter account/)).toBeVisible()
    await expect(page.getByPlaceholder(/请输入验证码|Enter captcha/)).toBeVisible()
    await expect(page.locator('.lf-captcha-img svg')).toBeVisible()

    const geometry = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth,
      clientHeight: document.documentElement.clientHeight,
      scrollHeight: document.documentElement.scrollHeight,
    }))
    expect(geometry.scrollWidth, `${width}px viewport: ${JSON.stringify(geometry)}`).toBeLessThanOrEqual(geometry.clientWidth)
    expect(geometry.scrollHeight).toBeGreaterThanOrEqual(geometry.clientHeight)

    await page.getByPlaceholder(/请输入账号|Enter account/).fill('keyboard-user')
    await page.getByPlaceholder(/请输入密码|Enter password/).fill('Keyboard1!')
    await page.getByPlaceholder(/请输入验证码|Enter captcha/).fill('7QN3')
    await page.getByRole('button', { name: /短信登录|SMS sign-in/ }).click()
    await expect(page.getByPlaceholder(/请输入手机号|Enter phone number/)).toBeVisible()
    await expect(page.getByPlaceholder(/请输入短信验证码|Enter the SMS code/)).toBeVisible()
    const submit = page.locator('.hero-btn')
    await submit.scrollIntoViewIfNeeded()
    await expect(submit).toBeVisible()
    await expect(page.locator('.skin-picker')).toBeVisible()
    // Enter 激活切换按钮时不冒泡成密码登录提交。
    await page.getByRole('button', { name: /账号登录|Password sign-in/ }).press('Enter')
    await expect(page.getByPlaceholder(/请输入账号|Enter account/)).toBeVisible()
    expect(loginRequests).toBe(0)
  }
})

test('renders SSO only for providers returned by the backend', async ({ page }) => {
  let providers: LoginMocks['providers'] = []
  await mockLoginApi(page, { captchaEnabled: false })
  await page.unroute('**/api/v1/auth/external/providers')
  await page.route('**/api/v1/auth/external/providers', (route) => route.fulfill({
    json: envelope(providers),
  }))

  await page.goto('/login')
  await expect(page.locator('.lf-divider')).toHaveCount(0)
  await expect(page.locator('.lf-sso-btn')).toHaveCount(0)

  providers = [{ code: 'github', displayName: 'GitHub' }]
  await page.reload()
  await expect(page.locator('.lf-sso-btn[data-provider="github"]')).toBeVisible()
  await expect(page.locator('.lf-sso-btn')).toHaveCount(1)
  await expect(page.locator('.lf-sso-btn[data-provider="google"]')).toHaveCount(0)
})

test('treats the query skin as a preview and persists an explicit menu choice', async ({ page }) => {
  await mockLoginApi(page, { captchaEnabled: false })
  await useStableLightTheme(page)

  await page.goto('/login')
  await page.evaluate(() => localStorage.setItem('login-skin', 'aurora'))
  await page.goto('/login?skin=spotlight')

  await expect(page.locator('.spotlight')).toBeVisible()
  await expect.poll(() => page.evaluate(() => localStorage.getItem('login-skin'))).toBe('aurora')

  await page.locator('.skin-picker').click()
  await page.locator('.n-dropdown-option').filter({ hasText: /双栏|Split/ }).click()
  await expect(page.locator('.split')).toBeVisible()
  await expect.poll(() => page.evaluate(() => localStorage.getItem('login-skin'))).toBe('split')

  await page.goto('/login')
  await expect(page.locator('.split')).toBeVisible()
})
