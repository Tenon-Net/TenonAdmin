import { test, expect, type Page } from '@playwright/test'
import { login, openAppPicker, enterApp, sidebarLeaves } from './helpers'

/**
 * 多应用门户回归:切换应用后首页要跟着变,且切回来后菜单点得开(不能白屏)。
 * 白屏 bug 活在渲染层(动态路由重建 + keep-alive + out-in Transition),只有真浏览器抓得到,单测抓不到。
 *
 * 前置:后端在 :5100 跑,库里至少有 2 个应用(内置「系统」+ 你自己建的业务应用),超管账号可登录。
 */

/** 从任意位置切到指定应用(选择页上直接点;在应用里则先经顶栏九宫格回选择页)。 */
async function switchTo(page: Page, title: string) {
  await enterApp(page, new RegExp(`^${title}$`))
}

/** 内容区渲染出东西了没——白屏 bug 的判据就是这个 .page 下空空如也。 */
async function expectContentRendered(page: Page, ctx: string) {
  const content = page.locator('.page > *')
  await expect(content.first(), `内容区空白(${ctx})`).toBeVisible({ timeout: 10_000 })
}

test('切换应用:首页跟着应用变,切回来后菜单还点得开', async ({ page }) => {
  // 登录 + 3 次切应用 + 逐个点开全部叶子,冷 vite(首次按需 transform)实测 33s,顶穿全局 30s。
  // 这条比别的用例长一个量级,单独给余量,别为它把全局超时放宽。
  test.setTimeout(60_000)

  await login(page)

  // 门户里至少要有两个应用,否则这个测试没有意义(而不是假装通过)
  const cards = await openAppPicker(page)
  const titles = (await cards.locator('.name').allInnerTexts()).map((s) => s.trim()).filter(Boolean)
  expect(titles.length, '库里至少要有 2 个应用才能测切换').toBeGreaterThanOrEqual(2)

  const [first, second] = titles

  // ① 进 A 应用,记下它的首页(此刻已在选择页上,直接点卡片)
  await switchTo(page, first!)
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
  const leaves = await sidebarLeaves(page)
  expect(leaves.length, '侧边菜单不该是空的').toBeGreaterThan(0)
  for (const { name, item } of leaves) {
    await item.click()
    // 先断"这一项真的成了选中项",再断内容。少了这一条,「点到的其实是别的东西」会伪装成通过:
    // 下面的 expectContentRendered 断的是**上一页**,而上一页当然是渲染好的。
    await expect(item, `点击菜单「${name}」后它没有变成选中态`).toHaveClass(
      /n-menu-item-content--selected/,
      { timeout: 10_000 },
    )
    await expectContentRendered(page, `切回 ${first} 后点击菜单「${name}」`)
  }
})

/**
 * 默认应用可改:曾经的死局是「设了默认 → 登录直进默认 → 再也回不到选择页 → 默认永远改不掉」。
 * 注意:本例会把超管的默认应用改掉(后端没有"取消默认"的接口),跑完不还原。
 */
test('默认应用:选择页能改默认,F5 也不会被弹回首页', async ({ page }) => {
  await login(page)

  const cards = await openAppPicker(page)
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
