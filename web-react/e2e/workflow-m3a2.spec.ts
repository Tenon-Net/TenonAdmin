import { expect, test } from '@playwright/test'

import { cancelDrawer, chooseOption, expectMessage, formItemByLabel, saveDrawer, tableRow, visibleDrawer } from './antd'
import { enterSystemApp, login } from './helpers'
import { addAfter, addNode, publishDefinition, saveDefinition } from './workflow'

test('M3a2 Webhook 定义:六字段落库、回显并发布', async ({ page }) => {
  test.setTimeout(150_000)

  const suffix = Date.now()
  const definitionName = `M3a2 Webhook ${suffix.toString(36)}`
  const webhookUrl = `https://example.com/hooks/m3a2-${suffix}`

  // Webhook 属治理面:定义列表、设计器都在「系统」应用下。
  await login(page)
  await enterSystemApp(page)
  await page.goto('/workflow/definition')
  await page.getByRole('button', { name: /新建流程|New workflow/i }).click()
  await expect(page).toHaveURL(/\/workflow\/definition\/designer/)

  await page.getByPlaceholder(/流程名称|Workflow name/i).fill(definitionName)
  await page.getByRole('button', { name: /新建草稿|Create draft/i }).click()
  await expect(page).toHaveURL(/\/workflow\/definition\/designer\?id=\d+/, { timeout: 15_000 })

  await addNode(page, addAfter(page, 0), /^Webhook$/i)

  let drawer = await visibleDrawer(page)
  await drawer.getByLabel(/^Webhook URL$/i).fill(webhookUrl)
  await chooseOption(page, formItemByLabel(drawer, /请求方法|Method/i).locator('.ant-select'), /^PATCH$/)
  await formItemByLabel(drawer, /超时|Timeout/i).locator('input').fill('45')
  await chooseOption(page, formItemByLabel(drawer, /失败策略|Failure policy/i).locator('.ant-select'), /转人工处理|Manual handling/i)

  // 请求头与最大尝试次数是低频项,收在「高级」里。
  await drawer.locator('.wf-advanced .ant-collapse-header').click()
  await drawer.getByRole('button', { name: /添加请求头|Add header/i }).click()
  await drawer.getByPlaceholder(/^(名称|Name)$/i).fill('X-M3A2')
  await drawer.getByPlaceholder(/^(值|Value)$/i).fill('acceptance')
  await formItemByLabel(drawer, /最大尝试次数|Maximum attempts/i).locator('input').fill('5')
  await saveDrawer(page, drawer)

  await saveDefinition(page)

  // 刷新后从后端快照回显:六个字段一个都不能丢。
  await page.reload()
  const webhookNode = page.locator('.wf-card.is-webhook')
  await expect(webhookNode).toContainText(webhookUrl)
  await webhookNode.click()
  drawer = await visibleDrawer(page)
  await expect(drawer.getByLabel(/^Webhook URL$/i)).toHaveValue(webhookUrl)
  await expect(formItemByLabel(drawer, /请求方法|Method/i)).toContainText('PATCH')
  await expect(formItemByLabel(drawer, /超时|Timeout/i).locator('input')).toHaveValue('45')
  await expect(formItemByLabel(drawer, /失败策略|Failure policy/i)).toContainText(/转人工处理|Manual handling/i)
  await drawer.locator('.wf-advanced .ant-collapse-header').click()
  await expect(drawer.getByPlaceholder(/^(名称|Name)$/i)).toHaveValue('X-M3A2')
  await expect(drawer.getByPlaceholder(/^(值|Value)$/i)).toHaveValue('acceptance')
  await expect(formItemByLabel(drawer, /最大尝试次数|Maximum attempts/i).locator('input')).toHaveValue('5')
  await cancelDrawer(page, drawer)

  await publishDefinition(page)
  await expectMessage(page, /已发布|Published/i)

  await page.goto('/workflow/definition')
  const row = tableRow(page, definitionName)
  await expect(row).toBeVisible({ timeout: 15_000 })
  await expect(row).toContainText(/已发布|Published/i)
  await row.getByRole('button', { name: /设\s*计|Design/i }).click()
  await expect(page).toHaveURL(/\/workflow\/definition\/designer\?id=\d+/)
  await expect(page.locator('.wf-card.is-webhook')).toContainText(webhookUrl)
})
