import { expect, type Locator, type Page } from '@playwright/test'

import { chooseMultiple, chooseOption, formItemByLabel } from './antd'

/**
 * 工作流三个套件共用的设计器 / 发起夹具。
 *
 * **模块分区是硬前置**:`/workflow/definition*`、`/workflow/monitor`、`/workflow/delegation` 挂在「系统」,
 * `/workflow/{start,todo,cc,mine,done}` 挂在「业务中心」。调用方负责先 `enterSystemApp` / `enterBusinessApp`,
 * 这里只断言页面真的到了 —— 切错应用会落 404,后面的断言就都在假现场上跑。
 */

/** 主链(不含分支臂内的子链)。 */
export const MAIN_CHAIN = '.wf-tree > .wf-chain'

/** 点加号插入节点。加号弹层是 Popover,里面每个入口是一颗按钮。 */
export async function addNode(page: Page, trigger: Locator, nodeName: RegExp) {
  await trigger.click()
  const popover = page.locator('.ant-popover:visible')
  await expect(popover).toBeVisible({ timeout: 10_000 })
  await popover.getByRole('button', { name: nodeName }).click()
}

/** 主链上某个节点之后的加号。 */
export function addAfter(page: Page, index: number) {
  return page.locator(`${MAIN_CHAIN} > .wf-chain-node`).nth(index).locator(':scope > .wf-add .wf-add-btn')
}

/** 空态建草稿,落在 `?id=` 的设计器上(系统应用)。 */
export async function createDraft(page: Page, name: string) {
  await page.goto('/workflow/definition/designer')
  await expect(page.getByText(/尚未打开流程|No workflow opened/i)).toBeVisible({ timeout: 15_000 })
  await page.getByPlaceholder(/流程名称|Workflow name/i).fill(name)
  await page.getByRole('button', { name: /新建草稿|Create draft/i }).click()
  await expect(page).toHaveURL(/\/workflow\/definition\/designer\?id=\d+/, { timeout: 15_000 })
  await expect(page.locator('.wf-card.is-root')).toBeVisible()
}

/** 审批/抄送节点配「指定成员」。 */
export async function configureAssignee(page: Page, drawer: Locator, who: RegExp) {
  await chooseOption(page, formItemByLabel(drawer, /^(办理人|Assignee)$/).locator('.ant-select'), /指定成员|Specified/i)
  await chooseMultiple(page, formItemByLabel(drawer, /^(指定成员|Members)$/).locator('.ant-select'), who)
}

/** 保存草稿并等后端确认(设计器保存是 `definition/update`)。 */
export async function saveDefinition(page: Page) {
  const saved = page.waitForResponse((r) =>
    r.url().includes('/api/v1/workflow/definition/update') && r.request().method() === 'POST')
  await page.getByRole('button', { name: /^(保\s*存|Save)$/i }).click()
  expect((await saved).ok()).toBe(true)
}

/** 发布并等后端确认。设计器的发布会先存一次草稿,故两条响应都要等。 */
export async function publishDefinition(page: Page) {
  const saved = page.waitForResponse((r) =>
    r.url().includes('/api/v1/workflow/definition/update') && r.request().method() === 'POST')
  const published = page.waitForResponse((r) =>
    r.url().includes('/api/v1/workflow/definition/publish') && r.request().method() === 'POST')
  await page.getByRole('button', { name: /^(发\s*布|Publish)$/i }).click()
  expect((await saved).ok()).toBe(true)
  const response = await published
  expect(response.ok()).toBe(true)
  expect((await response.json()).code).toBe(0)
}

/** 发起一单,返回实例 Id(业务中心应用)。`variable` 用于条件分支取值。 */
export async function startInstance(
  page: Page,
  definitionName: string,
  businessKey: string,
  variable?: { key: string; value: string },
): Promise<number> {
  await page.goto('/workflow/start')
  await expect(page.getByText(/发起流程|Start workflow/i).first()).toBeVisible({ timeout: 15_000 })

  await chooseOption(
    page,
    formItemByLabel(page.locator('body'), /^(流程|Workflow)$/).locator('.ant-select'),
    new RegExp(definitionName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')),
  )
  await page.getByPlaceholder(/可选.*业务单据|business document/i).fill(businessKey)
  if (variable) {
    await page.getByPlaceholder(/^(键名|Key)$/).fill(variable.key)
    await page.getByPlaceholder(/^(值|Value)$/).fill(variable.value)
  }
  await page.getByRole('button', { name: /提交发起|Submit/i }).click()

  await expect(page).toHaveURL(/\/workflow\/instance\/\d+\/detail/, { timeout: 20_000 })
  const match = /\/workflow\/instance\/(\d+)\/detail/.exec(page.url())
  expect(match).not.toBeNull()
  return Number(match![1])
}

/** 详情页状态(基本信息里的状态 Tag)。 */
export function instanceStatus(page: Page) {
  return page.locator('.ant-descriptions').first()
}
