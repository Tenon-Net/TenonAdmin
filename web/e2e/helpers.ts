import { expect, type Locator, type Page } from '@playwright/test'

/**
 * 三个 spec 共用的登录与「进哪个应用」。
 *
 * 抽出来的原因不是去重,是**正确性**:登录后落在哪个应用取决于该用户的「默认应用」,
 * 而那是一份跨用例、跨整个套件存活的**全局可变状态**——`module-switch` 里那条改默认应用的用例
 * 会把它改掉,于是后面所有依赖某个应用下路由的用例(`/workbench` 只挂在「系统」下、
 * `/system/role` 同理)全部失效,且按字母序它恰好跑在最前面。
 * 每个用例自己把前置条件立起来,顺序就不再有毒。
 */

export const ADMIN_ACCOUNT = process.env.TENON_E2E_ACCOUNT ?? 'superAdmin'
export const ADMIN_PASSWORD = process.env.TENON_E2E_PASSWORD ?? 'Aa123456'

/** 内置「系统」应用的标题——`/workbench` 与 `/system/*` 都挂在它下面。 */
export const SYSTEM_APP = /^系统$|^System$/

/** 登录。验证码若开着,直接从内联 SVG 的 <text> 里抠字符——SvgCaptchaProvider 自己就说了"防人不防机"。 */
export async function login(page: Page, account = ADMIN_ACCOUNT, password = ADMIN_PASSWORD) {
  await page.goto('/login')
  await page.getByPlaceholder(/账号|account/i).fill(account)
  await page.getByPlaceholder(/密码|password/i).first().fill(password)

  const captchaSvg = page.locator('.lf-captcha-img svg')
  if (await captchaSvg.isVisible().catch(() => false)) {
    const code = (await captchaSvg.locator('text').allInnerTexts()).join('')
    await page.getByPlaceholder(/验证码|captcha/i).fill(code)
  }

  await page.locator('button.hero-btn').click()
  await expect(page).not.toHaveURL(/\/login/, { timeout: 15_000 })
}

/**
 * 打开应用选择页 `/module`,返回卡片 locator。
 * 已经在选择页上就不点了:顶栏那颗「切换应用」按钮只存在于应用壳里,而用户没设默认应用时
 * 登录直接落在选择页,壳还没渲染——照点必然超时。
 *
 * 前置:调用者须有 **2 个以上**应用。那颗按钮由 `AppHeader.vue` 的 `auth.modules.length > 1` 把关,
 * 单应用用户登录会被直接送进唯一那个应用,再调本函数就是在等一颗永远不会出现的按钮。
 */
export async function openAppPicker(page: Page) {
  if (!/\/module/.test(page.url())) {
    await page.getByRole('button', { name: /切换应用|switch app/i }).click()
  }
  await expect(page).toHaveURL(/\/module/)
  const cards = page.locator('.card')
  await expect(cards.first()).toBeVisible()
  return cards
}

/**
 * 进指定应用。`title` 由**调用方自己加锚**(`^...$`):本函数不加,而子串匹配下「系统」会先命中
 * 「业务系统」这类名字,切成原地不动。
 */
export async function enterApp(page: Page, title: RegExp) {
  await openAppPicker(page)
  const card = page.locator('.card').filter({ has: page.locator('.name', { hasText: title }) })
  await expect(card, `选择页上没有匹配 ${title} 的应用`).toHaveCount(1)
  await card.click()
  // 进应用必然离开选择页(落到该应用首页);还停在 /module 就是没进去——别让后续断言在假现场上跑
  await expect(page, `点了 ${title} 但没进应用`).not.toHaveURL(/\/module/, { timeout: 10_000 })
}

/**
 * 侧边栏叶子的选择器。目录和叶子共用 `.n-menu-item-content`,**只有目录带展开箭头**(纵向菜单下成立:
 * `Submenu` 传 `showArrow: !isHorizontal`)。把目录当叶子的后果不是多几条文字,是调用方**点到目录**——
 * 那会把子菜单收起来,同一批里后面那些项当场从 DOM 上脱离,报成"元素 detached"。
 *
 * **必须限定在 `.sidenav` 内**:上面那个「只有目录带箭头」的判据只在纵向菜单下成立,而
 * `layouts/AppHeader.vue` 里有三个 `mode="horizontal"` 的 `n-menu`——横向菜单的目录**不带箭头**,
 * 不限定范围就会被当成叶子点。今天不发作只是因为 Playwright 每个用例新开 context、布局偏好为空 →
 * 默认侧栏布局 → 那三个菜单根本不渲染;顶栏布局一旦成为默认(或用例开始持久化偏好)就会静默变坏。
 */
const SIDE = '.sidenav'
const LEAF = `${SIDE} .n-menu-item-content:not(:has(.n-menu-item-content__arrow))`

/**
 * 展开侧边栏所有目录,返回每个叶子的文字**和它的 locator**。
 *
 * 关键一:**目录默认可能已经展开**——Naive 会自动展开当前路由所在的那个目录,而用户只有一个应用时
 * 登录直接落在某个页面上(不经选择页),于是那个目录一定是展开的。此时无条件点击是把它**收起来**,
 * 叶子随之全部消失,调用方看到的是"菜单为空"。只点还没展开的:判据是 `.n-submenu-children` 是否可见。
 *
 * 关键二:**连 locator 一起还给调用方,别只给名字**。拿名字回头再找必然踩「目录与叶子同名」——
 * 种子里「文件管理」既是目录(Id 30)又是它下面的叶子(Id 78),子串匹配 + `.first()` 按文档序命中的是目录,
 * 于是那一页从来没被真正打开过,而紧跟的内容断言断的是**上一页**,属于"因为错误的理由而通过"。
 * 按下标取,这类歧义从根上不存在。
 */
export async function sidebarLeaves(page: Page): Promise<{ name: string; item: Locator }[]> {
  await page.waitForLoadState('networkidle')
  // 展开一层会露出下一层目录,重复到没有可展开的为止(种子菜单树只有 2 层,给个上限防死循环;
  // 真超过 3 层会静默漏掉更深的,当前数据到不了那儿)
  for (let pass = 0; pass < 3; pass++) {
    const subs = page.locator(`${SIDE} .n-submenu`)
    let clicked = false
    for (let i = 0; i < (await subs.count()); i++) {
      const sub = subs.nth(i)
      const children = sub.locator('.n-submenu-children').first()
      if (await children.isVisible().catch(() => false)) continue
      await sub.locator(':scope > .n-menu-item > .n-menu-item-content').click()
      // 点完立刻钉住展开态。子菜单包在 NFadeInExpandTransition 里,高度从 0 动画到 auto:
      // 不等它稳,下一趟可能读到高度为 0 的首帧、判成"还没展开"再点一次——那一下是**收起**,叶子静默变少。
      await expect(children, '目录点开后子菜单没有展开').toBeVisible({ timeout: 5_000 })
      clicked = true
    }
    if (!clicked) break
  }
  const leaves = page.locator(LEAF)
  const names = (await leaves.allInnerTexts()).map((s) => s.trim())
  return names.map((name, i) => ({ name, item: leaves.nth(i) })).filter((l) => l.name)
}

/** 只需要名字的调用方用这个。 */
export async function sidebarLeafNames(page: Page): Promise<string[]> {
  return (await sidebarLeaves(page)).map((l) => l.name)
}

/** 落在选择页(用户没有默认应用)时进第一个应用;已在应用里则原地不动。 */
export async function enterFirstAppIfNeeded(page: Page) {
  if (!/\/module/.test(page.url())) return
  const cards = page.locator('.card')
  if ((await cards.count()) > 0) {
    await cards.first().click()
    await expect(page).not.toHaveURL(/\/module/, { timeout: 10_000 })
  }
}
