import { fileURLToPath } from 'node:url'

import { expect, test, type Locator, type Page } from '@playwright/test'

import { enterApp, login, SYSTEM_APP } from './helpers'

const DESIGNER_SHOT = fileURLToPath(new URL('../../.loop/wf-ui-shots/m2a-01-designer-published.png', import.meta.url))
const HIGH_SHOT = fileURLToPath(new URL('../../.loop/wf-ui-shots/m2a-02-high-approved.png', import.meta.url))
const DEFAULT_SHOT = fileURLToPath(new URL('../../.loop/wf-ui-shots/m2a-03-default-approved.png', import.meta.url))

const APPROVAL_NAME = '总经理审批'
const CONDITION_FIELD = 'amount'
const CONDITION_THRESHOLD = '10000'
const HIGH_AMOUNT = '20000'
const DEFAULT_AMOUNT = '5000'

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

async function chooseDefinition(page: Page, definitionName: string) {
  const definitionSelect = page.locator('.n-form-item').first().locator('.n-select')
  await chooseNaiveOption(page, definitionSelect, new RegExp(definitionName))
}

async function startInstance(page: Page, definitionName: string, businessKey: string, amount: string) {
  await page.goto('/workflow/start')
  await expect(page.getByText(/发起流程|Start workflow/i).first()).toBeVisible()
  await chooseDefinition(page, definitionName)
  const businessKeyInput = page.getByPlaceholder(/可选.*业务单据|business document key/i)
  const variableKeyInput = page.getByPlaceholder(/^键名$|^Key$/i)
  const variableValueInput = page.getByPlaceholder(/^值$|^Value$/i)
  await expect(businessKeyInput).toBeVisible({ timeout: 10_000 })
  await expect(variableKeyInput).toBeVisible({ timeout: 10_000 })
  await expect(variableValueInput).toBeVisible({ timeout: 10_000 })
  await businessKeyInput.fill(businessKey)
  await variableKeyInput.fill(CONDITION_FIELD)
  await variableValueInput.fill(amount)
  await page.getByRole('button', { name: /提交发起|Submit/i }).click()
  await expect(page).toHaveURL(/\/workflow\/instance\/\d+\/detail/, { timeout: 15_000 })
  const match = /\/workflow\/instance\/(\d+)\/detail/.exec(page.url())
  expect(match).not.toBeNull()
  return Number(match![1])
}

test('M2a branch definition routes high amount to approval and low amount to default', async ({ page }) => {
  test.setTimeout(180_000)

  const suffix = Date.now().toString(36)
  const definitionName = `M2a UI ${suffix}`
  const highBusinessKey = `M2A-HIGH-${suffix}`
  const defaultBusinessKey = `M2A-DEFAULT-${suffix}`

  await login(page)
  await enterApp(page, SYSTEM_APP)
  await page.goto('/workflow/definition/designer')
  await expect(page.getByText(/未打开流程|No workflow opened/i)).toBeVisible()

  await page.getByPlaceholder(/流程名称|Workflow name/i).fill(definitionName)
  await page.getByRole('button', { name: /新建草稿|Create draft/i }).click()
  await expect(page).toHaveURL(/\/workflow\/definition\/designer\?id=\d+/, { timeout: 15_000 })
  await expect(page.locator('.wf-card.is-root')).toBeVisible()

  const mainChain = page.locator('.wf-tree > .wf-chain')
  const startNode = mainChain.locator(':scope > .wf-chain-node').first()
  await addNode(page, startNode.locator(':scope > .wf-add .wf-add-btn'), /条件分支|Branch/i)

  let drawer = await visibleDrawer(page)
  await drawer.getByRole('button', { name: /取消|Cancel/i }).click()
  await expect(drawer).toBeHidden()

  const firstNormalArm = page
    .locator('.wf-branch')
    .first()
    .locator('.wf-arm')
    .filter({ hasNot: page.locator('.wf-arm-default') })
    .first()
  await addNode(
    page,
    firstNormalArm.locator(':scope > .wf-arm-body > .wf-add .wf-add-btn'),
    /审批|Approval/i,
  )

  drawer = await visibleDrawer(page)
  await drawer.getByPlaceholder(/节点名称|Node name/i).fill(APPROVAL_NAME)
  const assigneeItem = formItemByLabel(drawer, /^办理人$|^Assignee$/i)
  await chooseNaiveOption(page, assigneeItem.locator('.n-select'), /指定成员|Specified users/i)
  const usersItem = formItemByLabel(drawer, /^指定成员$|^Members$/i)
  const usersSelect = usersItem.locator('.n-select')
  await expect(usersSelect).toBeVisible({ timeout: 10_000 })
  await usersSelect.locator('.n-base-selection').click()
  const adminOption = page.locator('.n-base-select-option:visible').filter({ hasText: /superAdmin/i }).first()
  await expect(adminOption).toBeVisible({ timeout: 10_000 })
  await adminOption.click()
  await page.keyboard.press('Escape')
  await expect(usersSelect).toContainText(/superAdmin/i)
  await drawer.getByRole('button', { name: /保存|Save/i }).click()
  await expect(drawer).toBeHidden()

  await page.locator('.wf-card.is-branch').click()
  drawer = await visibleDrawer(page)
  const activeArm = drawer.locator('.wf-branch-conditions > .n-collapse-item--active')
  await expect(activeArm).toHaveCount(1)
  await activeArm.getByRole('button', { name: /^(添加条件|Add condition)$/i }).click()
  await activeArm.getByPlaceholder(/变量字段|Variable field/i).fill(CONDITION_FIELD)
  await chooseNaiveOption(page, activeArm.getByLabel(/比较方式|Operator/i), /^大于$|^Greater than$/i)
  await activeArm.getByPlaceholder(/比较值|Value/i).fill(CONDITION_THRESHOLD)
  await drawer.getByRole('button', { name: /保存|Save/i }).click()
  await expect(drawer).toBeHidden()

  await expect(page.locator('.wf-card.is-approval')).toContainText(APPROVAL_NAME)
  const publishResponse = page.waitForResponse((response) =>
    response.url().includes('/api/v1/workflow/definition/publish') && response.request().method() === 'POST',
  )
  await page.getByRole('button', { name: /^发布$|^Publish$/i }).click()
  const response = await publishResponse
  expect(response.ok()).toBe(true)
  expect((await response.json()).code).toBe(0)
  await expect(page.locator('.n-message').filter({ hasText: /已发布|Published/i }).last()).toBeVisible()
  await expect(page.locator('.wf-card.is-branch')).toBeVisible()
  await expect(page.locator('.wf-card.is-approval')).toContainText(APPROVAL_NAME)
  await page.screenshot({ path: DESIGNER_SHOT, fullPage: true })

  const highInstanceId = await startInstance(page, definitionName, highBusinessKey, HIGH_AMOUNT)
  await expect(page.locator('.n-descriptions').getByText(/审批中|Running/i)).toBeVisible()
  await expect(page.getByRole('button', { name: /同意|Approve/i })).toBeVisible()

  await page.goto('/workflow/todo')
  const todoRow = page.locator('.n-data-table-tr').filter({ hasText: highBusinessKey })
  await expect(todoRow).toBeVisible()
  await expect(todoRow).toContainText(APPROVAL_NAME)
  await todoRow.getByRole('button', { name: /办理|Handle/i }).click()
  await expect(page).toHaveURL(new RegExp(`/workflow/instance/${highInstanceId}/detail`))

  await page.getByRole('button', { name: /同意|Approve/i }).click()
  const actionModal = page.locator('.n-modal:visible')
  await expect(actionModal).toBeVisible()
  await actionModal.getByRole('button', { name: /确定|Confirm/i }).click()
  await expect(actionModal).toBeHidden()
  await expect(page.locator('.n-descriptions').getByText(/已通过|Approved/i)).toBeVisible({ timeout: 15_000 })
  await expect(page.getByRole('button', { name: /同意|Approve/i })).toHaveCount(0)
  await page.screenshot({ path: HIGH_SHOT, fullPage: true })

  const defaultInstanceId = await startInstance(page, definitionName, defaultBusinessKey, DEFAULT_AMOUNT)
  expect(defaultInstanceId).not.toBe(highInstanceId)
  await expect(page.locator('.n-descriptions').getByText(/已通过|Approved/i)).toBeVisible()
  await expect(page.getByRole('button', { name: /同意|Approve/i })).toHaveCount(0)
  await expect(page.getByText(defaultBusinessKey)).toBeVisible()
  await page.screenshot({ path: DEFAULT_SHOT, fullPage: true })
})
