import { test, expect, type Page } from '@playwright/test'

/**
 * 多应用门户回归:切换应用后首页要跟着变,且切回来后菜单点得开(不能白屏)。
 * 白屏 bug 活在渲染层(动态路由重建 + keep-alive + out-in Transition),只有真浏览器抓得到,单测抓不到。
 *
 * 前置:后端在 :5100 跑,库里至少有 2 个应用(内置「系统」+ 你自己建的业务应用),超管账号可登录。
 */

const ACCOUNT = process.env.TENON_E2E_ACCOUNT ?? 'superAdmin'
const PASSWORD = process.env.TENON_E2E_PASSWORD ?? 'Aa123456'

/** 登录。验证码若开着,直接从内联 SVG 的 <text> 里抠字符——SvgCaptchaProvider 自己就说了"防人不防机"。 */
async function login(page: Page) {
  await page.goto('/login')
  await page.getByPlaceholder(/账号|account/i).fill(ACCOUNT)
  await page.getByPlaceholder(/密码|password/i).first().fill(PASSWORD)

  const captchaSvg = page.locator('.lf-captcha-img svg')
  if (await captchaSvg.isVisible().catch(() => false)) {
    const code = (await captchaSvg.locator('text').allInnerTexts()).join('')
    await page.getByPlaceholder(/验证码|captcha/i).fill(code)
  }

  await page.locator('button.hero-btn').click()
  await expect(page).not.toHaveURL(/\/login/, { timeout: 15_000 })
}

/** 顶栏九宫格 → 应用选择页 /module(不再是下拉直切:应用一多下拉就难用,且「设为默认」只在选择页上)。 */
async function gotoModulePage(page: Page) {
  await page.getByRole('button', { name: /切换应用|switch app/i }).click()
  await expect(page).toHaveURL(/\/module/)
  const cards = page.locator('.card')
  await expect(cards.first()).toBeVisible()
  return cards
}

/** 在选择页上点某个应用卡片。精确匹配 .name:子串匹配下「系统」会先命中「业务系统」,切成原地不动。 */
async function pickCard(page: Page, title: string) {
  const card = page.locator('.card').filter({ has: page.locator('.name', { hasText: new RegExp(`^${title}$`) }) })
  await card.click()
  // 进应用必然离开选择页(落到该应用首页);还停在 /module 就是没进去——别让后续断言在假现场上跑
  await expect(page, `点了「${title}」但没进应用`).not.toHaveURL(/\/module/, { timeout: 10_000 })
}

/** 从应用内切到另一个应用:顶栏九宫格 → 选择页 → 点卡片。 */
async function switchTo(page: Page, title: string) {
  await gotoModulePage(page)
  await pickCard(page, title)
}

/** 内容区渲染出东西了没——白屏 bug 的判据就是这个 .page 下空空如也。 */
async function expectContentRendered(page: Page, ctx: string) {
  const content = page.locator('.page > *')
  await expect(content.first(), `内容区空白(${ctx})`).toBeVisible({ timeout: 10_000 })
}

/** 侧边菜单里所有可见叶子(展开所有目录后)。 */
async function sidebarLeafNames(page: Page): Promise<string[]> {
  // 目录项(有展开箭头)全部展开,露出叶子
  const submenus = page.locator('.n-submenu > .n-menu-item > .n-menu-item-content')
  for (let i = 0; i < (await submenus.count()); i++) {
    await submenus.nth(i).click()
  }
  const leaves = page.locator('.n-menu-item-content:not(.n-menu-item-content--collapsed)')
  await expect(leaves.first()).toBeVisible()
  return (await leaves.allInnerTexts()).map((s) => s.trim()).filter(Boolean)
}

test('切换应用:首页跟着应用变,切回来后菜单还点得开', async ({ page }) => {
  await login(page)

  // 门户里至少要有两个应用,否则这个测试没有意义(而不是假装通过)
  const cards = await gotoModulePage(page)
  const titles = (await cards.locator('.name').allInnerTexts()).map((s) => s.trim()).filter(Boolean)
  expect(titles.length, '库里至少要有 2 个应用才能测切换').toBeGreaterThanOrEqual(2)

  const [first, second] = titles

  // ① 进 A 应用,记下它的首页(此刻已在选择页上,直接点卡片)
  await pickCard(page, first!)
  const homeA = new URL(page.url()).pathname
  await expectContentRendered(page, `${first} 首页`)

  // ② 切到 B 应用:首页必须换一个(每个应用有自己的首页),内容要渲染出来
  await switchTo(page, second!)
  const homeB = new URL(page.url()).pathname
  await expectContentRendered(page, `${second} 首页`)
  expect(homeB, `切到「${second}」后首页仍是「${first}」的 ${homeA}`).not.toBe(homeA)

  // ③ 切回 A 应用 —— 这是白屏 bug 的现场
  await switchTo(page, first!)
  await expectContentRendered(page, `切回 ${first} 后的首页`)
  expect(new URL(page.url()).pathname).toBe(homeA)

  // ④ 切回来之后,逐个点侧边菜单叶子,每个都得渲染出内容(bug 表现为全部空白)
  const leaves = await sidebarLeafNames(page)
  expect(leaves.length, '侧边菜单不该是空的').toBeGreaterThan(0)
  for (const name of leaves) {
    await page.locator('.n-menu-item-content').filter({ hasText: name }).first().click()
    await expectContentRendered(page, `切回 ${first} 后点击菜单「${name}」`)
  }
})

/**
 * 默认应用可改:曾经的死局是「设了默认 → 登录直进默认 → 再也回不到选择页 → 默认永远改不掉」。
 * 注意:本例会把超管的默认应用改掉(后端没有"取消默认"的接口),跑完不还原。
 */
test('默认应用:选择页能改默认,F5 也不会被弹回首页', async ({ page }) => {
  await login(page)

  const cards = await gotoModulePage(page)
  expect(await cards.count(), '库里至少要有 2 个应用才能测改默认').toBeGreaterThanOrEqual(2)

  const isDefault = /^默认$|^Default$/
  // 非默认的卡才挂「设为默认」按钮;挑第一张来设,默认角标应当当场转移过来。
  // 先把卡名取出来另行定位:locator 是惰性的,点完之后这张卡就不再"含设为默认按钮",按原表达式会匹配不到自己。
  const settable = page.locator('.card').filter({ has: page.getByText(/设为默认|Set default/) }).first()
  const name = (await settable.locator('.name').innerText()).trim()
  await settable.getByText(/设为默认|Set default/).click()

  const target = page.locator('.card').filter({ has: page.locator('.name', { hasText: new RegExp(`^${name}$`) }) })
  await expect(target.getByText(isDefault), `设了「${name}」为默认,角标却没转过来`).toBeVisible({ timeout: 10_000 })
  await expect(page.locator('.card').getByText(isDefault), '默认应用只能有一个').toHaveCount(1)

  // F5 停在选择页:守卫从前会把 /module 弹回首页,那正是"默认改不掉"的根因
  await page.reload()
  await expect(page).toHaveURL(/\/module/)
  await expect(page.locator('.card').first()).toBeVisible()
})
