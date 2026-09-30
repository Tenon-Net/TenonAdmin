import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { login, enterApp, expectAppMessage, SYSTEM_APP } from './helpers'
import { apiAdminToken, apiCreateViewOnlyUser } from './api'

/**
 * 第三方接入(Vue):真实后端(MinimalHost 启用接入模块)下的接入应用管理闭环。
 * 创建应用 → 发放凭据(一次性展示、不落前端存储)→ 未授权调用被拒 → 授权 → 调用成功 → 查看调用记录(深链 + 刷新)
 * → 轮换(旧凭据按窗口失效)→ 停用后拒绝;以及接口失败提示与无权限用户的按钮隐藏。
 * 另锁两处回归:授权抽屉加载失败时不能保存(不会把已有授权清空);轮换时并存窗口留空即按系统默认,旧凭据不立即失效。
 * 开放端点用模块内置的连通性检查 `GET /api/open/v1/whoami`(默认拒绝,须授予)。
 */

const apiBase = () => process.env.TENON_E2E_API_BASE ?? 'http://127.0.0.1:5101'
const WHOAMI = '/api/open/v1/whoami'
const APP_MENU_ID = 50001
const APP_PAGE_BUTTON_ID = 50002
const ROOT_MENU_ID = 50000
const APP_MANAGEMENT_BUTTON_IDS = Array.from({ length: 20 }, (_, i) => APP_PAGE_BUTTON_ID + i)

/** 按标题取表单弹层(弹窗或抽屉,随全局表单形态),不依赖遮罩层在 DOM 里的先后。 */
function formContainer(page: Page, title: RegExp) {
  return page.locator('.n-modal, .n-drawer').filter({ hasText: title })
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

async function callWhoami(request: APIRequestContext, key: string) {
  const res = await request.get(`${apiBase()}${WHOAMI}`, { headers: { 'X-Api-Key': key } })
  return { status: res.status(), body: (await res.json()) as { code: number; data?: { appCode?: string } } }
}

async function gotoApps(page: Page) {
  await page.goto('/integration/app')
  await expect(page.locator('.n-data-table')).toBeVisible({ timeout: 10_000 })
}

function appRow(page: Page, code: string) {
  return page.locator('.n-data-table-tr').filter({ hasText: code })
}

async function openMore(page: Page, code: string, item: RegExp) {
  await appRow(page, code).getByTestId('integration-more').click()
  await page.locator('.n-dropdown-option').filter({ hasText: item }).click()
}

/** 读一次性凭据弹窗里的完整凭据,并确认关闭。 */
async function takeSecret(page: Page): Promise<string> {
  const input = page.getByTestId('integration-secret').locator('input')
  await expect(input).toBeVisible()
  const key = await input.inputValue()
  expect(key).toMatch(/^tna_[0-9a-f]{16}\.[A-Za-z0-9_-]{43}$/)
  await page.getByRole('button', { name: /我已妥善保存|I have saved it/ }).click()
  await expect(page.getByTestId('integration-secret')).toHaveCount(0)
  return key
}

/** 点关闭钮收起抽屉并确认已隐藏(Escape 依赖焦点位置,整套连跑时不可靠)。 */
async function closeDrawer(drawer: Locator) {
  await drawer.locator('.n-drawer-header__close, .n-base-close').first().click()
  await expect(drawer).toBeHidden({ timeout: 10_000 })
}

/** 凭据原文不得出现在任何前端存储里。 */
async function expectNotStored(page: Page, key: string) {
  const secret = key.slice(key.indexOf('.') + 1)
  const dump = await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }))
  expect(dump).not.toContain(secret)
}

async function fillAppForm(page: Page, code: string, name: string) {
  const form = formContainer(page, /新增接入应用|Add integration app/)
  await form.getByPlaceholder(/2–64|letters/).fill(code)
  await form.locator('.n-form-item').filter({ hasText: /应用名称|App name/ }).locator('input').fill(name)
  await form.getByRole('button', { name: /^保存$|^Save$/ }).click()
  return form
}

test.describe('第三方接入 · 接入应用管理(Vue)', () => {
  test.describe.configure({ mode: 'serial' })

  test('创建 → 发放 → 授权 → 调用 → 记录 → 轮换 → 停用 全流程', async ({ page, request }) => {
    test.setTimeout(60_000)   // 九步完整流程可能超过默认 30 秒预算,与 React 同类用例保持一致
    const code = `e2e-vue-${Date.now().toString(36)}`
    await login(page)
    await enterApp(page, SYSTEM_APP)
    await gotoApps(page)

    // ① 新增应用
    await page.getByRole('button', { name: /^新增$|^Add$/ }).click()
    await fillAppForm(page, code, 'E2E 对接方')
    await expectAppMessage(page, /接入应用已保存|Integration app saved/)
    await expect(appRow(page, code)).toHaveCount(1)

    // ② 重复编码:接口失败提示本地化文案
    await page.getByRole('button', { name: /^新增$|^Add$/ }).click()
    const dup = await fillAppForm(page, code, '重复')
    await expectAppMessage(page, /接入应用编码已存在|already exists/)
    await dup.getByRole('button', { name: /^取消$|^Cancel$/ }).click()

    // ③ 发放凭据:一次性展示,不落前端存储
    await appRow(page, code).getByTestId('integration-credentials').click()
    const credentialDrawer = page.locator('.n-drawer').filter({ hasText: /凭据 ·|Credentials ·/ })
    await expect(credentialDrawer).toBeVisible()
    await expect(credentialDrawer.getByText(/还没有凭据|No credentials yet/)).toBeVisible()   // 空状态
    await page.getByTestId('integration-issue').click()
    await formContainer(page, /发放接入凭据|Issue integration credential/).getByRole('button', { name: /^发放凭据$|^Issue credential$/ }).click()
    const key1 = await takeSecret(page)
    await expectNotStored(page, key1)
    await closeDrawer(credentialDrawer)

    // ④ 未授权:默认拒绝
    const denied = await callWhoami(request, key1)
    expect(denied.status).toBe(403)
    expect(denied.body.code).toBe(49002)

    // ⑤ 授权连通性检查端点
    await openMore(page, code, /接口与数据权限|API & data access/)
    const grantDrawer = page.locator('.n-drawer').filter({ hasText: /接口与数据权限 ·|API & data access ·/ })
    await expect(grantDrawer).toBeVisible()
    await grantDrawer.locator('.n-data-table-tr').filter({ hasText: 'api/open/v1/whoami' }).locator('.n-checkbox').check()
    await page.getByTestId('integration-grant-save').click()
    await expectAppMessage(page, /接口与数据权限已保存|API and data access saved/)

    // ⑥ 调用成功
    const ok = await callWhoami(request, key1)
    expect(ok.status).toBe(200)
    expect(ok.body.data?.appCode).toBe(code)

    // ⑦ 调用记录:从「更多」深链进入,刷新后筛选仍在
    await openMore(page, code, /开放接口调用记录|Open API call logs/i)
    await expect(page).toHaveURL(/\/integration\/inbound-log\?appId=/)
    const logTable = page.locator('.n-data-table')
    await expect(logTable.locator('.n-data-table-tr').filter({ hasText: /拒绝访问|Forbidden/ })).toHaveCount(1)
    await expect(logTable.locator('.n-data-table-tr').filter({ hasText: /成功|Succeeded/ })).toHaveCount(1)
    await page.reload()
    await expect(page.locator('.n-tag').filter({ hasText: code })).toBeVisible({ timeout: 10_000 })
    await expect(page.locator('.n-data-table .n-data-table-tr').filter({ hasText: 'GET:/api/open/v1/whoami' })).toHaveCount(2)

    // ⑧ 轮换:并存窗口 0 → 旧凭据立即失效、新凭据可用
    await gotoApps(page)
    await appRow(page, code).getByTestId('integration-credentials').click()
    const credDrawer = page.locator('.n-drawer').filter({ hasText: /凭据 ·|Credentials ·/ })
    await credDrawer.getByRole('button', { name: /^轮换$|^Rotate$/ }).click()
    const rotateForm = formContainer(page, /轮换接入凭据|Rotate integration credential/)
    const overlap = rotateForm.locator('.n-form-item').filter({ hasText: /并存窗口|Overlap/ }).locator('input')
    await overlap.fill('0')
    await overlap.blur()
    await rotateForm.getByRole('button', { name: /^轮换$|^Rotate$/ }).click()
    const key2 = await takeSecret(page)
    expect(key2).not.toBe(key1)
    await expectNotStored(page, key2)
    await expect(credDrawer.locator('.n-data-table-tr').filter({ hasText: /已过期|Expired/ })).toHaveCount(1)
    await closeDrawer(credDrawer)
    expect((await callWhoami(request, key1)).status).toBe(401)
    expect((await callWhoami(request, key2)).status).toBe(200)

    // ⑨ 停用:确认后全部凭据立即失效
    await appRow(page, code).locator('.n-switch').click()
    await page.locator('.n-dialog').getByRole('button', { name: /^确定$|^Confirm$|^OK$/ }).click()
    await expect(appRow(page, code).locator('.n-switch--active')).toHaveCount(0)
    const afterDisable = await callWhoami(request, key2)
    expect(afterDisable.status).toBe(401)
    expect(afterDisable.body.code).toBe(49001)
  })

  test('已撤销凭据可删除,有效凭据不可删除', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    const code = `e2e-vue-cred-delete-${Date.now().toString(36)}`
    await createApp(request, token, code)

    await login(page)
    await enterApp(page, SYSTEM_APP)
    await gotoApps(page)
    await appRow(page, code).getByTestId('integration-credentials').click()
    const drawer = page.locator('.n-drawer').filter({ hasText: /凭据 ·|Credentials ·/ })
    await drawer.getByTestId('integration-issue').click()
    await formContainer(page, /发放接入凭据|Issue integration credential/).getByRole('button', { name: /^发放凭据$|^Issue credential$/ }).click()
    const key = await takeSecret(page)
    const credentialRow = drawer.locator('.n-data-table-tr').filter({ hasText: key.split('.')[0] })

    await expect(credentialRow.getByRole('button', { name: /^删除$|^Delete$/ })).toHaveCount(0)
    await credentialRow.getByRole('button', { name: /^撤销$|^Revoke$/ }).click()
    await page.getByRole('button', { name: /^确认$|^Confirm$/ }).click()
    await expectAppMessage(page, /凭据已撤销|Credential revoked/)

    const revokedRow = drawer.locator('.n-data-table-tr').filter({ hasText: key.split('.')[0] })
    await revokedRow.getByRole('button', { name: /^删除$|^Delete$/ }).click()
    await page.getByRole('button', { name: /^确认$|^Confirm$/ }).click()
    await expectAppMessage(page, /凭据已删除|Credential deleted/)
    await expect(revokedRow).toHaveCount(0)
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
    const menuItem = page.locator('.sidenav .n-menu-item-content').filter({ hasText: /^接入应用$|^Integration Apps$/ })
    await expect(menuItem).toBeVisible({ timeout: 15_000 })
    await menuItem.click()
    await expect(page).toHaveURL(/\/integration\/app/)
    await expect(appRow(page, `e2e-view-${stamp}`)).toBeVisible({ timeout: 10_000 })   // 先等数据渲染,再断言按钮不存在
    await expect(page.getByRole('button', { name: /^新增$|^Add$/ })).toBeHidden()
    await expect(page.getByRole('button', { name: /下载接口文档（JSON）|Download API docs \(JSON\)/ })).toBeHidden()
    await expect(page.getByTestId('integration-credentials')).toHaveCount(0)
    await expect(page.getByTestId('integration-more')).toHaveCount(0)
    await expect(appRow(page, `e2e-view-${stamp}`).locator('.n-switch--disabled')).toBeVisible()
  })

  test('普通管理员有路由权限时仍不能扩大应用能力', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    const code = `e2e-vue-operator-${Date.now().toString(36)}`
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

    await page.getByRole('button', { name: /^新增$|^Add$/ }).click()
    const add = formContainer(page, /新增接入应用|Add integration app/)
    await expect(add.locator('.n-form-item').filter({ hasText: /归属机构|Owner organization/ }).locator('.n-base-selection--disabled')).toBeVisible()
    await expect(add.locator('.n-switch')).toHaveCount(0)
    await add.locator('.n-form-item').filter({ hasText: /应用编码|App code/ }).locator('input').fill(`operator-add-${Date.now().toString(36)}`)
    await add.locator('.n-form-item').filter({ hasText: /应用名称|App name/ }).locator('input').fill('运维新建应用')
    await add.getByRole('button', { name: /^保存$|^Save$/ }).click()
    await expect(add).toBeHidden({ timeout: 15_000 })
    const operatorApp = page.locator('.n-data-table-tr').filter({ hasText: '运维新建应用' })
    await expect(operatorApp).toBeVisible()
    await expect(operatorApp.locator('.n-switch--active')).toHaveCount(0)
    await expect(operatorApp.locator('.n-switch--disabled')).toBeVisible()

    await openMore(page, code, /^编辑$|^Edit$/)
    const edit = formContainer(page, /编辑接入应用|Edit integration app/)
    await expect(edit.locator('.n-form-item').filter({ hasText: /归属机构|Owner organization/ }).locator('.n-base-selection--disabled')).toBeVisible()
    await edit.locator('.n-form-item').filter({ hasText: /应用名称|App name/ }).locator('input').fill('受限运维改名')
    await edit.getByRole('button', { name: /^保存$|^Save$/ }).click()
    await expect(edit).toBeHidden({ timeout: 15_000 })
    await expect(appRow(page, code)).toContainText('受限运维改名')
    const detail = await request.get(`${apiBase()}/api/v1/integration/app/${appId}`, { headers: bearer(token) })
    expect(((await detail.json()) as { data: { ownerOrgId: number } }).data.ownerOrgId).toBe(1)

    await appRow(page, code).getByTestId('integration-credentials').click()
    const credentials = page.locator('.n-drawer').filter({ hasText: /凭据 ·|Credentials ·/ })
    await expect(credentials.getByTestId('integration-issue')).toHaveCount(0)
    await expect(credentials.getByRole('button', { name: /^轮换$|^Rotate$/ })).toHaveCount(0)
    await expect(credentials.getByRole('button', { name: /调整到期|Expiry/ })).toHaveCount(0)
    await expect(credentials.getByRole('button', { name: /^撤销$|^Revoke$/ })).toBeVisible()
    await closeDrawer(credentials)

    await openMore(page, code, /接口与数据权限|API & data access/)
    const grants = page.locator('.n-drawer').filter({ hasText: /接口与数据权限 ·|API & data access ·/ })
    await expect(grants.getByTestId('integration-grant-save')).toHaveCount(0)
    await expect(grants.locator('.n-data-table-tbody .n-checkbox').first()).toHaveClass(/n-checkbox--disabled/)
    await closeDrawer(grants)

    await expect(appRow(page, code).locator('.n-switch--disabled')).toHaveCount(0)
    await appRow(page, code).locator('.n-switch').click()
    await page.locator('.n-dialog').getByRole('button', { name: /^确定$|^Confirm$|^OK$/ }).click()
    await expect(appRow(page, code).locator('.n-switch--active')).toHaveCount(0)
    await expect(appRow(page, code).locator('.n-switch--disabled')).toBeVisible()
  })

  test('授权抽屉加载失败:不能保存、已有授权不被清空,重试后恢复', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    const code = `e2e-vue-grant-${Date.now().toString(36)}`
    const appId = await createApp(request, token, code)
    const whoami = await whoamiPermission(request, token)
    await setGrants(request, token, appId, [whoami])

    await login(page)
    await enterApp(page, SYSTEM_APP)
    await gotoApps(page)
    const catalog = '**/api/v1/integration/catalog/endpoints'
    await page.route(catalog, (route) => route.fulfill({ status: 500, json: { code: 50000, message: 'e2e: catalog unavailable' } }))
    await openMore(page, code, /接口与数据权限|API & data access/)
    const grantDrawer = page.locator('.n-drawer').filter({ hasText: /接口与数据权限 ·|API & data access ·/ })
    await expect(grantDrawer.getByText(/接口与数据权限加载失败|Failed to load API and data access/)).toBeVisible()
    // 没拿到本应用的完整基线就保存,会把授权写成空集:保存钮必须不可用
    await expect(page.getByTestId('integration-grant-save')).toBeDisabled()

    await page.unroute(catalog)
    await grantDrawer.getByRole('button', { name: /^重试$|^Retry$/ }).click()
    await expect(grantDrawer.locator('.n-data-table-tr').filter({ hasText: 'api/open/v1/whoami' }).locator('.n-checkbox')).toBeChecked()
    await expect(page.getByTestId('integration-grant-save')).toBeEnabled()
    await closeDrawer(grantDrawer)
    expect(await readGrants(request, token, appId)).toEqual([whoami])
  })

  test('轮换时并存窗口留空:按系统默认窗口,旧凭据不立即失效', async ({ page, request }) => {
    const token = await apiAdminToken(request)
    const code = `e2e-vue-rotate-${Date.now().toString(36)}`
    const appId = await createApp(request, token, code)
    await setGrants(request, token, appId, [await whoamiPermission(request, token)])
    const key1 = await issueCredential(request, token, appId)
    expect((await callWhoami(request, key1)).status).toBe(200)

    await login(page)
    await enterApp(page, SYSTEM_APP)
    await gotoApps(page)
    await appRow(page, code).getByTestId('integration-credentials').click()
    const credDrawer = page.locator('.n-drawer').filter({ hasText: /凭据 ·|Credentials ·/ })
    await credDrawer.getByRole('button', { name: /^轮换$|^Rotate$/ }).click()
    const rotateForm = formContainer(page, /轮换接入凭据|Rotate integration credential/)
    const overlap = rotateForm.locator('.n-form-item').filter({ hasText: /并存窗口|Overlap/ }).locator('input')
    await overlap.fill('')
    await overlap.blur()
    await rotateForm.getByRole('button', { name: /^轮换$|^Rotate$/ }).click()
    const key2 = await takeSecret(page)
    await closeDrawer(credDrawer)
    // 留空 = 服务端默认并存窗口(e2e 宿主不配 Credentials,即默认 24 小时):旧凭据此刻仍可用
    expect((await callWhoami(request, key1)).status).toBe(200)
    expect((await callWhoami(request, key2)).status).toBe(200)
  })
})
