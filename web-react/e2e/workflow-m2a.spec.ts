import { expect, test } from '@playwright/test'

import { cancelDrawer, chooseOption, confirmModal, saveDrawer, tableRow, visibleDrawer } from './antd'
import { enterBusinessApp, enterSystemApp, login } from './helpers'
import {
  addAfter,
  addNode,
  configureAssignee,
  createDraft,
  instanceStatus,
  publishDefinition,
  startInstance,
} from './workflow'

const APPROVAL_NAME = '总经理审批'
const CONDITION_FIELD = 'amount'
const CONDITION_THRESHOLD = '10000'
const HIGH_AMOUNT = '20000'
const DEFAULT_AMOUNT = '5000'

test('M2a 条件分支:大额进审批、小额走默认臂', async ({ page }) => {
  test.setTimeout(180_000)

  const suffix = Date.now().toString(36)
  const definitionName = `M2a UI ${suffix}`
  const highBusinessKey = `M2A-HIGH-${suffix}`
  const defaultBusinessKey = `M2A-DEFAULT-${suffix}`

  // 治理页在「系统」应用下;先切应用再开路由,否则 React 侧同样没有这条路由。
  await login(page)
  await enterSystemApp(page)
  await createDraft(page, definitionName)

  // 发起人之后插条件分支;插入即打开抽屉,先取消(条件留到臂内节点配完再填)。
  await addNode(page, addAfter(page, 0), /条件分支|Branch/i)
  await cancelDrawer(page, await visibleDrawer(page))

  const firstNormalArm = page
    .locator('.wf-branch')
    .first()
    .locator('.wf-arm')
    .filter({ hasNot: page.locator('.wf-arm-default') })
    .first()
  await addNode(
    page,
    firstNormalArm.locator(':scope > .wf-arm-body > .wf-add .wf-add-btn'),
    /^(审批|Approval)$/i,
  )

  let drawer = await visibleDrawer(page)
  await drawer.getByLabel(/节点名称|Node name/i).fill(APPROVAL_NAME)
  await configureAssignee(page, drawer, /superAdmin/i)
  await saveDrawer(page, drawer)

  // 回到分支节点配条件:默认展开的就是第一条非默认臂。
  await page.locator('.wf-card.is-branch').click()
  drawer = await visibleDrawer(page)
  const activeArm = drawer.locator('.wf-branch-conditions > .ant-collapse-item-active')
  await expect(activeArm).toHaveCount(1)
  await activeArm.getByRole('button', { name: /^(添加条件|Add condition)$/i }).click()
  await activeArm.getByLabel(/变量字段|Variable field/i).fill(CONDITION_FIELD)
  // 叶子行里只有比较方式一个 Select(字段与值是 Input)。
  await chooseOption(page, activeArm.locator('.wf-condition-leaf .ant-select').first(), /^(大于|Greater than)$/i)
  // 数值型比较值是 InputNumber:限定 spinbutton,否则会连带匹配上下步进按钮的 aria-label。
  await activeArm.getByRole('spinbutton', { name: /比较值|Value/i }).fill(CONDITION_THRESHOLD)
  await saveDrawer(page, drawer)

  await expect(page.locator('.wf-card.is-approval')).toContainText(APPROVAL_NAME)
  await publishDefinition(page)
  await expect(page.locator('.wf-card.is-branch')).toBeVisible()

  // 员工侧在「业务中心」:不切应用直开 /workflow/start 会 404。
  await enterBusinessApp(page)
  const highInstanceId = await startInstance(page, definitionName, highBusinessKey, {
    key: CONDITION_FIELD,
    value: HIGH_AMOUNT,
  })
  await expect(instanceStatus(page).getByText(/审批中|Running/i)).toBeVisible()
  await expect(page.getByRole('button', { name: /^(同\s*意|Approve)$/i })).toBeVisible()

  await page.goto('/workflow/todo')
  const todoRow = tableRow(page, highBusinessKey)
  await expect(todoRow).toBeVisible({ timeout: 15_000 })
  await expect(todoRow).toContainText(APPROVAL_NAME)
  await todoRow.getByRole('button', { name: /办\s*理|Handle/i }).click()
  await expect(page).toHaveURL(new RegExp(`/workflow/instance/${highInstanceId}/detail`))

  await page.getByRole('button', { name: /^(同\s*意|Approve)$/i }).click()
  await confirmModal(page)
  await expect(instanceStatus(page).getByText(/已通过|Approved/i)).toBeVisible({ timeout: 20_000 })
  await expect(page.getByRole('button', { name: /^(同\s*意|Approve)$/i })).toHaveCount(0)

  // 小额落默认臂:默认臂为空 → 直接结单,详情里不该再有审批动作。
  const defaultInstanceId = await startInstance(page, definitionName, defaultBusinessKey, {
    key: CONDITION_FIELD,
    value: DEFAULT_AMOUNT,
  })
  expect(defaultInstanceId).not.toBe(highInstanceId)
  await expect(instanceStatus(page).getByText(/已通过|Approved/i)).toBeVisible({ timeout: 20_000 })
  await expect(page.getByRole('button', { name: /^(同\s*意|Approve)$/i })).toHaveCount(0)
  await expect(page.getByText(defaultBusinessKey)).toBeVisible()
})
