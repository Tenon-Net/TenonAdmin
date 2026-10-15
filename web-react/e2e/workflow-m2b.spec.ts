import { expect, test } from '@playwright/test'
import { apiAdminToken, apiCreateUser } from './api'

import { confirmModal, expectMessage, saveDrawer, tableRow, visibleDrawer } from './antd'
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

test('M2b 动词:退回、撤销、催办,以及抄送已读 / 我发起的 / 我已办的', async ({ page, request }) => {
  test.setTimeout(240_000)

  const suffix = Date.now().toString(36)
  const selfDef = `M2b Self ${suffix}`
  const urgeDef = `M2b Urge ${suffix}`
  const returnKey = `M2B-RET-${suffix}`
  const cancelKey = `M2B-CAN-${suffix}`
  const urgeKey = `M2B-URG-${suffix}`
  // 催办需要一个非发起人的办理人。#44 起内置数据范围演示账号(如「全部数据」)不再出现在办理人选择器里，
  // 这里建一个专用账号代替。
  const urgeApprover = `m2b-urge-${suffix}`
  await apiCreateUser(request, await apiAdminToken(request), {
    account: urgeApprover, name: urgeApprover, password: 'M2b@Urge123456', forceTotp: false,
  })

  await login(page)
  await enterSystemApp(page)

  // 抄送 → 审批(都给自己),用于退回与撤销。
  await createDraft(page, selfDef)
  await addNode(page, addAfter(page, 0), /^(抄送|CC)$/i)
  let drawer = await visibleDrawer(page)
  await configureAssignee(page, drawer, /superAdmin/i)
  await saveDrawer(page, drawer)

  await addNode(page, addAfter(page, 1), /^(审批|Approval)$/i)
  drawer = await visibleDrawer(page)
  await drawer.getByLabel(/节点名称|Node name/i).fill('M2b 审批')
  await configureAssignee(page, drawer, /superAdmin/i)
  // 低频项收在「高级」里:退回策略不该出现在默认可见区。
  await expect(drawer.getByText(/退回策略|Return policy/i)).toHaveCount(0)
  await drawer.locator('.wf-advanced .ant-collapse-header').click()
  await expect(drawer.getByText(/退回策略|Return policy/i)).toBeVisible()
  await saveDrawer(page, drawer)
  await publishDefinition(page)

  // 审批人是别人 → 发起人只剩催办。
  await createDraft(page, urgeDef)
  await addNode(page, addAfter(page, 0), /^(审批|Approval)$/i)
  drawer = await visibleDrawer(page)
  await configureAssignee(page, drawer, new RegExp(urgeApprover))
  await saveDrawer(page, drawer)
  await publishDefinition(page)

  await enterBusinessApp(page)

  await startInstance(page, selfDef, returnKey)
  await page.getByRole('button', { name: /^(退\s*回|Return)$/i }).click()
  // #45 起退回意见必填：空意见点确定，弹窗应保持打开；填写后才能提交。
  const returnModal = page.locator('[role="dialog"]:visible')
  await expect(returnModal).toBeVisible({ timeout: 10_000 })
  await returnModal.getByRole('button', { name: /^(确\s*定|OK|Confirm)$/i }).click()
  await expect(returnModal).toBeVisible()
  await returnModal.locator('textarea').first().fill('M2b 退回意见')
  await confirmModal(page)
  await expect(page.locator('.ant-timeline')).toContainText(/退\s*回|Return/i, { timeout: 20_000 })

  await startInstance(page, selfDef, cancelKey)
  await page.getByRole('button', { name: /^(撤\s*销|Cancel instance)$/i }).click()
  await confirmModal(page)
  await expect(instanceStatus(page).getByText(/已撤销|Cancelled/i)).toBeVisible({ timeout: 20_000 })

  await startInstance(page, urgeDef, urgeKey)
  await page.getByRole('button', { name: /^(催\s*办|Urge)$/i }).click()
  await confirmModal(page)
  await expectMessage(page, /操作成功|Success/i)

  // 抄送在流程里已送达;点开详情即标已读,列表回来必须是「已读」。
  await page.goto('/workflow/cc')
  const ccRow = tableRow(page, returnKey)
  await expect(ccRow).toBeVisible({ timeout: 20_000 })
  await expect(ccRow).toContainText(/已读|Read/i)

  await page.goto('/workflow/mine')
  await expect(tableRow(page, returnKey)).toBeVisible({ timeout: 20_000 })
  await expect(tableRow(page, cancelKey)).toBeVisible()

  await page.goto('/workflow/done')
  await expect(tableRow(page, returnKey)).toBeVisible({ timeout: 20_000 })
})
