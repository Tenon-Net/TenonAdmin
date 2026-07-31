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
  await expect(page.getByText(/绑定|Bind|Authenticator|认证器/i).first()).toBeVisible()

  const accountField = page.getByLabel(/账号|account/i)
  if (!(await accountField.inputValue()).trim()) {
    await accountField.fill(account)
  }
  await page.getByLabel(/当前密码|password/i).fill(password)
  await page.getByRole('button', { name: /继\s*续|begin|开始/i }).click()

  const seedInput = page.locator('input[readonly]').filter({ hasNot: page.locator('[type="password"]') }).first()
  await expect(seedInput).toBeVisible({ timeout: 15_000 })
  const seed = (await seedInput.inputValue()).trim()
  expect(seed.length).toBeGreaterThan(10)

  const code = computeTotp(seed)
  await page.getByLabel(/动态口令|authenticator|code/i).fill(code)
  await page.getByRole('button', { name: /验证并完成|complete|完成/i }).click()

  await expect(page.getByText('保存恢复码')).toBeVisible({ timeout: 15_000 })
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

  await page.goto('/mfa/bind')
  await page.getByLabel(/账号|account/i).fill('e2euser')
  await page.getByLabel(/当前密码|password/i).fill('whatever')
  await page.getByRole('button', { name: /继\s*续|begin|开始/i }).click()
  await expect(page.getByText('将此帐户添加到认证器应用，然后输入当前动态口令。')).toBeVisible({ timeout: 10_000 })

  await page.getByLabel(/动态口令|code/i).fill('123456')
  await page.getByRole('button', { name: /验证并完成|complete|完成/i }).click()

  await expect(page.getByText(/未能返回恢复码|重新开始设置/i)).toBeVisible({ timeout: 10_000 })
  await expect(page.getByText('保存恢复码')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /验证并完成|complete|完成/i })).toBeVisible()
})
