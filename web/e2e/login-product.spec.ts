import { expect, test, type Page } from '@playwright/test'

const envelope = (data: unknown) => ({ code: 0, msg: 'ok', data })

const captchaSvg = `
  <svg xmlns="http://www.w3.org/2000/svg" width="120" height="40" viewBox="0 0 120 40">
    <rect width="120" height="40" fill="#fff" />
    <text x="24" y="27" fill="#111">7QN3</text>
  </svg>
`

async function mockLoginApi(page: Page) {
  await page.route('**/api/v1/sys/config/site', (route) => route.fulfill({
    json: envelope({
      title: 'TenonAdmin',
      subtitle: '',
      copyright: '',
      copyrightUrl: '',
      logo: '',
      captchaEnabled: true,
      smsLoginEnabled: true,
    }),
  }))
  await page.route('**/api/v1/auth/captcha', (route) => route.fulfill({
    json: envelope({ captchaId: 'product-e2e-captcha', svg: captchaSvg, type: 'char' }),
  }))
  await page.route('**/api/v1/auth/external/providers', (route) => route.fulfill({
    json: envelope([]),
  }))
}

async function useStableLightTheme(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('app', JSON.stringify({ themeScheme: 'light', locale: 'zh-CN' }))
  })
}

test('shows the real product preview and localized docs while preserving the form across display changes', async ({ page }) => {
  await mockLoginApi(page)
  await useStableLightTheme(page)
  await page.setViewportSize({ width: 1280, height: 900 })

  await page.goto('/login?skin=product')

  await expect(page.locator('.product-page')).toBeVisible()
  const preview = page.getByRole('img', { name: 'TenonAdmin Vue 模板的用户管理界面预览' })
  await expect(preview).toBeVisible()
  await expect(preview).toHaveAttribute('src', /product-preview.*\.webp/)
  await expect.poll(() => preview.evaluate((image: HTMLImageElement) => image.naturalWidth)).toBeGreaterThan(0)

  const docs = page.getByRole('link', { name: /开发文档/ })
  await expect(docs).toHaveAttribute('href', 'https://tenon.52moyu.net/zh/')
  await expect(docs).toHaveAttribute('target', '_blank')

  await page.getByPlaceholder('请输入账号').fill('product-user')
  await page.getByPlaceholder('请输入密码').fill('keep-this-secret')
  await page.getByPlaceholder('请输入验证码').fill('7QN3')

  await page.getByRole('button', { name: /暗色|Dark/ }).click()
  await expect(page.locator('.product-page')).toHaveClass(/product-dark/)
  await page.getByRole('button', { name: /语言|Language/ }).click()

  await expect(page.getByPlaceholder('Enter account')).toHaveValue('product-user')
  await expect(page.getByPlaceholder('Enter password')).toHaveValue('keep-this-secret')
  await expect(page.getByPlaceholder('Enter captcha')).toHaveValue('7QN3')
  await expect(page.getByRole('link', { name: /Documentation/ })).toHaveAttribute(
    'href',
    'https://tenon.52moyu.net/',
  )
})

test('keeps captcha, SMS, and submit controls reachable without horizontal overflow', async ({ page }) => {
  await mockLoginApi(page)
  await useStableLightTheme(page)

  for (const width of [320, 375, 414, 768]) {
    await page.setViewportSize({ width, height: 640 })
    await page.goto('/login?skin=product')

    await expect(page.locator('.product-page')).toBeVisible()
    await expect(page.getByPlaceholder(/请输入账号|Enter account/)).toBeVisible()
    await expect(page.getByPlaceholder(/请输入验证码|Enter captcha/)).toBeVisible()
    await expect(page.locator('.lf-captcha-img svg')).toBeVisible()

    const geometry = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth,
    }))
    expect(geometry.scrollWidth, `${width}px viewport: ${JSON.stringify(geometry)}`).toBeLessThanOrEqual(
      geometry.clientWidth,
    )

    await page.getByRole('button', { name: /短信登录|SMS sign-in/ }).scrollIntoViewIfNeeded()
    await page.getByRole('button', { name: /短信登录|SMS sign-in/ }).click()
    await expect(page.getByPlaceholder(/请输入手机号|Enter phone number/)).toBeVisible()
    await expect(page.getByPlaceholder(/请输入短信验证码|Enter the SMS code/)).toBeVisible()

    const submit = page.locator('.hero-btn')
    await submit.scrollIntoViewIfNeeded()
    await expect(submit).toBeVisible()
    await expect(page.locator('.skin-picker')).toBeVisible()
  }
})

test('keeps split as the default and persists an explicit product menu choice', async ({ page }) => {
  await mockLoginApi(page)
  await useStableLightTheme(page)

  await page.goto('/login')
  await expect(page.locator('.split')).toBeVisible()
  await expect.poll(() => page.evaluate(() => localStorage.getItem('login-skin'))).toBeNull()

  await page.locator('.skin-picker').click()
  await page.locator('.n-dropdown-option').filter({ hasText: /概览|Overview/ }).click()
  await expect(page.locator('.product-page')).toBeVisible()
  await expect.poll(() => page.evaluate(() => localStorage.getItem('login-skin'))).toBe('product')

  await page.goto('/login')
  await expect(page.locator('.product-page')).toBeVisible()
})
