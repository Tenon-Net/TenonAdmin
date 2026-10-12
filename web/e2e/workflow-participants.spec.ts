import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { apiAdminToken, apiCreateViewOnlyUser } from './api'
import { enterBusinessApp, login } from './helpers'
import type { components } from '../src/api/schema'

type Detail = components['schemas']['WfInstanceDetailOutput']
type Node = components['schemas']['WfNode']
type Id = number | string
type Person = { id: Id; token: string; name: string }
const apiBase = () => process.env.TENON_E2E_API_BASE!
let admin: string
let people: Person[]

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

test.beforeAll(async ({ request }) => {
  admin = await apiAdminToken(request)
  people = []
  for (const [index, name] of ['张三', '李四', '王五'].entries()) {
    const user = await apiCreateViewOnlyUser(request, admin, {
      prefix: `wf-person-${index}`, name, menuIds: [48050, 48021],
    })
    const auth = await call<{ accessToken: string; userId: Id }>(request, '', 'auth/login', { account: user.account, password: user.password })
    people.push({ id: auth.userId, token: auth.accessToken, name })
  }
})

test.beforeEach(async ({ page }) => {
  await login(page)
  await enterBusinessApp(page)
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
  const layout = await card.locator('.wf-card-text.is-runtime').evaluate((element) => ({ height: element.clientHeight, content: element.scrollHeight }))
  expect(layout.height, '节点摘要不能裁掉等待人员').toBeGreaterThanOrEqual(layout.content - 1)
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
  const dialog = page.getByTestId('wf-approval-progress-modal')
  await expect(dialog).toBeVisible()
  await dialog.locator('details.wf-progress-round > summary').first().click()
  await expect(dialog.getByText('第一轮意见', { exact: true })).toBeVisible()
  const pastPeople = dialog.locator('details.wf-progress-round[open] .wf-progress-person')
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
  await expect(nodeCard(page, '部门审批')).toContainText('王五')
  await expect(nodeCard(page, '财务审批')).toContainText('李四')
  await page.screenshot({ path: info.outputPath('parallel-transfer.png'), fullPage: true, animations: 'disabled' })
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
  const dialog = page.getByTestId('wf-approval-progress-modal')
  await dialog.locator('details.wf-progress-round > summary').first().click()
  const pastPeople = dialog.locator('details.wf-progress-round[open] .wf-progress-person')
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
