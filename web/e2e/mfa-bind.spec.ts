import { expect, test } from '@playwright/test'
import { seedForceTotpUser } from './api'
import { computeTotp } from './totp'

/**
 * 真实后端:建用户 → 浏览器自助绑定(账号+密码) → TOTP 完成 → 恢复码展示。
 * 宿主需 TenonAdmin:Security:Totp:Enabled=true。
 */
test('MFA bind: self-service account+password → authenticator → recovery codes', async ({ page, request }) => {
  test.setTimeout(60_000)
  const { account, password } = await seedForceTotpUser(request)

  await page.goto(`/mfa/bind?account=${encodeURIComponent(account)}`)
  await expect(page.getByText(/设置身份验证器|设置认证器/i).first()).toBeVisible()

  // 账号可能已预填;密码必填
  const accountInput = page.locator('input:not([type="password"]):not([readonly])').first()
  if (!(await accountInput.inputValue()).trim()) {
    await accountInput.fill(account)
  }
  await page.locator('input[type="password"]').fill(password)
  await page.getByRole('button', { name: /开始设置/ }).click()

  const seedInput = page.locator('input[readonly]').first()
  await expect(seedInput).toBeVisible({ timeout: 15_000 })
  const seed = (await seedInput.inputValue()).trim()
  expect(seed.length).toBeGreaterThan(10)

  const code = computeTotp(seed)
  await page.locator('input:not([readonly]):not([type="password"])').last().fill(code)
  await page.getByRole('button', { name: /完成设置/ }).click()

  await expect(page.locator('.recovery-code').first()).toBeVisible({ timeout: 15_000 })
  const codes = await page.locator('.recovery-code').allInnerTexts()
  expect(codes.filter((c) => c.trim().length > 0).length).toBeGreaterThanOrEqual(1)
})

test('MFA bind: empty recoveryCodes never shows success screen', async ({ page }) => {
  await page.route('**/api/v1/auth/mfa/bind/start', async (route) => {
    await route.fulfill({
      json: {
        code: 0,
        data: {
          bindChallengeId: 'chal-e2e',
          otpauthUri: 'otpauth://totp/Tenon:e2e?secret=JBSWY3DPEHPK3PXP',
          seed: 'JBSWY3DPEHPK3PXP',
        },
      },
    })
  })
  await page.route('**/api/v1/auth/mfa/bind/complete', async (route) => {
    await route.fulfill({ json: { code: 0, data: { recoveryCodes: [] } } })
  })

  await page.goto('/mfa/bind')
  await page.locator('input:not([type="password"]):not([readonly])').first().fill('e2euser')
  await page.locator('input[type="password"]').fill('whatever')
  await page.getByRole('button', { name: /开始设置/ }).click()
  await expect(page.getByRole('button', { name: /完成设置/ })).toBeVisible({ timeout: 10_000 })

  await page.locator('input:not([readonly]):not([type="password"])').last().fill('123456')
  await page.getByRole('button', { name: /完成设置/ }).click()

  await expect(page.getByText(/设置响应不完整|未能返回恢复码|重新开始/i)).toBeVisible({ timeout: 10_000 })
  await expect(page.locator('.recovery-code')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /完成设置/ })).toBeVisible()
})
