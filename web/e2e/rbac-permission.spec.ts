import { test, expect, type Page } from '@playwright/test'

/**
 * RBAC 权限端到端测试:验证角色-菜单授权 + 角色-用户授权的完整流程。
 *
 * 前置:
 * - 后端在 :5100 运行,数据库包含种子数据(5 个 scope_* 用户/角色,密码 123456)
 * - 种子角色初始状态:未授权任何菜单(仅有用户↔角色 + 数据范围关联)
 * - 前端在 :5173 运行
 *
 * 注意:测试会修改数据库(给角色授权菜单/用户),适合在开发库上跑。
 */

const ADMIN_ACCOUNT = process.env.TENON_E2E_ACCOUNT ?? 'superAdmin'
const ADMIN_PASSWORD = process.env.TENON_E2E_PASSWORD ?? 'Aa123456'
const SEED_PASSWORD = '123456'

// ── helpers ──────────────────────────────────────────────────────

async function loginAs(page: Page, account: string, password: string) {
  await page.goto('/login')
  await page.waitForLoadState('networkidle')

  const accountInput = page.getByPlaceholder(/账号|account/i)
  await accountInput.fill(account)
  await page.getByPlaceholder(/密码|password/i).first().fill(password)

  const captchaSvg = page.locator('.lf-captcha-img svg')
  if (await captchaSvg.isVisible().catch(() => false)) {
    const code = (await captchaSvg.locator('text').allInnerTexts()).join('')
    await page.getByPlaceholder(/验证码|captcha/i).fill(code)
  }

  await page.locator('button.hero-btn').click()
  await expect(page).not.toHaveURL(/\/login/, { timeout: 15_000 })
}

async function logout(page: Page) {
  await page.evaluate(() => localStorage.clear())
  await page.goto('/login')
  await expect(page).toHaveURL(/\/login/, { timeout: 5_000 })
}

/** 如果落在模块选择页 /module,选第一个应用进去。 */
async function enterFirstModuleIfNeeded(page: Page) {
  if (page.url().includes('/module')) {
    const cards = page.locator('.card')
    if ((await cards.count()) > 0) {
      await cards.first().click()
      await expect(page).not.toHaveURL(/\/module/, { timeout: 10_000 })
    }
  }
}

/** 展开所有侧边目录,收集菜单叶子文字。 */
async function getSidebarMenuNames(page: Page): Promise<string[]> {
  await page.waitForLoadState('networkidle')
  const submenus = page.locator('.n-submenu > .n-menu-item > .n-menu-item-content')
  for (let i = 0; i < (await submenus.count()); i++) {
    await submenus.nth(i).click()
  }
  const leaves = page.locator('.n-menu-item-content:not(.n-menu-item-content--collapsed)')
  const count = await leaves.count()
  if (count === 0) return []
  return (await leaves.allInnerTexts()).map((s) => s.trim()).filter(Boolean)
}

/** 导航到角色管理页。 */
async function gotoRolePage(page: Page) {
  await page.goto('/system/role')
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.n-data-table')).toBeVisible({ timeout: 10_000 })
}

/** 在角色列表中找到指定角色行的"更多"按钮并点击。 */
async function clickRoleMoreButton(page: Page, roleName: string) {
  // NDataTable 用 div 不用 tr
  const row = page.locator('.n-data-table-tr').filter({ hasText: roleName })
  await expect(row).toBeVisible({ timeout: 5_000 })
  await row.getByText(/更多|More/i).click()
}

/** FormContainer 可能是 modal 或 drawer,返回当前可见的容器。 */
function formContainer(page: Page) {
  return page.locator('.n-drawer, .n-modal')
}

// ── tests ────────────────────────────────────────────────────────

test.describe('RBAC 权限', () => {
  test.describe.configure({ mode: 'serial' })

  test('超管给角色授权菜单 → 该用户登录后只看到被授权的菜单', async ({ page }) => {
    // ① 超管登录,进角色管理
    await loginAs(page, ADMIN_ACCOUNT, ADMIN_PASSWORD)
    await enterFirstModuleIfNeeded(page)
    await gotoRolePage(page)

    // ② 给"全部数据"角色授权菜单
    await clickRoleMoreButton(page, '全部数据')
    // NDropdown 渲染到 body 的 popover 里,等它出现
    const menuItem = page.locator('.n-dropdown-option').filter({ hasText: /授权菜单|Grant menus/ })
    await expect(menuItem).toBeVisible({ timeout: 3_000 })
    await menuItem.click()

    // 等容器打开,GrantMenuTable 渲染
    const drawer = formContainer(page)
    await expect(drawer).toBeVisible({ timeout: 5_000 })
    // GrantMenuTable 渲染需要等 API 返回菜单树
    await expect(drawer.locator('.grant-grid')).toBeVisible({ timeout: 10_000 })
    await page.waitForTimeout(500) // 等 checkbox 渲染完毕

    // 勾选"组织管理"目录(级联选中其下所有菜单+按钮)——幂等:已勾则跳过
    const orgCatalog = drawer.locator('.col-catalog').filter({ hasText: '组织管理' })
    await expect(orgCatalog).toBeVisible({ timeout: 5_000 })
    const checkbox = orgCatalog.locator('.n-checkbox')
    const isChecked = await checkbox.evaluate((el) => el.classList.contains('n-checkbox--checked'))
    if (!isChecked) await checkbox.click()

    // 保存
    await drawer.getByText(/保存|Save/i).click()
    await expect(page.locator('.n-message')).toContainText(/保存|saved/i, { timeout: 5_000 })

    // ③ 退出,以 scope_all 用户重新登录
    await logout(page)
    await loginAs(page, '全部数据', SEED_PASSWORD)
    await enterFirstModuleIfNeeded(page)

    // ④ 验证侧边菜单
    const menus = await getSidebarMenuNames(page)
    expect(menus.length, '应至少看到组织管理下的菜单').toBeGreaterThan(0)

    // 组织管理下的菜单应当可见
    const hasOrgMenu = menus.some((m) => /机构管理|用户管理|角色管理/.test(m))
    expect(hasOrgMenu, `菜单列表 [${menus.join(', ')}] 应包含组织管理下的子菜单`).toBe(true)
  })

  test('未授权菜单的用户 → 侧边菜单为空或仅工作台', async ({ page }) => {
    // scope_self 角色未被授权任何菜单
    await loginAs(page, '仅本人数据', SEED_PASSWORD)
    await enterFirstModuleIfNeeded(page)

    const menus = await getSidebarMenuNames(page)
    // 最多只有工作台(Permission="" 的顶级菜单,不受权限控制)
    expect(menus.length, `未授权用户不应看到业务菜单,实际看到: [${menus.join(', ')}]`).toBeLessThanOrEqual(1)
  })

  test('授权用户 → UserPicker 种子关联正确回显', async ({ page }) => {
    await loginAs(page, ADMIN_ACCOUNT, ADMIN_PASSWORD)
    await enterFirstModuleIfNeeded(page)
    await gotoRolePage(page)

    // 找"本机构数据"角色 → 授权用户
    await clickRoleMoreButton(page, '本机构数据')
    const item = page.locator('.n-dropdown-option').filter({ hasText: /授权用户|Grant users/ })
    await expect(item).toBeVisible({ timeout: 3_000 })
    await item.click()

    const picker = formContainer(page)
    await expect(picker).toBeVisible({ timeout: 5_000 })

    // 右面板"已选"区应有 1 个种子用户(scope_org)
    const selectedItems = picker.locator('.selected-item')
    await expect(selectedItems).toHaveCount(1, { timeout: 5_000 })
  })

  test('授权用户 → 添加用户 → 保存 → 回显 → 还原', async ({ page }) => {
    await loginAs(page, ADMIN_ACCOUNT, ADMIN_PASSWORD)
    await enterFirstModuleIfNeeded(page)
    await gotoRolePage(page)

    // ① 打开"仅本人数据"角色的 UserPicker
    await clickRoleMoreButton(page, '仅本人数据')
    const item = page.locator('.n-dropdown-option').filter({ hasText: /授权用户|Grant users/ })
    await expect(item).toBeVisible({ timeout: 3_000 })
    await item.click()

    const picker = formContainer(page)
    await expect(picker).toBeVisible({ timeout: 5_000 })
    await page.waitForTimeout(500)

    // 记录初始已选数
    const initialCount = await picker.locator('.selected-item').count()

    // ② 在中间面板找"超级管理员"行,点"添加"按钮
    const adminRow = picker.locator('.n-data-table-tr').filter({ hasText: '超级管理员' })
    await expect(adminRow).toBeVisible({ timeout: 5_000 })
    await adminRow.locator('button').filter({ hasText: /添加|Add/ }).click()

    // 右面板应多出一条
    await expect(picker.locator('.selected-item')).toHaveCount(initialCount + 1, { timeout: 3_000 })

    // ③ 保存
    const confirmBtn = picker.locator('button').filter({ hasText: /确定|Confirm/ })
    await confirmBtn.click()
    await expect(page.locator('.n-message')).toBeVisible({ timeout: 5_000 })
    await page.waitForTimeout(1000)

    // ④ 重新打开验证回显
    await clickRoleMoreButton(page, '仅本人数据')
    const item2 = page.locator('.n-dropdown-option').filter({ hasText: /授权用户|Grant users/ })
    await expect(item2).toBeVisible({ timeout: 3_000 })
    await item2.click()

    const picker2 = formContainer(page)
    await expect(picker2).toBeVisible({ timeout: 5_000 })
    await page.waitForTimeout(500)
    await expect(picker2.locator('.selected-item')).toHaveCount(initialCount + 1, { timeout: 5_000 })

    // ⑤ 清理:移除刚加的"超级管理员",保存还原
    const adminItem = picker2.locator('.selected-item').filter({ hasText: '超级管理员' })
    await adminItem.locator('button').click()
    await expect(picker2.locator('.selected-item')).toHaveCount(initialCount, { timeout: 3_000 })
    const confirmBtn2 = picker2.locator('button').filter({ hasText: /确定|Confirm/ })
    await confirmBtn2.click()
    await expect(page.locator('.n-message')).toBeVisible({ timeout: 5_000 })
  })
})
