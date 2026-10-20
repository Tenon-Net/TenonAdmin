import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { apiAdminToken, apiCreateViewOnlyUser } from './api'
import { enterBusinessApp, enterSystemApp, login } from './helpers'
import type { components } from '../src/api/schema'

type Detail = components['schemas']['WfInstanceDetailOutput']
type Node = components['schemas']['WfNode']
type Id = number | string
type Person = { id: Id; token: string; name: string; account: string }
type ParticipantPage = { items: Array<{ name?: string; account?: string }>; total: number }
const apiBase = () => process.env.TENON_E2E_API_BASE!
let admin: string
let people: Person[]
const excludedDemoAccounts = ['全部数据', '本机构数据', '本机构及以下', '仅本人数据', '自定义范围'] as const

async function call<T>(request: APIRequestContext, token: string, path: string, data?: unknown): Promise<T> {
  const response = data === undefined
    ? await request.get(`${apiBase()}/api/v1/${path}`, { headers: { Authorization: `Bearer ${token}` } })
    : await request.post(`${apiBase()}/api/v1/${path}`, { headers: { Authorization: `Bearer ${token}` }, data })
  const body = await response.json()
  expect(response.ok(), `${path}: HTTP ${response.status()}`).toBe(true)
  expect(body.code, `${path}: ${JSON.stringify(body)}`).toBe(0)
  return body.data as T
}

function approval(id: string, name: string, ids: Id[], mode: 'seq' | 'any' | 'all' = 'any'): Node {
  return { id, name, type: 'approval', props: { mode, returnPolicy: 'prev', assignee: { provider: 'user', params: { userIds: ids } } } }
}

async function start(request: APIRequestContext, next: Node): Promise<Id> {
  const definitionId = await call<Id>(request, admin, 'workflow/definition/add', {
    name: `人员展示-${Date.now()}`, model: { version: 1, root: { id: 'start', name: '发起人', type: 'start', next } },
  })
  await call(request, admin, 'workflow/definition/publish', { id: definitionId })
  const result = await call<{ instanceId: Id }>(request, admin, 'workflow/instance/start', { definitionId })
  return result.instanceId
}
const detail = (request: APIRequestContext, id: Id) => call<Detail>(request, admin, `workflow/instance/${id}`)
async function open(page: Page, id: Id) {
  await page.goto(`/workflow/instance/${id}/detail`)
  await expect(page.locator('.wf-tree')).toBeVisible()
  await expect(page.locator('.wf-tree')).not.toContainText('请选择审批人')
}
const nodeCard = (page: Page, name: string) => page.locator('.wf-card').filter({ hasText: name })

function formItemByLabel(scope: Locator, label: RegExp) {
  return scope
    .locator('.ant-form-item-label label')
    .filter({ hasText: label })
    .first()
    .locator('xpath=ancestor::*[contains(concat(" ", normalize-space(@class), " "), " ant-form-item ")][1]')
}

async function openDesignerParticipantSelect(page: Page, request: APIRequestContext, suffix: string) {
  const definitionId = await call<Id>(request, admin, 'workflow/definition/add', {
    name: `办理人过滤-${suffix}-${Date.now()}`,
    model: {
      version: 1,
      root: {
        id: 'start', type: 'start', name: '发起人',
        next: approval('participant', '办理人过滤', []),
      },
    },
  })
  await enterSystemApp(page)
  await page.goto(`/workflow/definition/designer?id=${definitionId}`)
  const card = page.locator('.wf-card.is-approval').filter({ hasText: '办理人过滤' })
  await expect(card).toBeVisible({ timeout: 15_000 })
  await card.click()
  const drawer = page.locator('[role="dialog"]:visible')
  await expect(drawer).toHaveCount(1)
  const select = formItemByLabel(drawer, /^(指定成员|Members)$/).locator('.ant-select')
  await expect(select).toBeVisible()
  return select
}

async function searchDesignerParticipants(page: Page, select: Locator, keyword: string) {
  await select.locator('.ant-select-content').first().click()
  const searched = page.waitForResponse((response) => {
    const url = new URL(response.url())
    return url.pathname === '/api/v1/workflow/participant/page'
      && response.request().method() === 'GET'
      && url.searchParams.get('Keyword') === keyword
  })
  await select.locator('input').fill(keyword)
  const response = await searched
  expect(response.ok(), `${keyword}: HTTP ${response.status()}`).toBe(true)
  const body = await response.json() as { code?: number; data?: ParticipantPage }
  expect(body.code, `${keyword}: participant response`).toBe(0)
  expect(body.data, `${keyword}: participant data`).toBeDefined()
  return { data: body.data!, options: page.locator('.ant-select-dropdown:visible .ant-select-item-option') }
}

test.beforeAll(async ({ request }) => {
  admin = await apiAdminToken(request)
  people = []
  for (const [index, name] of ['张三', '李四', '王五'].entries()) {
    const user = await apiCreateViewOnlyUser(request, admin, {
      prefix: `wf-person-${index}`, name, menuIds: [48050, 48021],
    })
    const auth = await call<{ accessToken: string; userId: Id }>(request, '', 'auth/login', { account: user.account, password: user.password })
    people.push({ id: auth.userId, token: auth.accessToken, name, account: user.account })
  }
})

test.beforeEach(async ({ page }) => {
  await login(page)
  await enterBusinessApp(page)
})

for (const account of excludedDemoAccounts) {
  test(`设计器办理人不显示内置数据范围演示账号：${account}`, async ({ page, request }) => {
    const select = await openDesignerParticipantSelect(page, request, account)
    const { data, options } = await searchDesignerParticipants(page, select, account)
    expect(data.items, `${account} 的真实接口候选项应为空`).toEqual([])
    expect(data.total, `${account} 的真实接口候选数应为 0`).toBe(0)
    await expect(options, `${account} 不应出现在办理人候选项`).toHaveCount(0)
  })
}

test('设计器办理人可检索并选中正常中文姓名账号', async ({ page, request }) => {
  const select = await openDesignerParticipantSelect(page, request, '正常中文姓名')
  const { data, options } = await searchDesignerParticipants(page, select, '张三')
  expect(data.items.some((item) => item.name === '张三' && item.account === people[0]!.account)).toBe(true)
  const option = options.filter({ hasText: `张三(${people[0]!.account})` })
  await expect(option).toHaveCount(1)
  await option.click()
  await page.keyboard.press('Escape')
  await expect(select).toContainText('张三')
})

test('顺序审批展示实际姓名与等待队列，并隔离退回重审轮次', async ({ page, request }, info) => {
  test.setTimeout(90_000)
  const firstNode = approval('dept', '部门审批', people.map((person) => person.id), 'seq')
  firstNode.next = approval('final', '最终审批', [people[2]!.id])
  const id = await start(request, firstNode)
  let instance = await detail(request, id)
  const taskId = instance.currentTasks![0]!.taskId!
  await open(page, id)
  let card = nodeCard(page, '部门审批')
  for (const person of people) await expect(card).toContainText(person.name)
  await expect(card).toContainText('待审批')
  await expect(card).toContainText('等待轮到')
  await expect(nodeCard(page, '最终审批')).toContainText('未开始')
  await expect(nodeCard(page, '最终审批')).not.toContainText('王五')
  await page.screenshot({ path: info.outputPath('sequential-pending.png'), fullPage: true, animations: 'disabled' })

  await call(request, people[0]!.token, 'workflow/task/approve', { taskId, comment: '第一轮意见' })
  instance = await detail(request, id)
  expect(instance.currentTasks![0]!.participants!.filter((person) => person.status === 1).map((person) => person.displayName)).toEqual(['李四'])
  await page.reload()
  card = nodeCard(page, '部门审批')
  await expect(card).toContainText('张三')
  await expect(card).toContainText(/同意|已处理/)
  await expect(card).toContainText('李四')
  await page.screenshot({ path: info.outputPath('sequential-approved.png'), fullPage: true, animations: 'disabled' })

  await call(request, people[1]!.token, 'workflow/task/return', { taskId, comment: '补充材料后重审' })
  await call(request, admin, 'workflow/instance/resubmit', { instanceId: id })
  instance = await detail(request, id)
  expect(String(instance.currentTasks![0]!.taskId)).not.toBe(String(taskId))
  expect(instance.pastTasks!.some((task) => String(task.taskId) === String(taskId))).toBe(true)
  await page.reload()
  card = nodeCard(page, '部门审批')
  await expect(card).toContainText('张三')
  await expect(card).toContainText('待审批')
  await card.click()
  const dialog = page.locator('[data-wf-approval-detail="dept"]')
  await expect(dialog).toBeVisible()
  await dialog.getByText(/历史轮次/).click()
  await dialog.getByText('第 1 轮', { exact: true }).click()
  await expect(dialog.getByText('第一轮意见', { exact: true })).toBeVisible()
  const pastPeople = dialog.locator('.ant-collapse .wf-approval-person')
  await expect(pastPeople.filter({ hasText: '张三' })).toContainText('同意')
  await expect(pastPeople.filter({ hasText: '李四' })).toContainText('退回')
  await expect(pastPeople.filter({ hasText: '王五' })).toContainText('无需处理')
  await page.screenshot({ path: info.outputPath('returned-rounds.png'), fullPage: true, animations: 'disabled' })
})

test('会签展示部分完成，或签完成后其他人员无需处理', async ({ page, request }, info) => {
  test.setTimeout(90_000)
  const allId = await start(request, approval('all', '共同审批', people.slice(0, 2).map((person) => person.id), 'all'))
  let instance = await detail(request, allId)
  await call(request, people[0]!.token, 'workflow/task/approve', { taskId: instance.currentTasks![0]!.taskId, comment: '会签意见' })
  await open(page, allId)
  await expect(nodeCard(page, '共同审批')).toContainText('张三')
  await expect(nodeCard(page, '共同审批')).toContainText('李四')
  await expect(nodeCard(page, '共同审批')).toContainText('待审批')
  await page.screenshot({ path: info.outputPath('countersign.png'), fullPage: true, animations: 'disabled' })

  const anyId = await start(request, approval('any', '任一人审批', people.slice(0, 2).map((person) => person.id)))
  instance = await detail(request, anyId)
  await call(request, people[0]!.token, 'workflow/task/approve', { taskId: instance.currentTasks![0]!.taskId })
  await open(page, anyId)
  const card = nodeCard(page, '任一人审批')
  await expect(card).toContainText('张三')
  await expect(card).toContainText('李四')
  await expect(card).toContainText('无需处理')
  await expect(card).not.toContainText('待审批')
  await page.screenshot({ path: info.outputPath('any-completed.png'), fullPage: true, animations: 'disabled' })
})

test('并行节点各自展示人员，转交后更新实际待办人', async ({ page, request }, info) => {
  test.setTimeout(90_000)
  const id = await start(request, {
    id: 'parallel', type: 'parallel', name: '并行审批', parallelArms: [
      { id: 'left', name: '部门', next: approval('dept', '部门审批', [people[0]!.id]) },
      { id: 'right', name: 'finance', next: approval('finance', '财务审批', [people[1]!.id]) },
    ],
  })
  const instance = await detail(request, id)
  await open(page, id)
  await expect(nodeCard(page, '部门审批')).toContainText('张三')
  await expect(nodeCard(page, '部门审批')).not.toContainText('李四')
  await expect(nodeCard(page, '财务审批')).toContainText('李四')
  await expect(nodeCard(page, '财务审批')).not.toContainText('张三')
  await call(request, people[0]!.token, 'workflow/task/transfer', {
    taskId: instance.currentTasks!.find((task) => task.nodeId === 'dept')!.taskId, toUserId: people[2]!.id, comment: '由王五处理',
  })
  await page.reload()
  const transferredCard = nodeCard(page, '部门审批')
  await expect(transferredCard).toContainText('王五')
  await expect(transferredCard).toContainText(/张三\s*·\s*转办/)
  await expect(transferredCard).not.toContainText('无需处理')
  await expect(nodeCard(page, '财务审批')).toContainText('李四')
  await page.screenshot({ path: info.outputPath('parallel-transfer.png'), fullPage: true, animations: 'disabled' })
  await transferredCard.click()
  const dialog = page.locator('[data-wf-approval-detail="dept"]')
  const currentPeople = dialog.locator('.wf-approval-person')
  await expect(currentPeople.filter({ hasText: '张三' })).toContainText('转办')
  await expect(currentPeople.filter({ hasText: '张三' })).not.toContainText('无需处理')
  await expect(currentPeople.filter({ has: page.locator('strong').filter({ hasText: /^王五$/ }) })).toContainText('待审批')
})


test('无人办理时撤销，仍能查看全部原审批人员且无虚假待办', async ({ page, request }, info) => {
  const id = await start(request, approval('cancel', '撤销审批', people.map((person) => person.id), 'all'))
  await call(request, admin, 'workflow/instance/cancel', { instanceId: id, comment: '申请撤销' })
  const instance = await detail(request, id)
  expect(instance.currentTasks).toEqual([])
  expect(instance.pastTasks![0]!.participants).toHaveLength(3)
  await open(page, id)
  const card = nodeCard(page, '撤销审批')
  await expect(card).toContainText('无需处理')
  await expect(card).not.toContainText('待审批')
  await card.click()
  const dialog = page.locator('[data-wf-approval-detail="cancel"]')
  await dialog.getByText(/历史轮次/).click()
  await dialog.getByText('第 1 轮', { exact: true }).click()
  const pastPeople = dialog.locator('.ant-collapse .wf-approval-person')
  for (const person of people) await expect(pastPeople.filter({ hasText: person.name })).toContainText('无需处理')
  await page.screenshot({ path: info.outputPath('cancelled-participants.png'), fullPage: true, animations: 'disabled' })
})


test('退回早期节点后，下游旧轮通过不冒充本轮进度', async ({ page, request }) => {
  test.setTimeout(90_000)
  const first = approval('first', '部门初审', [people[0]!.id])
  const second = approval('second', '财务复审', [people[1]!.id])
  const last = approval('last', '负责人终审', [people[2]!.id])
  last.props = { ...last.props, returnPolicy: 'any' }
  first.next = second
  second.next = last
  const id = await start(request, first)
  for (const person of people.slice(0, 2)) {
    const current = await detail(request, id)
    await call(request, person.token, 'workflow/task/approve', { taskId: current.currentTasks![0]!.taskId })
  }
  const current = await detail(request, id)
  await call(request, people[2]!.token, 'workflow/task/return', {
    taskId: current.currentTasks![0]!.taskId, targetNodeId: 'first', comment: '从初审重新办理',
  })
  await call(request, admin, 'workflow/instance/resubmit', { instanceId: id })
  await open(page, id)
  await expect(nodeCard(page, '部门初审')).toContainText('待审批')
  await expect(nodeCard(page, '财务复审')).toContainText('未开始')
  await expect(nodeCard(page, '财务复审')).not.toContainText('同意')
})
