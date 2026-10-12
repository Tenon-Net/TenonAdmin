import { test, expect, type Page, type Locator } from '@playwright/test'
import { login, enterSystemApp } from './helpers'

// 只操作本次 Playwright 临时宿主，不创建或删除业务记录。
test.beforeEach(async ({ page }) => {
  await page.addInitScript((react) => {
    if (!localStorage.getItem('app')) {
      const settings = { motionMode: 'standard', pageTransition: 'fade-slide', locale: 'zh-CN' }
      localStorage.setItem('app', JSON.stringify(react ? { state: settings, version: 0 } : settings))
    }
  }, false)
})
test.setTimeout(60_000)

async function setSettings(page: Page, settings: Record<string, string>) {
  await page.evaluate(async (settings) => {
    const { useAppStore } = await import('/src/stores/app.ts')
    Object.assign(useAppStore(), settings)
  }, settings)
}

async function go(page: Page, name: string, path: string) {
  await page.keyboard.press('Control+k')
  const search = page.getByPlaceholder('搜索菜单…')
  await search.fill(name)
  await search.press('Enter')
  await expect(page).toHaveURL(new RegExp(path + '$'))
  await expect(page.locator('.chip.active').filter({ hasText: name })).toBeVisible()
  await expect(search).toBeHidden()
}


// 等待组件库真正进入 active 阶段再测量，覆盖其多帧准备过程。
async function clickAndReadPanelMotion(trigger: Locator, phase: 'enter' | 'leave') {
  return trigger.evaluate(async (button, { selector, phase }) => {
    ;(button as HTMLElement).click()
    const deadline = performance.now() + 1000
    let root: Element | null = null
    do {
      await new Promise(requestAnimationFrame)
      root = document.querySelector(selector)
      if (!root) continue
      const active = [root, ...root.querySelectorAll('*')].find((element) => {
        const classes = element.className.toString()
        return (phase === 'enter' ? /enter-active|appear-active/ : /leave-active/).test(classes)
          && !classes.includes('mask')
      })
      if (active) {
        const style = getComputedStyle(active)
        return { duration: [style.animationDuration, style.transitionDuration].join(','), className: active.className.toString() }
      }
    } while (performance.now() < deadline)
    return { duration: '', className: root?.className.toString() ?? 'panel missing' }
  }, { selector: '.motion-form-panel', phase })
}

const host = '.page'
const chip = '.chip'

test('动效模式持久化，系统减少动态效果实时优先，关闭后仍能导航', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'standard')
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'off')
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'standard')

  await page.getByRole('button', { name: '系统设置', exact: true }).click()
  await page.locator('.n-drawer-content').getByText('外观', { exact: true }).click()
  const settings = page.locator('.n-drawer-content')
  const hint = settings.getByText(/浏览器报告减少动态效果偏好/)
  await expect(hint).toBeHidden()
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await expect(hint).toContainText('标准')
  await settings.getByText('轻量', { exact: true }).click()
  await expect(hint).toContainText('轻量')
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'off')
  await settings.getByText('关闭', { exact: true }).click()
  await expect(hint).toBeHidden()
  await settings.getByText('标准', { exact: true }).click()
  await expect(hint).toContainText('标准')
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await expect(hint).toBeHidden()
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'standard')
  await settings.getByText('轻量', { exact: true }).click()
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'light')
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'light')
  await setSettings(page, { motionMode: 'off' })
  await go(page, '角色管理', '/system/role')
  await expect(page.locator(host).getByRole('button', { name: /^新\s*增$/ })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'off')
})

test('缓存恢复保留筛选和滚动，快速切页与标签刷新得到最新状态', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  await go(page, '角色管理', '/system/role')
  const input = page.locator(host).locator('input:visible').first()
  await input.fill('缓存筛选')
  // 制造长内容验证共用滚动宿主；不依赖种子记录数量。
  await page.locator(host).evaluate((element) => {
    const active = [...element.children].find((child) => getComputedStyle(child).display !== 'none') as HTMLElement
    active.style.minHeight = '1600px'
    element.scrollTop = 240
    element.dispatchEvent(new Event('scroll'))
  })
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(240)
  await go(page, '用户管理', '/system/user')
  const roleTab = page.locator('.tabsbar').locator(chip).filter({ hasText: '角色管理' })
  const userTab = page.locator('.tabsbar').locator(chip).filter({ hasText: '用户管理' })
  // dispatch 不等待元素稳定，覆盖上一个过渡尚未结束时再次切换。
  await roleTab.dispatchEvent('click')
  await userTab.dispatchEvent('click')
  await roleTab.dispatchEvent('click')
  await expect(page).toHaveURL(/\/system\/role$/)
  await expect(input).toHaveValue('缓存筛选')
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(240)

  await roleTab.click({ button: 'right' })
  await page.getByText('刷新', { exact: true }).last().click()
  await expect(input).toHaveValue('')
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(0)
})

test('三种模式下弹窗与抽屉反复开关，校验失败保留表单，嵌套搜索切页收起弹层', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  await go(page, '角色管理', '/system/role')
  for (const motionMode of ['standard', 'light', 'off']) {
    for (const formStyle of ['modal', 'drawer']) {
      await setSettings(page, { motionMode, formStyle })
      await page.locator(host).getByRole('button', { name: /^新\s*增$/ }).click()
      const dialog = page.locator('[role="dialog"]:visible').filter({ hasText: '新增角色' })
      await expect(dialog).toHaveCount(1)
      // 连续触发同一保存按钮，校验失败仍只保留当前表单。
      await dialog.getByRole('button', { name: /^保\s*存$/ }).evaluate((button) => {
        ;(button as HTMLElement).click()
        ;(button as HTMLElement).click()
      })
      await expect(dialog).toBeVisible()
      await expect(dialog.getByText('请输入角色编码', { exact: true })).toBeVisible()
      await dialog.getByRole('button', { name: /^取\s*消$/ }).click()
      await expect(dialog).toBeHidden()
    }
  }

  await page.locator(host).getByRole('button', { name: /^新\s*增$/ }).click()
  // 菜单搜索嵌套于表单弹层上；切页后两个弹层都应收起。
  await go(page, '用户管理', '/system/user')
  await expect(page.locator('[role="dialog"]:visible')).toHaveCount(0)
  await go(page, '角色管理', '/system/role')
  await expect(page.locator('[role="dialog"]:visible')).toHaveCount(0)
  await expect(page.locator(host).getByRole('button', { name: /^新\s*增$/ })).toBeVisible()
})

async function setTabCachePolicy(page: Page, noCache: boolean) {
  await page.evaluate(async (noCache) => {
    const { useTabsStore } = await import('/src/stores/tabs.ts')
    const tab = useTabsStore().tabs.find((tab: { path: string }) => tab.path === '/system/role')
    if (tab) tab.noCache = noCache
  }, noCache)
}

async function scrollPage(page: Page, top: number) {
  await page.locator(host).evaluate((element, top) => {
    element.scrollTop = top
    element.dispatchEvent(new Event('scroll'))
  }, top)
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(top)
}

test('侧栏折叠真实过渡时长随三档模式和系统偏好变化', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  const aside = page.locator('.aside')
  const collapse = page.getByRole('button', { name: /^折叠菜单$|^打开菜单$/ })
  for (const [motionMode, duration, width] of [
    ['standard', '0.2s', '76px'],
    ['light', '0.12s', '236px'],
    ['off', '0s', '76px'],
  ]) {
    await setSettings(page, { motionMode })
    await expect(aside).toHaveCSS('transition-duration', duration!)
    await collapse.click()
    await expect(aside).toHaveCSS('width', width!)
  }
  await setSettings(page, { motionMode: 'standard' })
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await expect(aside).toHaveCSS('transition-duration', '0s')
  await collapse.click()
  await expect(aside).toHaveCSS('width', '236px')
  expect(await aside.evaluate((element) => element.getAnimations().length)).toBe(0)
})

test('滚动记忆两端都排除 noCache，策略切换和关闭重开不留下旧位置', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  await setSettings(page, { motionMode: 'off' })
  await go(page, '角色管理', '/system/role')
  await expect(page.locator(host).getByText('角色名称', { exact: true }).first()).toBeVisible()
  // 重挂后的页面也足够长，避免短内容自然把 scrollTop 截断为 0，掩盖错误恢复。
  await page.addStyleTag({ content: '.page > .page-view { min-height: 1600px; }' })
  await scrollPage(page, 240)
  await go(page, '用户管理', '/system/user')
  const roleTab = page.locator('.tabsbar').locator(chip).filter({ hasText: '角色管理' })
  const revisitRole = async () => {
    await roleTab.click()
    await expect(page).toHaveURL(/\/system\/role$/)
    // 用户页也有“新增”，不能用共用按钮把尚未提交的角色页误判为已就绪。
    await expect(page.locator(host).getByText('角色名称', { exact: true }).first()).toBeVisible()
  }
  await revisitRole()
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(240)

  await setTabCachePolicy(page, true)
  await go(page, '用户管理', '/system/user')
  await revisitRole()
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(0)
  await scrollPage(page, 320)
  await go(page, '用户管理', '/system/user')
  await revisitRole()
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(0)

  await setTabCachePolicy(page, false)
  await go(page, '用户管理', '/system/user')
  await revisitRole()
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(0)
  await scrollPage(page, 180)
  await go(page, '用户管理', '/system/user')
  await roleTab.click({ button: 'right' })
  await page.getByText('关闭', { exact: true }).last().click()
  await expect(roleTab).toHaveCount(0)
  await go(page, '角色管理', '/system/role')
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(0)
})


test('刷新非活动缓存标签只重置目标页的状态与滚动', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  await setSettings(page, { motionMode: 'off' })
  await go(page, '角色管理', '/system/role')
  await expect(page.locator(host).getByText('角色名称', { exact: true }).first()).toBeVisible()
  await page.addStyleTag({ content: '.page > .page-view { min-height: 1600px; }' })
  const input = page.locator(host).locator('input:visible').first()
  await input.fill('目标页待刷新')
  await scrollPage(page, 240)
  await go(page, '用户管理', '/system/user')
  await expect(page.locator(host).getByText('账号', { exact: true }).first()).toBeVisible()
  await input.fill('当前页保持')
  await scrollPage(page, 120)
  const roleTab = page.locator('.tabsbar').locator(chip).filter({ hasText: '角色管理' })
  await roleTab.click({ button: 'right' })
  await page.getByText('刷新', { exact: true }).last().click()
  await expect(input).toHaveValue('当前页保持')
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(120)
  await roleTab.click()
  await expect(page).toHaveURL(/\/system\/role$/)
  await expect(input).toHaveValue('')
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(0)
})

async function readRunningPanelMotion(root: Locator) {
  return root.evaluate((element) => element.getAnimations({ subtree: true }).filter((animation) => {
    const target = (animation.effect as KeyframeEffect | null)?.target
    // 只测组件库的面板过渡，表单内必要的加载 spinner 可以继续转动。
    return animation.playState === 'running' && target instanceof Element
      && /(?:enter|appear|leave)(?:-active)?(?:\s|$)/.test(target.className.toString())
      && !target.className.toString().includes('mask')
  }).map((animation) => ({
    target: ((animation.effect as KeyframeEffect).target as Element).className.toString(),
    duration: animation.effect?.getTiming().duration,
  })))
}

test('弹层入退场执行中切换档位或系统偏好会及时收尾且释放滚动锁', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  await go(page, '角色管理', '/system/role')
  // 只拉长标准档已有参数，确保策略切换发生在真实动画中途；收尾必须快于原来的 2 秒。
  await page.addStyleTag({
    content: ":root[data-motion='standard'] { --motion-duration-panel-enter: 2000ms; --motion-duration-panel-exit: 2000ms; }",
  })
  const root = page.locator('.motion-form-panel')
  const dialog = page.locator('[role="dialog"]:visible').filter({ hasText: '新增角色' })
  const changePolicy = async (policy: string) => {
    if (policy === 'reduce') await page.emulateMedia({ reducedMotion: 'reduce' })
    else await setSettings(page, { motionMode: policy })
  }
  for (const formStyle of ['modal', 'drawer']) {
    for (const policy of ['light', 'off', 'reduce']) {
      await page.emulateMedia({ reducedMotion: 'no-preference' })
      await setSettings(page, { motionMode: 'standard', formStyle })
      const entering = await clickAndReadPanelMotion(page.locator(host).getByRole('button', { name: /^新\s*增$/ }), 'enter')
      expect(entering.duration.split(',').map(Number.parseFloat), entering.className).toContain(2)
      // active 类先于浏览器启动实际过渡；等运行态后再切换策略，避免只测到准备帧。
      await expect.poll(async () => (await readRunningPanelMotion(root)).length, { timeout: 750 }).toBeGreaterThan(0)
      await changePolicy(policy)
      await expect(dialog).toHaveCount(1)
      await expect.poll(() => readRunningPanelMotion(root), { timeout: 750 }).toEqual([])
      await expect(dialog).toBeVisible()
      await expect.poll(() => root.evaluate((element) =>
        [element, ...element.querySelectorAll('*')].some((node) => /enter-active|appear-active/.test(node.className.toString())),
      )).toBe(false)

      await page.emulateMedia({ reducedMotion: 'no-preference' })
      await setSettings(page, { motionMode: 'standard' })
      const leaving = await clickAndReadPanelMotion(dialog.getByRole('button', { name: /^取\s*消$/ }), 'leave')
      expect(leaving.duration.split(',').map(Number.parseFloat), leaving.className).toContain(2)
      await expect.poll(async () => (await readRunningPanelMotion(root)).length, { timeout: 750 }).toBeGreaterThan(0)
      await changePolicy(policy)
      await expect(dialog).toBeHidden({ timeout: 750 })
      await expect(page.locator('body')).not.toHaveCSS('overflow', 'hidden')
      await expect(page.locator('.n-modal-mask, .n-drawer-mask').filter({ visible: true })).toHaveCount(0)
    }
  }
})

test('慢路由模块加载期间快速切页，迟到模块不能改变前台或滚动记忆', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  await go(page, '用户管理', '/system/user')
  await go(page, '角色管理', '/system/role')
  // URL/标签先更新，out-in 的旧用户页此时可能还在；只在建立初始状态时等真实角色页。
  await expect(page.locator(host).getByText('角色名称', { exact: true }).first()).toBeVisible()
  await page.addStyleTag({ content: '.page > .page-view { min-height: 1600px; }' })
  const input = page.locator(host).locator('input:visible').first()
  await input.fill('路由模块竞态')
  await scrollPage(page, 240)

  let release!: () => void
  let intercepted!: () => void
  const gate = new Promise<void>((resolve) => { release = resolve })
  const started = new Promise<void>((resolve) => { intercepted = resolve })
  const modulePath = '/src/views/system/position/index.vue'
  const loaded = page.waitForResponse((response) => new URL(response.url()).pathname === modulePath)
  // 拦截动态 import 的 JS 模块，而不是业务 API。每个用例的新 context 保证模块尚未加载。
  await page.route((url) => url.pathname === modulePath, async (route) => {
    intercepted()
    await gate
    await route.continue()
  })
  try {
    await go(page, '岗位管理', '/system/position')
    await started
    const roleTab = page.locator('.tabsbar').locator(chip).filter({ hasText: '角色管理' })
    const userTab = page.locator('.tabsbar').locator(chip).filter({ hasText: '用户管理' })
    await roleTab.dispatchEvent('click')
    await userTab.dispatchEvent('click')
    await roleTab.dispatchEvent('click')
    await expect(page).toHaveURL(/\/system\/role$/)
    await expect(input).toHaveValue('路由模块竞态')
    await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(240)
  } finally {
    release()
  }
  await loaded
  // 等模块实际解析执行完，并跨两帧提交；只等响应无法证明迟到模块不会影响当前页。
  await page.evaluate(async (path) => {
    await import(/* @vite-ignore */ path)
    await new Promise(requestAnimationFrame)
    await new Promise(requestAnimationFrame)
  }, modulePath)
  await expect(page).toHaveURL(/\/system\/role$/)
  await expect(input).toHaveValue('路由模块竞态')
  await expect.poll(() => page.locator(host).evaluate((element) => element.scrollTop)).toBe(240)
  await expect(page.locator('.tabsbar').locator(chip + '.active')).toContainText('角色管理')
  await go(page, '用户管理', '/system/user')
  await expect(page.locator(host).getByRole('button', { name: /^新\s*增$/ })).toBeVisible()
})


test('公共面板实际采用统一进退时长，快速反向关闭不残留遮罩', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  await go(page, '角色管理', '/system/role')
  for (const formStyle of ['modal', 'drawer']) {
    await setSettings(page, { motionMode: 'standard', formStyle })
    const entering = await clickAndReadPanelMotion(page.locator(host).getByRole('button', { name: /^新\s*增$/ }), 'enter')
    expect(entering.duration.split(',').map((value) => parseFloat(value)), entering.className).toContain(0.26)
    const dialog = page.locator('[role="dialog"]:visible').filter({ hasText: '新增角色' })
    // 完整入场后测退出；入场中的反向关闭可能由组件库直接收尾而不创建 leave 动画。
    await expect.poll(() => page.locator('.motion-form-panel').evaluate((root) =>
      [root, ...root.querySelectorAll('*')].some((element) => /enter-active|appear-active/.test(element.className.toString())),
    )).toBe(false)
    const leaving = await clickAndReadPanelMotion(dialog.getByRole('button', { name: /^取\s*消$/ }), 'leave')
    expect(leaving.duration.split(',').map((value) => parseFloat(value)), leaving.className).toContain(0.18)
    await expect(dialog).toBeHidden()
    await expect(page.locator('body')).not.toHaveCSS('overflow', 'hidden')

    await clickAndReadPanelMotion(page.locator(host).getByRole('button', { name: /^新\s*增$/ }), 'enter')
    await dialog.getByRole('button', { name: /^取\s*消$/ }).dispatchEvent('click')
    await expect(dialog).toBeHidden()
    await expect(page.locator('body')).not.toHaveCSS('overflow', 'hidden')
  }
})


test('首次内容入场结束释放 transform，缓存恢复不重复播放', async ({ page }) => {
  await page.addInitScript((selector) => {
    const original = Element.prototype.animate
    const state = window as unknown as { contentEntrances: number }
    state.contentEntrances = 0
    Element.prototype.animate = function (...args) {
      if (this.matches(selector)) state.contentEntrances++
      return original.apply(this, args)
    }
  }, '.banner')
  await login(page)
  await enterSystemApp(page)
  const banner = page.locator('.banner')
  await expect(banner).toBeVisible()
  await expect.poll(() => page.evaluate(() => (window as unknown as { contentEntrances: number }).contentEntrances)).toBeGreaterThan(0)
  await expect.poll(() => banner.evaluate((element) => element.getAnimations().length)).toBe(0)
  await expect.poll(() => banner.evaluate((element) => getComputedStyle(element).transform)).toBe('none')
  const initial = await page.evaluate(() => (window as unknown as { contentEntrances: number }).contentEntrances)

  await go(page, '角色管理', '/system/role')
  await page.locator('.tabsbar').locator(chip).filter({ hasText: '工作台' }).click()
  await expect(page).toHaveURL(/\/workbench$/)
  await expect(banner).toBeVisible()
  expect(await page.evaluate(() => (window as unknown as { contentEntrances: number }).contentEntrances)).toBe(initial)

  await setSettings(page, { pageTransition: 'none' })
  await page.reload()
  await expect(banner).toBeVisible()
  expect(await page.evaluate(() => (window as unknown as { contentEntrances: number }).contentEntrances)).toBe(0)
})

test.describe('窄屏和触摸输入', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true })
  test('标签标记跟随横向滚动、尺寸和键盘切换，关闭动效仍准确定位', async ({ page }) => {
    await login(page)
    await enterSystemApp(page)
    await go(page, '角色管理', '/system/role')
    await go(page, '用户管理', '/system/user')
    await go(page, '岗位管理', '/system/position')
    await setSettings(page, { motionMode: 'off' })
    const role = page.locator('.tabsbar').locator(chip).filter({ hasText: '角色管理' })
    await role.focus()
    await role.press('Enter')
    await expect(page).toHaveURL(/\/system\/role$/)
    // 真正用触摸切换标签，再用键盘返回，覆盖两种输入途径。
    await page.locator('.tabsbar').locator(chip).filter({ hasText: '用户管理' }).tap()
    await expect(page).toHaveURL(/\/system\/user$/)
    await role.focus()
    await expect(role).toBeFocused()
    await role.press('Enter')
    await expect(page).toHaveURL(/\/system\/role$/)

    const alignmentError = () => page.locator('.tabsbar').evaluate((element, selectors) => {
      const active = element.querySelector(selectors.chip + '.active')!
      const marker = element.querySelector(selectors.marker)!
      const a = active.getBoundingClientRect()
      const m = marker.getBoundingClientRect()
      return Math.max(Math.abs(a.left - m.left), Math.abs(a.width - m.width))
    }, { chip, marker: '.tab-active-indicator' })
    await expect.poll(alignmentError).toBeLessThan(2)
    await page.setViewportSize({ width: 520, height: 844 })
    await expect.poll(alignmentError).toBeLessThan(2)
    await page.locator('.tabsbar').evaluate((element) => {
      for (const node of element.querySelectorAll<HTMLElement>('*')) {
        if (node.scrollWidth > node.clientWidth && getComputedStyle(node).overflowX !== 'visible') {
          node.scrollLeft = node.scrollWidth
          node.dispatchEvent(new Event('scroll'))
        }
      }
    })
    await expect.poll(alignmentError).toBeLessThan(2)
  })
})


test('慢请求完成时不会把已经切走的页面带回前台，请求失败后仍可操作', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  await go(page, '角色管理', '/system/role')
  let release!: () => void
  let intercepted!: () => void
  const gate = new Promise<void>((resolve) => { release = resolve })
  const started = new Promise<void>((resolve) => { intercepted = resolve })
  const completed = page.waitForResponse((response) => response.url().includes('/api/v1/sys/position/page'))
  await page.route('**/api/v1/sys/position/page**', async (route) => {
    intercepted()
    await gate
    await route.continue()
  })
  try {
    await go(page, '岗位管理', '/system/position')
    await started
    await page.locator('.tabsbar').locator(chip).filter({ hasText: '角色管理' }).dispatchEvent('click')
    await expect(page).toHaveURL(/\/system\/role$/)
  } finally {
    release()
  }
  await completed
  await expect(page).toHaveURL(/\/system\/role$/)
  await expect(page.locator(host).getByRole('button', { name: /^新\s*增$/ })).toBeVisible()

  await page.route('**/api/v1/sys/user/page**', (route) => route.fulfill({
    status: 503, contentType: 'application/json', body: JSON.stringify({ code: 503, message: '测试服务不可用' }),
  }))
  const failedResponse = page.waitForResponse((response) => response.url().includes('/api/v1/sys/user/page') && response.status() === 503)
  await go(page, '用户管理', '/system/user')
  await failedResponse
  await expect(page.locator(host).getByRole('button', { name: /^新\s*增$/ })).toBeVisible()
  await go(page, '角色管理', '/system/role')
  await expect(page.locator(host).getByRole('button', { name: /^新\s*增$/ })).toBeVisible()
})
