import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { login, enterApp, SYSTEM_APP } from './helpers'
import { apiAdminToken, apiCreateViewOnlyUser } from './api'
import { confirmModal, expectMessage, formItemByLabel, tableRow, visibleDialog } from './antd'

/**
 * 第三方接入(React):真实后端(MinimalHost 启用接入模块)下的接入应用管理闭环,与 Vue 端验收同一业务但独立实现。
 * 创建应用 → 发放凭据(一次性展示、不落前端存储)→ 未授权调用被拒 → 授权 → 调用成功 → 查看调用记录(深链 + 刷新)
 * → 轮换(旧凭据按窗口失效)→ 停用后拒绝;以及接口失败提示与无权限用户的按钮隐藏。
 * 另锁两处回归:授权抽屉加载失败时不能保存(不会把已有授权清空);轮换时并存窗口留空即按系统默认,旧凭据不立即失效。
 * 开放端点用模块内置的连通性检查 `GET /api/open/v1/whoami`(默认拒绝,须授予)。
 */

const apiBase = () => process.env.TENON_E2E_API_BASE ?? 'http://127.0.0.1:5101'
const WHOAMI = '/api/open/v1/whoami'
const ROOT_MENU_ID = 50000
const APP_MENU_ID = 50001
const APP_PAGE_BUTTON_ID = 50002
const APP_MANAGEMENT_BUTTON_IDS = Array.from({ length: 20 }, (_, i) => APP_PAGE_BUTTON_ID + i)

async function callWhoami(request: APIRequestContext, key: string) {
  const res = await request.get(`${apiBase()}${WHOAMI}`, { headers: { 'X-Api-Key': key } })
  return { status: res.status(), body: (await res.json()) as { code: number; data?: { appCode?: string } } }
}

const bearer = (token: string) => ({ Authorization: `Bearer ${token}` })

/** 经管理接口建一个启用的接入应用,返回其 id。 */
async function createApp(request: APIRequestContext, token: string, code: string) {
  const res = await request.post(`${apiBase()}/api/v1/integration/app`, { headers: bearer(token), data: { code, name: code, enabled: true } })
  const body = (await res.json()) as { code: number; data: number | string }
  expect(body.code).toBe(0)
  return body.data
}

/** 连通性检查端点的授权码,从开放端点清单里取而不手写。 */
async function whoamiPermission(request: APIRequestContext, token: string) {
  const res = await request.get(`${apiBase()}/api/v1/integration/catalog/endpoints`, { headers: bearer(token) })
  const endpoint = ((await res.json()) as { data: { permission: string; route: string }[] }).data.find((e) => e.route.endsWith('open/v1/whoami'))
  expect(endpoint).toBeTruthy()
  return endpoint!.permission
}

async function setGrants(request: APIRequestContext, token: string, appId: number | string, permissions: string[]) {
  const res = await request.put(`${apiBase()}/api/v1/integration/app/${appId}/grants`, { headers: bearer(token), data: { permissions } })
  expect(((await res.json()) as { code: number }).code).toBe(0)
}

async function readGrants(request: APIRequestContext, token: string, appId: number | string) {
  const res = await request.get(`${apiBase()}/api/v1/integration/app/${appId}/grants`, { headers: bearer(token) })
  return ((await res.json()) as { data: { permissions: string[] } }).data.permissions
}

async function issueCredential(request: APIRequestContext, token: string, appId: number | string) {
  const res = await request.post(`${apiBase()}/api/v1/integration/app/${appId}/credentials`, { headers: bearer(token), data: {} })
  const body = (await res.json()) as { code: number; data: { apiKey: string } }
  expect(body.code).toBe(0)
  return body.data.apiKey
}

async function gotoApps(page: Page) {
  await page.goto('/integration/app')
  await expect(page.locator('.ant-table')).toBeVisible({ timeout: 10_000 })
}

function dialogWith(page: Page, text: RegExp): Locator {
  return visibleDialog(page).filter({ hasText: text })
}

async function openMore(page: Page, code: string, item: RegExp) {
  await tableRow(page, code).getByTestId('integration-more').click()
  await page.locator('.ant-dropdown:visible .ant-dropdown-menu-item').filter({ hasText: item }).click()
}

/** 读一次性凭据弹窗里的完整凭据,并确认关闭。 */
async function takeSecret(page: Page): Promise<string> {
  const secret = page.getByTestId('integration-secret')
  await expect(secret).toBeVisible()
  const key = await secret.inputValue()
  expect(key).toMatch(/^tna_[0-9a-f]{16}\.[A-Za-z0-9_-]{43}$/)
  await page.getByRole('button', { name: /我已妥善保存|I have saved it/ }).click()
  await expect(page.getByTestId('integration-secret')).toHaveCount(0)
  return key
}

/** 凭据原文不得出现在任何前端存储里。 */
async function expectNotStored(page: Page, key: string) {
  const secret = key.slice(key.indexOf('.') + 1)
  const dump = await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }))
  expect(dump).not.toContain(secret)
}

/** 点关闭钮收起抽屉并确认已隐藏(Escape 依赖焦点位置,整套连跑时不可靠)。 */
async function closeDrawer(drawer: Locator) {
  await drawer.locator('.ant-drawer-close').first().click()
  await expect(drawer).toBeHidden({ timeout: 10_000 })
}

async function fillAppForm(page: Page, code: string, name: string) {
  const dialog = dialogWith(page, /新增接入应用|Add integration app/)
  await expect(dialog).toBeVisible()
  await formItemByLabel(dialog, /应用编码|App code/).locator('input').fill(code)
  await formItemByLabel(dialog, /应用名称|App name/).locator('input').fill(name)
  await dialog.getByRole('button', { name: /^(保\s*存|Save)$/i }).click()
  return dialog
}

test.describe('第三方接入 · 接入应用管理(React)', () => {
  test.describe.configure({ mode: 'serial' })

  test('创建 → 发放 → 授权 → 调用 → 记录 → 轮换 → 停用 全流程', async ({ page, request }) => {
    test.setTimeout(60_000)   // 九个步骤串成一条流程,实测约 30 秒,默认 30 秒预算会卡在边上
    const code = `e2e-react-${Date.now().toString(36)}`
    await login(page)
    await enterApp(page, SYSTEM_APP)
    await gotoApps(page)

    // ① 新增应用
    await page.getByRole('button', { name: /^(新\s*增|Add)$/i }).click()
    await fillAppForm(page, code, 'E2E 对接方')
    await expectMessage(page, /接入应用已保存|Integration app saved/)
    await expect(tableRow(page, code)).toHaveCount(1)

    // ② 重复编码:接口失败提示本地化文案,表单不关
    await page.getByRole('button', { name: /^(新\s*增|Add)$/i }).click()
    const dup = await fillAppForm(page, code, '重复')
    await expectMessage(page, /接入应用编码已存在|already exists/)
    await dup.getByRole('button', { name: /^(取\s*消|Cancel)$/i }).click()
    await expect(dup).toBeHidden()

    // ③ 发放凭据:空状态 → 一次性展示,不落前端存储
    await tableRow(page, code).getByTestId('integration-credentials').click()
    const credDrawer = dialogWith(page, /凭据 ·|Credentials ·/)
    await expect(credDrawer).toBeVisible()
    await expect(credDrawer.getByText(/还没有凭据|No credentials yet/)).toBeVisible()
    await page.getByTestId('integration-issue').click()
    await dialogWith(page, /发放接入凭据|Issue integration credential/).getByRole('button', { name: /^(发放凭据|Issue credential)$/ }).click()
    const key1 = await takeSecret(page)
    await expectNotStored(page, key1)
    await closeDrawer(credDrawer)

    // ④ 未授权:默认拒绝
    const denied = await callWhoami(request, key1)
    expect(denied.status).toBe(403)
    expect(denied.body.code).toBe(49002)

    // ⑤ 授权连通性检查端点
    await openMore(page, code, /接口与数据权限|API & data access/)
    const grantDrawer = dialogWith(page, /接口与数据权限 ·|API & data access ·/)
    await expect(grantDrawer).toBeVisible()
    await grantDrawer.locator('.ant-table-row').filter({ hasText: 'api/open/v1/whoami' }).locator('.ant-checkbox-input').check()
    await page.getByTestId('integration-grant-save').click()
    await expectMessage(page, /接口与数据权限已保存|API and data access saved/)
    await expect(grantDrawer).toBeHidden()

    // ⑥ 调用成功
    const ok = await callWhoami(request, key1)
    expect(ok.status).toBe(200)
    expect(ok.body.data?.appCode).toBe(code)

    // ⑦ 调用记录:从「更多」深链进入,刷新后筛选仍在
    await openMore(page, code, /开放接口调用记录|Open API call logs/i)
    await expect(page).toHaveURL(/\/integration\/inbound-log\?appId=/)
    await expect(page.locator('.ant-table-row').filter({ hasText: /拒绝访问|Forbidden/ })).toHaveCount(1, { timeout: 10_000 })
    await expect(page.locator('.ant-table-row').filter({ hasText: /成功|Succeeded/ })).toHaveCount(1)
    await page.reload()
    await expect(page.locator('.ant-tag').filter({ hasText: code })).toBeVisible({ timeout: 10_000 })
    await expect(page.locator('.ant-table-row').filter({ hasText: 'GET:/api/open/v1/whoami' })).toHaveCount(2, { timeout: 10_000 })

    // ⑧ 轮换:并存窗口 0 → 旧凭据立即失效、新凭据可用
    await gotoApps(page)
    await tableRow(page, code).getByTestId('integration-credentials').click()
    const credDrawer2 = dialogWith(page, /凭据 ·|Credentials ·/)
    await expect(credDrawer2).toBeVisible()
    await credDrawer2.getByRole('button', { name: /^(轮\s*换|Rotate)$/i }).click()
    const rotateDialog = dialogWith(page, /轮换接入凭据|Rotate integration credential/)
    const overlap = rotateDialog.getByTestId('integration-overlap')
    await overlap.fill('0')
    await overlap.blur()
    await rotateDialog.getByRole('button', { name: /^(轮\s*换|Rotate)$/i }).click()
    const key2 = await takeSecret(page)
    expect(key2).not.toBe(key1)
    await expectNotStored(page, key2)
    await expect(credDrawer2.locator('.ant-table-row').filter({ hasText: /已过期|Expired/ })).toHaveCount(1)
    await closeDrawer(credDrawer2)
    expect((await callWhoami(request, key1)).status).toBe(401)
    expect((await callWhoami(request, key2)).status).toBe(200)

    // ⑨ 停用:确认后全部凭据立即失效
    await tableRow(page, code).locator('.ant-switch').click()
    await confirmModal(page)
    await expect(tableRow(page, code).locator('.ant-switch-checked')).toHaveCount(0, { timeout: 10_000 })
    const afterDisable = await callWhoami(request, key2)
    expect(afterDisable.status).toBe(401)
    expect(afterDisable.body.code).toBe(49001)
  })

  test('只有查看权限的用户看不到管理按钮', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    const stamp = Date.now().toString(36)
    // 自备一条应用数据,不依赖上一条用例的产物(可单独运行)
    const appRes = await request.post(`${apiBase()}/api/v1/integration/app`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { code: `e2e-view-${stamp}`, name: '只读可见应用' },
    })
    expect(((await appRes.json()) as { code: number }).code).toBe(0)
    const viewer = await apiCreateViewOnlyUser(request, token, {
      prefix: 'itg',
      name: '接入只读',
      menuIds: [ROOT_MENU_ID, APP_MENU_ID, APP_PAGE_BUTTON_ID],
    })

    await login(page, viewer.account, viewer.password)
    // 登录后应用壳还在进入唯一应用、装配动态路由;此时硬刷新会打断它。等侧栏出现后按真实用户路径点菜单进入。
    const menuItem = page.getByRole('menuitem', { name: /^接入应用$|^Integration Apps$/ })
    await expect(menuItem).toBeVisible({ timeout: 15_000 })
    await menuItem.click()
    await expect(page).toHaveURL(/\/integration\/app/)
    await expect(tableRow(page, `e2e-view-${stamp}`)).toBeVisible({ timeout: 10_000 })   // 先等数据渲染,再断言按钮不存在
    await expect(page.getByRole('button', { name: /^(新\s*增|Add)$/i })).toHaveCount(0)
    await expect(page.getByRole('button', { name: /下载接口文档（JSON）|Download API docs \(JSON\)/ })).toHaveCount(0)
    await expect(page.getByTestId('integration-credentials')).toHaveCount(0)
    await expect(page.getByTestId('integration-more')).toHaveCount(0)
    await expect(tableRow(page, `e2e-view-${stamp}`).locator('.ant-switch-disabled')).toBeVisible()
  })

  test('已撤销凭据可以从列表删除', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    const code = `e2e-react-delete-credential-${Date.now().toString(36)}`
    const appId = await createApp(request, token, code)
    await issueCredential(request, token, appId)

    await login(page)
    await enterApp(page, SYSTEM_APP)
    await gotoApps(page)
    await tableRow(page, code).getByTestId('integration-credentials').click()
    const drawer = dialogWith(page, /凭据 ·|Credentials ·/)
    const row = drawer.locator('.ant-table-row').first()
    await expect(row.getByRole('button', { name: /^(删\s*除|Delete)$/i })).toHaveCount(0)

    await row.getByRole('button', { name: /^(撤\s*销|Revoke)$/i }).click()
    await page.locator('.ant-popover:visible').getByRole('button', { name: /^(确\s*定|OK)$/i }).click()
    await expectMessage(page, /凭据已撤销|Credential revoked/)
    await expect(row).toContainText(/已撤销|Revoked/)

    await row.getByRole('button', { name: /^(删\s*除|Delete)$/i }).click()
    await page.locator('.ant-popover:visible').getByRole('button', { name: /^(确\s*定|OK)$/i }).click()
    await expectMessage(page, /凭据已删除|Credential deleted/)
    await expect(drawer.getByText(/还没有凭据|No credentials yet/)).toBeVisible()
  })

  test('普通管理员有路由权限时仍不能扩大应用能力', async ({ page, request }) => {
    test.setTimeout(60_000)
    const token = await apiAdminToken(request)
    const code = `e2e-react-operator-${Date.now().toString(36)}`
    const appRes = await request.post(`${apiBase()}/api/v1/integration/app`, {
      headers: bearer(token),
      data: { code, name: '受限运维应用', enabled: true, ownerOrgId: 1 },
    })
    const appId = ((await appRes.json()) as { data: number | string }).data
    await issueCredential(request, token, appId)
    const operator = await apiCreateViewOnlyUser(request, token, {
      prefix: 'itg-op',
      name: '接入运维',
      menuIds: [ROOT_MENU_ID, APP_MENU_ID, ...APP_MANAGEMENT_BUTTON_IDS],
    })

    await login(page, operator.account, operator.password)
    await gotoApps(page)

    await page.getByRole('button', { name: /^(新\s*增|Add)$/i }).click()
    const add = dialogWith(page, /新增接入应用|Add integration app/)
    await expect(add.getByRole('combobox', { name: /归属机构|Owner organization/ })).toBeDisabled()
    await expect(add.getByRole('switch')).toHaveCount(0)
    await formItemByLabel(add, /应用编码|App code/).locator('input').fill(`operator-add-${Date.now().toString(36)}`)
    await formItemByLabel(add, /应用名称|App name/).locator('input').fill('运维新建应用')
    await add.getByRole('button', { name: /^(保\s*存|Save)$/i }).click()
    await expect(add).toBeHidden({ timeout: 15_000 })
    const operatorApp = tableRow(page, '运维新建应用')
    await expect(operatorApp).toBeVisible()
    await expect(operatorApp.getByRole('switch')).not.toBeChecked()
    await expect(operatorApp.getByRole('switch')).toBeDisabled()

    await openMore(page, code, /^编辑$|^Edit$/)
    const edit = dialogWith(page, /编辑接入应用|Edit integration app/)
    await expect(edit.getByRole('combobox', { name: /归属机构|Owner organization/ })).toBeDisabled()
    await formItemByLabel(edit, /应用名称|App name/).locator('input').fill('受限运维改名')
    await edit.getByRole('button', { name: /^(保\s*存|Save)$/i }).click()
    await expectMessage(page, /接入应用已保存|Integration app saved/)
    const detail = await request.get(`${apiBase()}/api/v1/integration/app/${appId}`, { headers: bearer(token) })
    expect(((await detail.json()) as { data: { ownerOrgId: number } }).data.ownerOrgId).toBe(1)

    await tableRow(page, code).getByTestId('integration-credentials').click()
    const credentials = dialogWith(page, /凭据 ·|Credentials ·/)
    await expect(credentials.getByTestId('integration-issue')).toHaveCount(0)
    await expect(credentials.getByRole('button', { name: /^(轮\s*换|Rotate)$/i })).toHaveCount(0)
    await expect(credentials.getByRole('button', { name: /调整到期|Expiry/ })).toHaveCount(0)
    await expect(credentials.getByRole('button', { name: /^(撤\s*销|Revoke)$/i })).toBeVisible()
    await closeDrawer(credentials)

    await openMore(page, code, /接口与数据权限|API & data access/)
    const grants = dialogWith(page, /接口与数据权限 ·|API & data access ·/)
    await expect(grants.getByTestId('integration-grant-save')).toHaveCount(0)
    await expect(grants.locator('.ant-checkbox-disabled').first()).toBeVisible()
    await closeDrawer(grants)

    await expect(tableRow(page, code).locator('.ant-switch-disabled')).toHaveCount(0)
    await tableRow(page, code).locator('.ant-switch').click()
    await confirmModal(page)
    await expect(tableRow(page, code).locator('.ant-switch-checked')).toHaveCount(0)
    await expect(tableRow(page, code).getByRole('switch')).toBeDisabled()
  })

  test('授权抽屉加载失败:不能保存、已有授权不被清空,重试后恢复', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    const code = `e2e-react-grant-${Date.now().toString(36)}`
    const appId = await createApp(request, token, code)
    const whoami = await whoamiPermission(request, token)
    await setGrants(request, token, appId, [whoami])

    await login(page)
    await enterApp(page, SYSTEM_APP)
    await gotoApps(page)
    const catalog = '**/api/v1/integration/catalog/endpoints'
    await page.route(catalog, (route) => route.fulfill({ status: 500, json: { code: 50000, message: 'e2e: catalog unavailable' } }))
    await openMore(page, code, /接口与数据权限|API & data access/)
    const grantDrawer = dialogWith(page, /接口与数据权限 ·|API & data access ·/)
    await expect(grantDrawer.getByText(/接口与数据权限加载失败|Failed to load API and data access/)).toBeVisible()
    // 没拿到本应用的完整基线就保存,会把授权写成空集:保存钮必须不可用
    await expect(page.getByTestId('integration-grant-save')).toBeDisabled()

    await page.unroute(catalog)
    await grantDrawer.getByRole('button', { name: /^(重\s*试|Retry)$/i }).click()
    await expect(grantDrawer.locator('.ant-table-row').filter({ hasText: 'api/open/v1/whoami' }).locator('.ant-checkbox-input')).toBeChecked()
    await expect(page.getByTestId('integration-grant-save')).toBeEnabled()
    await closeDrawer(grantDrawer)
    expect(await readGrants(request, token, appId)).toEqual([whoami])
  })

  test('轮换时并存窗口留空:按系统默认窗口,旧凭据不立即失效', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    const code = `e2e-react-rotate-${Date.now().toString(36)}`
    const appId = await createApp(request, token, code)
    await setGrants(request, token, appId, [await whoamiPermission(request, token)])
    const key1 = await issueCredential(request, token, appId)
    expect((await callWhoami(request, key1)).status).toBe(200)

    await login(page)
    await enterApp(page, SYSTEM_APP)
    await gotoApps(page)
    await tableRow(page, code).getByTestId('integration-credentials').click()
    const credDrawer = dialogWith(page, /凭据 ·|Credentials ·/)
    await expect(credDrawer).toBeVisible()
    await credDrawer.getByRole('button', { name: /^(轮\s*换|Rotate)$/i }).click()
    const rotateDialog = dialogWith(page, /轮换接入凭据|Rotate integration credential/)
    const overlap = rotateDialog.getByTestId('integration-overlap')
    await overlap.fill('')
    await overlap.blur()
    await rotateDialog.getByRole('button', { name: /^(轮\s*换|Rotate)$/i }).click()
    const key2 = await takeSecret(page)
    await closeDrawer(credDrawer)
    // 留空 = 服务端默认并存窗口(e2e 宿主不配 Credentials,即默认 24 小时):旧凭据此刻仍可用
    expect((await callWhoami(request, key1)).status).toBe(200)
    expect((await callWhoami(request, key2)).status).toBe(200)
  })
})
