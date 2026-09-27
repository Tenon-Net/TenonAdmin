import { expect, test } from '@playwright/test'
import { apiAdminToken, apiReauthWithPassword, seedForceTotpUser, setTotpFeatureEnabled } from './api'
import { computeTotp } from './totp'

/**
 * 真实后端:建用户 → 打开运行时 TOTP 总闸 → 浏览器自助绑定 → TOTP 完成 → 恢复码展示。
 * 不能把 Totp:Enabled 做成整个 Playwright 宿主地板,否则用户/角色高危写会全部触发 40024 再认证。
 */
test('MFA bind: self-service account+password → authenticator → recovery codes', async ({ page, request }) => {
  test.setTimeout(60_000)
  const { account, password } = await seedForceTotpUser(request)
  const admin = await apiAdminToken(request)
  await setTotpFeatureEnabled(request, admin, true)
  try {
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
  } finally {
    await apiReauthWithPassword(request, admin)
    await setTotpFeatureEnabled(request, admin, false)
  }
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
  // BindPage 的设置态提示是 mfaBind.scanSetupHint(扫码) + manualSetupHint(手动),不是 setupHint。
  await expect(page.getByText('用 Google Authenticator、Microsoft Authenticator 等应用扫描下方二维码。')).toBeVisible({ timeout: 10_000 })

  await page.getByLabel(/动态口令|code/i).fill('123456')
  await page.getByRole('button', { name: /验证并完成|complete|完成/i }).click()

  await expect(page.getByText(/未能返回恢复码|重新开始设置/i)).toBeVisible({ timeout: 10_000 })
  await expect(page.getByText('保存恢复码')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /验证并完成|complete|完成/i })).toBeVisible()
})
