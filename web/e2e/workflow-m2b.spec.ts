import { fileURLToPath } from 'node:url'

import { expect, test, type Locator, type Page } from '@playwright/test'

import { enterApp, login, SYSTEM_APP } from './helpers'

const RETURN_SHOT = fileURLToPath(new URL('../../.loop/wf-ui-shots/m2b-01-return.png', import.meta.url))
const CANCEL_SHOT = fileURLToPath(new URL('../../.loop/wf-ui-shots/m2b-02-cancel.png', import.meta.url))
const URGE_SHOT = fileURLToPath(new URL('../../.loop/wf-ui-shots/m2b-03-urge.png', import.meta.url))
const CC_SHOT = fileURLToPath(new URL('../../.loop/wf-ui-shots/m2b-04-cc-read.png', import.meta.url))
const MINE_SHOT = fileURLToPath(new URL('../../.loop/wf-ui-shots/m2b-05-mine.png', import.meta.url))
const DONE_SHOT = fileURLToPath(new URL('../../.loop/wf-ui-shots/m2b-06-done.png', import.meta.url))
const DRAWER_SHOT = fileURLToPath(new URL('../../.loop/wf-ui-shots/m2b-07-drawer-advanced.png', import.meta.url))

async function chooseNaiveOption(page: Page, select: Locator, option: RegExp) {
  await expect(select).toBeVisible({ timeout: 10_000 })
  await select.locator('.n-base-selection').click()
  const item = page.locator('.n-base-select-option:visible').filter({ hasText: option }).first()
  await expect(item).toBeVisible()
  await item.click()
}

async function visibleDrawer(page: Page) {
  const drawer = page.locator('.n-drawer:visible')
  await expect(drawer).toHaveCount(1)
  return drawer
}

function formItemByLabel(scope: Locator, label: RegExp) {
  return scope
    .locator('.n-form-item-label')
    .filter({ hasText: label })
    .first()
    .locator('xpath=ancestor::*[contains(concat(" ", normalize-space(@class), " "), " n-form-item ")][1]')
}

async function addNode(page: Page, trigger: Locator, nodeName: RegExp) {
  await trigger.click()
  const popover = page.locator('.n-popover:visible')
  await expect(popover).toBeVisible()
  await popover.getByRole('button', { name: nodeName }).click()
}

async function pickUsers(page: Page, drawer: Locator, usersLabel: RegExp, who: RegExp) {
  const usersItem = formItemByLabel(drawer, usersLabel)
  const usersSelect = usersItem.locator('.n-select')
  await expect(usersSelect).toBeVisible({ timeout: 10_000 })
  await usersSelect.locator('.n-base-selection').click()
  const option = page.locator('.n-base-select-option:visible').filter({ hasText: who }).first()
  await expect(option).toBeVisible({ timeout: 10_000 })
  await option.click()
  await page.keyboard.press('Escape')
  await expect(usersSelect).toContainText(who)
}

async function saveDrawer(page: Page, drawer: Locator) {
  await drawer.getByRole('button', { name: /保存|Save/i }).click()
  await expect(drawer).toBeHidden()
}

async function configureAssignee(page: Page, drawer: Locator, who: RegExp) {
  const assigneeItem = formItemByLabel(drawer, /^办理人$|^Assignee$/i)
  await chooseNaiveOption(page, assigneeItem.locator('.n-select'), /指定成员|Specified users/i)
  await pickUsers(page, drawer, /^指定成员$|^Members$/i, who)
}

async function chooseDefinition(page: Page, definitionName: string) {
  const definitionSelect = page.locator('.n-form-item').first().locator('.n-select')
  await chooseNaiveOption(page, definitionSelect, new RegExp(definitionName))
}

async function startInstance(page: Page, definitionName: string, businessKey: string) {
  await page.goto('/workflow/start')
  await expect(page.getByText(/发起流程|Start workflow/i).first()).toBeVisible()
  await chooseDefinition(page, definitionName)
  const businessKeyInput = page.getByPlaceholder(/可选.*业务单据|business document key/i)
  await expect(businessKeyInput).toBeVisible({ timeout: 10_000 })
  await businessKeyInput.fill(businessKey)
  await page.getByRole('button', { name: /提交发起|Submit/i }).click()
  await expect(page).toHaveURL(/\/workflow\/instance\/\d+\/detail/, { timeout: 15_000 })
  const match = /\/workflow\/instance\/(\d+)\/detail/.exec(page.url())
  expect(match).not.toBeNull()
  return Number(match![1])
}

async function confirmAction(page: Page) {
  const actionModal = page.locator('.n-modal:visible')
  await expect(actionModal).toBeVisible()
  await actionModal.getByRole('button', { name: /确定|Confirm/i }).click()
  await expect(actionModal).toBeHidden({ timeout: 15_000 })
}

async function publishDefinition(page: Page) {
  const publishResponse = page.waitForResponse((response) =>
    response.url().includes('/api/v1/workflow/definition/publish') && response.request().method() === 'POST',
  )
  await page.getByRole('button', { name: /^发布$|^Publish$/i }).click()
  const response = await publishResponse
  expect(response.ok()).toBe(true)
  expect((await response.json()).code).toBe(0)
  await expect(page.locator('.n-message').filter({ hasText: /已发布|Published/i }).last()).toBeVisible()
}

test('M2b verbs: return, cancel, urge, cc read, mine and done', async ({ page }) => {
  test.setTimeout(240_000)

  const suffix = Date.now().toString(36)
  const selfDef = `M2b Self ${suffix}`
  const urgeDef = `M2b Urge ${suffix}`
  const returnKey = `M2B-RET-${suffix}`
  const cancelKey = `M2B-CAN-${suffix}`
  const urgeKey = `M2B-URG-${suffix}`

  await login(page)
  await enterApp(page, SYSTEM_APP)

  await page.goto('/workflow/definition/designer')
  await expect(page.getByText(/未打开流程|No workflow opened/i)).toBeVisible()
  await page.getByPlaceholder(/流程名称|Workflow name/i).fill(selfDef)
  await page.getByRole('button', { name: /新建草稿|Create draft/i }).click()
  await expect(page).toHaveURL(/\/workflow\/definition\/designer\?id=\d+/, { timeout: 15_000 })

  const mainChain = page.locator('.wf-tree > .wf-chain')
  const startNode = mainChain.locator(':scope > .wf-chain-node').first()
  await addNode(page, startNode.locator(':scope > .wf-add .wf-add-btn'), /抄送|CC/i)
  let drawer = await visibleDrawer(page)
  await configureAssignee(page, drawer, /superAdmin/i)
  await saveDrawer(page, drawer)

  const ccChainNode = mainChain.locator(':scope > .wf-chain-node').nth(1)
  await addNode(page, ccChainNode.locator(':scope > .wf-add .wf-add-btn'), /审批|Approval/i)
  drawer = await visibleDrawer(page)
  await drawer.getByPlaceholder(/节点名称|Node name/i).fill('M2b 审批')
  await configureAssignee(page, drawer, /superAdmin/i)
  await drawer.locator('.wf-advanced').getByText(/高级|Advanced/i).click()
  await expect(drawer.getByText(/退回策略|Return policy/i)).toBeVisible()
  await page.screenshot({ path: DRAWER_SHOT, fullPage: true })
  await saveDrawer(page, drawer)
  await publishDefinition(page)

  await page.goto('/workflow/definition/designer')
  await page.getByPlaceholder(/流程名称|Workflow name/i).fill(urgeDef)
  await page.getByRole('button', { name: /新建草稿|Create draft/i }).click()
  await expect(page).toHaveURL(/\/workflow\/definition\/designer\?id=\d+/, { timeout: 15_000 })
  const urgeStart = page.locator('.wf-tree > .wf-chain > .wf-chain-node').first()
  await addNode(page, urgeStart.locator(':scope > .wf-add .wf-add-btn'), /审批|Approval/i)
  drawer = await visibleDrawer(page)
  await configureAssignee(page, drawer, /全部数据/)
  await saveDrawer(page, drawer)
  await publishDefinition(page)

  await startInstance(page, selfDef, returnKey)
  await expect(page.getByRole('button', { name: /^(退回|Return)$/i })).toBeVisible()
  await page.getByRole('button', { name: /^(退回|Return)$/i }).click()
  await confirmAction(page)
  await expect(page.locator('.n-tag, .n-timeline').getByText(/^(退回|Return)$/i).first()).toBeVisible({ timeout: 15_000 })
  await page.screenshot({ path: RETURN_SHOT, fullPage: true })

  await startInstance(page, selfDef, cancelKey)
  await expect(page.getByRole('button', { name: /^(撤销|Cancel instance)$/i })).toBeVisible()
  await page.getByRole('button', { name: /^(撤销|Cancel instance)$/i }).click()
  await confirmAction(page)
  await expect(page.locator('.n-descriptions').getByText(/已撤销|Cancelled/i)).toBeVisible({ timeout: 15_000 })
  await page.screenshot({ path: CANCEL_SHOT, fullPage: true })

  await startInstance(page, urgeDef, urgeKey)
  await expect(page.getByRole('button', { name: /^(催办|Urge)$/i })).toBeVisible()
  await page.getByRole('button', { name: /^(催办|Urge)$/i }).click()
  await confirmAction(page)
  await expect(page.locator('.n-message').filter({ hasText: /成功|Success/i }).last()).toBeVisible({ timeout: 15_000 })
  await page.screenshot({ path: URGE_SHOT, fullPage: true })

  await page.goto('/workflow/cc')
  const ccRow = page.locator('.n-data-table-tr').filter({ hasText: returnKey })
  await expect(ccRow).toBeVisible({ timeout: 15_000 })
  await expect(ccRow).toContainText(/已读|Read/i)
  await page.screenshot({ path: CC_SHOT, fullPage: true })

  await page.goto('/workflow/mine')
  await expect(page.locator('.n-data-table-tr').filter({ hasText: returnKey })).toBeVisible({ timeout: 15_000 })
  await expect(page.locator('.n-data-table-tr').filter({ hasText: cancelKey })).toBeVisible()
  await page.screenshot({ path: MINE_SHOT, fullPage: true })

  await page.goto('/workflow/done')
  await expect(page.locator('.n-data-table-tr').filter({ hasText: returnKey })).toBeVisible({ timeout: 15_000 })
  await page.screenshot({ path: DONE_SHOT, fullPage: true })
})
