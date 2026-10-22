import { expect, test } from '@playwright/test'
import { enterSystemApp, login } from './helpers'

// 初始化服务暂时不可用时保留有效会话；重试恢复原深链，不要求重新登录。
test('初始化接口 503 后可直接重试恢复深链', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  let fail = true
  await page.route('**/api/v1/personal/modules', async (route) => {
    if (fail) {
      fail = false
      await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: '暂时不可用' }) })
    } else await route.continue()
  })
  await page.goto('/system/user')
  await expect(page).toHaveURL(/\/initialization-error$/)
  const retry = page.getByRole('button', { name: /^重试$|^Retry$/ })
  await expect(retry).toBeVisible()
  await retry.click()
  await expect(page).toHaveURL(/\/system\/user$/)
  await expect(page.locator('.n-data-table')).toBeVisible()
})
