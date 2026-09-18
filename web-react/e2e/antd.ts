import { expect, type Locator, type Page } from '@playwright/test'

/**
 * antd 交互原语。Naive UI 版的对偶(`web/e2e` 里各 spec 各抄一份),这里集中一处:
 * 选择器一旦随 antd 升级改名,只改本文件。
 */

/**
 * 展开 antd Select 并选中一项。
 *
 * **不能用 `getByRole('option')`**:rc-select 给读屏器另挂了一份 `role=option` 的零尺寸镜像列表
 * (`height:0;width:0;overflow:hidden`),可见的真选项在虚拟列表里、只有 `.ant-select-item-option` 类。
 * 按角色取会拿到永不可见的那份并一直等超时。
 */
export async function chooseOption(page: Page, select: Locator, option: RegExp) {
  await expect(select).toBeVisible({ timeout: 10_000 })
  await select.locator('.ant-select-content').first().click()
  const item = page
    .locator('.ant-select-dropdown:visible .ant-select-item-option')
    .filter({ hasText: option })
    .first()
  await expect(item).toBeVisible({ timeout: 10_000 })
  await item.click()
}

/** 多选 Select:选完下拉不会自动收,按 Escape 收起再断言选中态。 */
export async function chooseMultiple(page: Page, select: Locator, option: RegExp) {
  await chooseOption(page, select, option)
  await page.keyboard.press('Escape')
  await expect(select).toContainText(option)
}

/**
 * 按标签文字回溯表单项。无 `name` 的 `Form.Item` 不会给 label 挂 `for`,
 * `getByLabel` 对这类控件无效,只能从标签往上找最近的 `.ant-form-item`。
 */
export function formItemByLabel(scope: Locator, label: RegExp) {
  return scope
    .locator('.ant-form-item-label label')
    .filter({ hasText: label })
    .first()
    .locator('xpath=ancestor::*[contains(concat(" ", normalize-space(@class), " "), " ant-form-item ")][1]')
}

/**
 * 当前可见的弹层面板。**按 `role=dialog` 取而不按类名**:antd 6 把面板类名从 `-content`
 * 改成了 `-section`(Drawer)/`-container`(Modal),角色则是 ARIA 契约、跨版本稳定。
 */
export function visibleDialog(page: Page) {
  return page.locator('[role="dialog"]:visible')
}

/** 当前可见的抽屉;超过一个说明上一个没关干净,不能靠 `.first()` 掩过去。 */
export async function visibleDrawer(page: Page) {
  const drawer = visibleDialog(page)
  await expect(drawer).toHaveCount(1)
  return drawer
}

export async function saveDrawer(page: Page, drawer: Locator) {
  await drawer.getByRole('button', { name: /^(保\s*存|Save)$/i }).click()
  await expect(drawer).toBeHidden({ timeout: 10_000 })
}

export async function cancelDrawer(page: Page, drawer: Locator) {
  await drawer.getByRole('button', { name: /^(取\s*消|Cancel)$/i }).click()
  await expect(drawer).toBeHidden({ timeout: 10_000 })
}

/** 确认当前弹窗(详情页动词弹窗、useConfirm 二次确认都是 antd Modal)。 */
export async function confirmModal(page: Page) {
  const modal = visibleDialog(page)
  await expect(modal).toBeVisible({ timeout: 10_000 })
  await modal.getByRole('button', { name: /^(确\s*定|OK|Confirm)$/i }).click()
  await expect(modal).toBeHidden({ timeout: 20_000 })
}

/** 断言本动作的 message 提示。antd 会堆叠通知,按文案过滤而不是对整类做单节点断言。 */
export async function expectMessage(page: Page, text: RegExp, timeout = 15_000) {
  await expect(page.locator('.ant-message-notice').filter({ hasText: text }).first()).toBeVisible({ timeout })
}

/** ProTable(antd Table)按文字取行。 */
export function tableRow(page: Page, text: string) {
  return page.locator('.ant-table-row').filter({ hasText: text })
}
