import { expect, test, type Page } from '@playwright/test'
import { ADMIN_ACCOUNT, ADMIN_PASSWORD } from './helpers'

const envelope = (data: unknown, code = 0, args?: Record<string, unknown>) => ({
  code,
  msg: code === 0 ? 'ok' : 'error',
  data,
  args,
})

const captchaSvg = (code: string) => `
  <svg xmlns="http://www.w3.org/2000/svg" width="120" height="40" viewBox="0 0 120 40">
    <rect width="120" height="40" fill="#fff" />
    <text x="24" y="27" fill="#111">${code}</text>
  </svg>
`

interface LoginMocks {
  captchaEnabled?: boolean
  smsLoginEnabled?: boolean
  providers?: Array<{ code: string; displayName: string; icon?: string | null }>
}

async function mockLoginBootstrap(page: Page, options: LoginMocks = {}) {
  let captchaLoads = 0
  await page.route('**/api/v1/sys/config/site', (route) => route.fulfill({
    json: envelope({
      title: 'TenonAdmin',
      subtitle: '',
      copyright: '',
      copyrightUrl: '',
      logo: '',
      captchaEnabled: options.captchaEnabled ?? false,
      smsLoginEnabled: options.smsLoginEnabled ?? false,
    }),
  }))
  await page.route('**/api/v1/auth/captcha', (route) => {
    captchaLoads++
    return route.fulfill({
      json: envelope({
        captchaId: `light-brand-${captchaLoads}`,
        svg: captchaSvg(`C${captchaLoads}11`),
        type: 'char',
      }),
    })
  })
  await page.route('**/api/v1/auth/external/providers', (route) => route.fulfill({
    json: envelope(options.providers ?? []),
  }))
  return { captchaLoads: () => captchaLoads }
}

async function useStableLightTheme(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('app', JSON.stringify({ themeScheme: 'light', locale: 'zh-CN' }))
    localStorage.removeItem('login-skin')
  })
}

test('light-brand preview is responsive and does not replace the saved default skin', async ({ page }) => {
  await mockLoginBootstrap(page)
  await useStableLightTheme(page)

  await page.goto('/login')
  await expect(page.locator('.split')).toBeVisible()
  await expect(page.locator('.light-brand')).toHaveCount(0)

  for (const viewport of [
    { width: 1448, height: 1086 },
    { width: 1920, height: 1080 },
    { width: 1366, height: 768 },
    { width: 390, height: 844 },
  ]) {
    await page.setViewportSize(viewport)
    await page.goto('/login?skin=light-brand')
    await expect(page.locator('.light-brand')).toBeVisible()

    const account = page.getByPlaceholder(/请输入账号|Enter account/)
    const submit = page.locator('button.hero-btn')
    await expect(account).toBeVisible()
    await submit.scrollIntoViewIfNeeded()
    await expect(submit).toBeVisible()

    const geometry = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth,
      accountLeft: document.querySelector<HTMLInputElement>('input[placeholder*="账号"], input[placeholder*="account"]')
        ?.getBoundingClientRect().left ?? -1,
    }))
    expect(geometry.scrollWidth, `${viewport.width}x${viewport.height}`).toBeLessThanOrEqual(geometry.clientWidth)
    if (viewport.width > 900) expect(geometry.accountLeft).toBeGreaterThan(viewport.width / 2)
  }

  await expect.poll(() => page.evaluate(() => localStorage.getItem('login-skin'))).toBeNull()
})

test('theme, locale, captcha and configured login methods remain interactive', async ({ page }) => {
  const captchaRequests = await mockLoginBootstrap(page, {
    captchaEnabled: true,
    smsLoginEnabled: true,
    providers: [{ code: 'github', displayName: 'GitHub' }],
  })
  await useStableLightTheme(page)
  await page.goto('/login?skin=light-brand')

  const account = page.getByPlaceholder(/请输入账号|Enter account/)
  const password = page.getByPlaceholder(/请输入密码|Enter password/)
  const captcha = page.getByPlaceholder(/请输入验证码|Enter captcha/)
  await account.fill('brand-user')
  await password.fill('Keep-this-secret1!')
  const captchaImage = page.locator('.lf-captcha-img svg')
  await expect(captchaImage).toBeVisible()
  const initialCaptcha = await captchaImage.textContent()
  const initialLoads = captchaRequests.captchaLoads()
  await captcha.fill('A111')
  await page.locator('.lf-captcha-img').click()
  await expect.poll(captchaRequests.captchaLoads).toBe(initialLoads + 1)
  await expect(captchaImage).not.toHaveText(initialCaptcha ?? '')
  // 手动刷新图形不消费旧输入；真正提交后 refreshAfterUse 才清空一次性验证码。
  await expect(captcha).toHaveValue('A111')
  await captcha.fill('B222')

  await expect(page.locator('.lf-sso-btn[data-provider="github"]')).toBeVisible()
  await page.getByRole('button', { name: /短信登录|SMS sign-in/ }).click()
  await expect(page.getByPlaceholder(/请输入手机号|Enter phone number/)).toBeVisible()
  await page.getByRole('button', { name: /账号登录|Password sign-in/ }).click()

  await page.getByRole('button', { name: /暗色|Dark/ }).click()
  await page.getByRole('button', { name: /语言|Language/ }).click()
  await expect(page.getByPlaceholder('Enter account')).toHaveValue('brand-user')
  await expect(page.getByPlaceholder('Enter password')).toHaveValue('Keep-this-secret1!')
  await expect(page.getByPlaceholder('Enter captcha')).toHaveValue('B222')
})

test('wrong password, repeat-submit guard and TOTP challenge stay on the shared form path', async ({ page }) => {
  await mockLoginBootstrap(page)
  await useStableLightTheme(page)
  let loginHits = 0
  await page.route('**/api/v1/auth/login', async (route) => {
    loginHits++
    const body = route.request().postDataJSON() as { account?: string }
    if (body.account === 'totp-user') {
      return route.fulfill({ status: 400, json: envelope(null, 40018, { challengeId: 'brand-totp' }) })
    }
    if (body.account === 'repeat-user') {
      await new Promise((resolve) => setTimeout(resolve, 250))
    }
    return route.fulfill({ status: 400, json: envelope(null, 40001) })
  })

  await page.goto('/login?skin=light-brand')
  const account = page.getByPlaceholder(/请输入账号|Enter account/)
  const password = page.getByPlaceholder(/请输入密码|Enter password/)
  await account.fill('wrong-user')
  await password.fill('Wrong-password1!')
  await password.press('Enter')
  await expect(page.locator('.n-message').last()).toBeVisible()
  await expect(page).toHaveURL(/\/login/)

  await account.fill('repeat-user')
  const beforeRepeat = loginHits
  await Promise.all([password.press('Enter'), password.press('Enter')])
  await expect.poll(() => loginHits).toBe(beforeRepeat + 1)
  await expect(page.locator('button.hero-btn')).toBeEnabled()

  await account.fill('totp-user')
  await password.press('Enter')
  await expect(page.getByPlaceholder(/动态口令|6.?位|totp/i)).toBeVisible()
  await expect(page.locator('.light-brand')).toBeVisible()
})

test('light-brand completes a real login against the isolated local backend', async ({ page }) => {
  await useStableLightTheme(page)
  await page.goto('/login?skin=light-brand')
  await expect(page.locator('.light-brand')).toBeVisible()

  await page.getByPlaceholder(/账号|account/i).fill(ADMIN_ACCOUNT)
  await page.getByPlaceholder(/密码|password/i).fill(ADMIN_PASSWORD)
  const captchaSvgNode = page.locator('.lf-captcha-img svg')
  if (await captchaSvgNode.isVisible().catch(() => false)) {
    const code = (await captchaSvgNode.locator('text').allInnerTexts()).join('')
    await page.getByPlaceholder(/验证码|captcha/i).fill(code)
  }
  await page.locator('button.hero-btn').click()
  await expect(page).not.toHaveURL(/\/login/, { timeout: 15_000 })
})
