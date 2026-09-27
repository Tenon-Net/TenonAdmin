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
  const switcher = page.getByRole('button', { name: /切换应用|switch app/i })
  // 登录后的落点由用户的「默认应用」决定:可能直接是选择页,也可能是某个应用壳。
  // 重定向中间态读 URL 会误判成「在应用里」,然后去等一颗选择页上永不出现的切换按钮。
  await Promise.race([
    page.waitForURL(/\/module/, { timeout: 15_000 }).catch(() => {}),
    switcher.waitFor({ state: 'visible', timeout: 15_000 }).catch(() => {}),
  ])
  if (!/\/module/.test(page.url())) {
    // 点击会立即卸载整个布局壳,普通 click 的稳定性重试会抓住已分离的旧按钮。
    await switcher.dispatchEvent('click')
  }
  await expect(page).toHaveURL(/\/module/)
  const cards = page.locator('[data-testid^="module-card-"]')
  await expect(cards.first()).toBeVisible()
  return cards
}

export async function enterApp(page: Page, title: RegExp) {
  const cards = await openAppPicker(page)
  // title 只匹配卡片内的应用名称节点；直接 hasText 会把 code/“设为默认”也拼进全文，锚定正则永远匹配不到。
  const card = cards.filter({ has: page.getByText(title) })
  await expect(card).toHaveCount(1)
  await card.click()
  await expect(page).not.toHaveURL(/\/module/, { timeout: 10_000 })
}

/** 内置「系统」应用:流程定义、设计器、流程监控、长期委托都挂在它下面。 */
export const SYSTEM_APP = /^系统$|^System$/
/** 示例「业务中心」应用:发起、待办、抄送、我发起的、我已办的挂在它下面。 */
export const BUSINESS_APP = /^业务中心$|^Business$/

export async function enterSystemApp(page: Page) {
  await enterApp(page, SYSTEM_APP)
}

/**
 * 进「业务中心」。**工作流菜单跨两个应用**(`WorkflowMenuSeed`:治理页在 system、员工页在 business),
 * 而路由只从当前应用的菜单树生成 —— 在系统应用里直开 `/workflow/start` 会落 404。
 * Vue 套件就是漏了这一步才失败,凡是跨组访问必须先切应用。
 */
export async function enterBusinessApp(page: Page) {
  await enterApp(page, BUSINESS_APP)
}
