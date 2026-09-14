import { expect, test, type Locator, type Page } from '@playwright/test'

import { enterApp, login, SYSTEM_APP } from './helpers'

async function chooseNaiveOption(page: Page, select: Locator, option: RegExp) {
  await select.locator('.n-base-selection').click()
  const item = page.locator('.n-base-select-option:visible').filter({ hasText: option }).first()
  await expect(item).toBeVisible()
  await item.click()
}

function formItemByLabel(scope: Locator, label: RegExp) {
  return scope
    .locator('.n-form-item-label')
    .filter({ hasText: label })
    .first()
    .locator('xpath=ancestor::*[contains(concat(" ", normalize-space(@class), " "), " n-form-item ")][1]')
}

test('M3a2 webhook definition persists its configuration and publishes', async ({ page }) => {
  test.setTimeout(120_000)

  const suffix = Date.now()
  const definitionName = `M3a2 Webhook ${suffix.toString(36)}`
  const webhookUrl = `https://example.com/hooks/m3a2-${suffix}`

  await login(page)
  await enterApp(page, SYSTEM_APP)
  await page.goto('/workflow/definition')
  await page.getByRole('button', { name: /新建流程|New workflow/i }).click()
  await expect(page).toHaveURL(/\/workflow\/definition\/designer/)

  await page.getByPlaceholder(/流程名称|Workflow name/i).fill(definitionName)
  await page.getByRole('button', { name: /新建草稿|Create draft/i }).click()
  await expect(page).toHaveURL(/\/workflow\/definition\/designer\?id=\d+/, { timeout: 15_000 })

  const startNode = page.locator('.wf-tree > .wf-chain > .wf-chain-node').first()
  await startNode.locator(':scope > .wf-add .wf-add-btn').click()
  const popover = page.locator('.n-popover:visible')
  await expect(popover).toBeVisible()
  await popover.getByRole('button', { name: /Webhook/i }).click()

  let drawer = page.locator('.n-drawer:visible')
  await expect(drawer).toHaveCount(1)
  await formItemByLabel(drawer, /^Webhook URL$/i).locator('input').fill(webhookUrl)
  await chooseNaiveOption(page, formItemByLabel(drawer, /请求方法|Method/i).locator('.n-select'), /^PATCH$/)
  await formItemByLabel(drawer, /超时.*秒|Timeout.*seconds/i).locator('input').fill('45')
  await chooseNaiveOption(page, formItemByLabel(drawer, /失败策略|Failure policy/i).locator('.n-select'), /转人工处理|Manual handling/i)

  await drawer.getByText(/^高级$|^Advanced$/i).click()
  await drawer.getByRole('button', { name: /添加请求头|Add header/i }).click()
  await drawer.getByPlaceholder(/^名称$|^Name$/i).fill('X-M3A2')
  await drawer.getByPlaceholder(/^值$|^Value$/i).fill('acceptance')
  await formItemByLabel(drawer, /最大尝试次数|Maximum attempts/i).locator('input').fill('5')
  await drawer.getByRole('button', { name: /保存|Save/i }).click()
  await expect(drawer).toBeHidden()

  const saveResponse = page.waitForResponse((response) =>
    response.url().includes('/api/v1/workflow/definition/update') && response.request().method() === 'POST',
  )
  await page.getByRole('button', { name: /^保存$|^Save$/i }).click()
  expect((await saveResponse).ok()).toBe(true)

  await page.reload()
  const webhookNode = page.locator('.wf-card.is-webhook')
  await expect(webhookNode).toContainText(webhookUrl)
  await webhookNode.click()
  drawer = page.locator('.n-drawer:visible')
  await expect(formItemByLabel(drawer, /^Webhook URL$/i).locator('input')).toHaveValue(webhookUrl)
  await expect(formItemByLabel(drawer, /请求方法|Method/i)).toContainText('PATCH')
  await expect(formItemByLabel(drawer, /超时.*秒|Timeout.*seconds/i).locator('input')).toHaveValue('45')
  await expect(formItemByLabel(drawer, /失败策略|Failure policy/i)).toContainText(/转人工处理|Manual handling/i)
  await drawer.getByText(/^高级$|^Advanced$/i).click()
  await expect(drawer.getByPlaceholder(/^名称$|^Name$/i)).toHaveValue('X-M3A2')
  await expect(drawer.getByPlaceholder(/^值$|^Value$/i)).toHaveValue('acceptance')
  await expect(formItemByLabel(drawer, /最大尝试次数|Maximum attempts/i).locator('input')).toHaveValue('5')
  await drawer.getByRole('button', { name: /取消|Cancel/i }).click()

  const publishResponse = page.waitForResponse((response) =>
    response.url().includes('/api/v1/workflow/definition/publish') && response.request().method() === 'POST',
  )
  await page.getByRole('button', { name: /^发布$|^Publish$/i }).click()
  expect((await publishResponse).ok()).toBe(true)
  await expect(page.locator('.n-message').filter({ hasText: /已发布|Published/i }).last()).toBeVisible()

  await page.goto('/workflow/definition')
  const row = page.locator('.n-data-table-tr').filter({ hasText: definitionName })
  await expect(row).toBeVisible()
  await expect(row).toContainText(/已发布|Published/i)
  await row.getByRole('button', { name: /设计|Design/i }).click()
  await expect(page).toHaveURL(/\/workflow\/definition\/designer\?id=\d+/)
  await expect(page.locator('.wf-card.is-webhook')).toContainText(webhookUrl)
})
