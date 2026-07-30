import { expect, test } from '@playwright/test'
import { seedForceTotpInvite } from './api'
import { computeTotp } from './totp'

/**
 * 真实后端:管理员 API 建 ForceTotp 用户并发邀请 → 浏览器绑定页密码确认 + TOTP 完成 → 恢复码展示。
 */
test('MFA bind: invite → password → authenticator → recovery codes', async ({ page, request }) => {
  test.setTimeout(60_000)
  const { password, inviteToken } = await seedForceTotpInvite(request)

  await page.goto(`/mfa/bind?token=${encodeURIComponent(inviteToken)}`)
  await expect(page.getByText(/设置身份验证器|设置认证器/i).first()).toBeVisible()

  // Naive FormItem labels are not reliably associated for getByLabel; password input is typed.
  await page.locator('input[type="password"]').fill(password)
  await page.getByRole('button', { name: /开始设置/ }).click()

  // Manual key appears after start (readonly)
  const seedInput = page.locator('input[readonly]').first()
  await expect(seedInput).toBeVisible({ timeout: 15_000 })
  const seed = (await seedInput.inputValue()).trim()
  expect(seed.length).toBeGreaterThan(10)

  const code = computeTotp(seed)
  // Dynamic code field is the only editable text input on step 2
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

  await page.goto('/mfa/bind?token=fake-invite')
  await page.locator('input[type="password"]').fill('whatever')
  await page.getByRole('button', { name: /开始设置/ }).click()
  await expect(page.getByRole('button', { name: /完成设置/ })).toBeVisible({ timeout: 10_000 })

  await page.locator('input:not([readonly]):not([type="password"])').last().fill('123456')
  await page.getByRole('button', { name: /完成设置/ }).click()

  // Error toast/message; stay off recovery step (no .recovery-code grid)
  await expect(page.getByText(/设置响应不完整|未能返回恢复码|重新开始/i)).toBeVisible({ timeout: 10_000 })
  await expect(page.locator('.recovery-code')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /完成设置/ })).toBeVisible()
})