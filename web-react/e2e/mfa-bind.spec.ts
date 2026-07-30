import { expect, test } from '@playwright/test'
import { seedForceTotpInvite } from './api'
import { computeTotp } from './totp'

/**
 * 真实后端:管理员 API 建 ForceTotp 用户并发邀请 → 浏览器绑定页密码确认 + TOTP 完成 → 恢复码展示。
 * 非 Level3 宿主下仍可绑定(强制登录门禁仅 Level3);完整 ForceTotp 登录门禁见后端 HTTP 测试与 login-totp mock。
 */
test('MFA bind: invite → password → authenticator → recovery codes', async ({ page, request }) => {
  test.setTimeout(60_000)
  const { password, inviteToken } = await seedForceTotpInvite(request)

  await page.goto(`/mfa/bind?token=${encodeURIComponent(inviteToken)}`)
  await expect(page.getByText(/绑定|Bind|Authenticator|认证器/i).first()).toBeVisible()

  await page.getByLabel(/当前密码|password/i).fill(password)
  await page.getByRole('button', { name: /继\s*续|begin|开始/i }).click()

  // Seed is shown read-only after start
  const seedInput = page.locator('input[readonly]').filter({ hasNot: page.locator('[type="password"]') }).first()
  await expect(seedInput).toBeVisible({ timeout: 15_000 })
  const seed = (await seedInput.inputValue()).trim()
  expect(seed.length).toBeGreaterThan(10)

  const code = computeTotp(seed)
  await page.getByLabel(/动态口令|authenticator|code/i).fill(code)
  await page.getByRole('button', { name: /验证并完成|complete|完成/i }).click()

  await expect(page.getByText('保存恢复码')).toBeVisible({ timeout: 15_000 })
  // antd TextArea keeps a hidden measure textarea; only the real one is not aria-hidden.
  const recovery = page.locator('textarea:not([aria-hidden="true"])')
  await expect(recovery).toBeVisible()
  const text = await recovery.inputValue()
  expect(text.split(/\r?\n/).filter((l) => l.trim().length > 0).length).toBeGreaterThanOrEqual(1)
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
  await page.getByLabel(/当前密码|password/i).fill('whatever')
  await page.getByRole('button', { name: /继\s*续|begin|开始/i }).click()
  await expect(page.getByText('将此帐户添加到认证器应用，然后输入当前动态口令。')).toBeVisible({ timeout: 10_000 })

  await page.getByLabel(/动态口令|code/i).fill('123456')
  await page.getByRole('button', { name: /验证并完成|complete|完成/i }).click()

  await expect(page.getByText(/未能返回恢复码|重新开始设置/i)).toBeVisible({ timeout: 10_000 })
  await expect(page.getByText('保存恢复码')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /验证并完成|complete|完成/i })).toBeVisible()
})