import { test, expect, type Page, type Locator } from '@playwright/test'
import { login, enterSystemApp } from './helpers'

// 只操作本次 Playwright 临时宿主，不创建或删除业务记录。
test.beforeEach(async ({ page }) => {
  await page.addInitScript((react) => {
    if (!localStorage.getItem('app')) {
      const settings = { motionMode: 'standard', pageTransition: 'fade-slide', locale: 'zh-CN' }
      localStorage.setItem('app', JSON.stringify(react ? { state: settings, version: 0 } : settings))
    }
  }, true)
})
test.setTimeout(60_000)

async function setSettings(page: Page, settings: Record<string, string>) {
  await page.evaluate(async (settings) => {
    const { useAppStore } = await import('/src/stores/app.ts')
    for (const [key, value] of Object.entries(settings)) useAppStore.getState().setSetting(key, value)
  }, settings)
}

async function go(page: Page, name: string, path: string) {
  await expect(page.locator('.tabsbar')).toBeVisible()
  await page.keyboard.press('Control+k')
  const search = page.getByPlaceholder('搜索菜单…')
  await search.fill(name)
  await search.press('Enter')
  await expect(page).toHaveURL(new RegExp(path + '$'))
  // URL 会先于 React 的页面提交变化；确认标签已登记后再发起下一次操作。
  await expect(page.locator('.tab-chip.active').filter({ hasText: name })).toBeVisible()
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
  }, { selector: '.form-container-motion', phase })
}

const host = '.shell-page'
const chip = '.tab-chip'

test('动效模式持久化，系统减少动态效果实时优先，关闭后仍能导航', async ({ page }) => {
  await login(page)
  await enterSystemApp(page)
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'standard')
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'off')
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await expect(page.locator('html')).toHaveAttribute('data-motion', 'standard')

  await page.getByRole('button', { name: '系统设置', exact: true }).click()
  const settings = page.locator('.ant-drawer')
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
    await expect.poll(() => page.locator('.form-container-motion').evaluate((root) =>
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
  }, '.dash-banner')
  await login(page)
  await enterSystemApp(page)
  const banner = page.locator('.dash-banner')
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
    await role.press('Enter')
    await expect(page).toHaveURL(/\/system\/role$/)

    const alignmentError = () => page.locator('.tabsbar').evaluate((element, selectors) => {
      const active = element.querySelector(selectors.chip + '.active')!
      const marker = element.querySelector(selectors.marker)!
      const a = active.getBoundingClientRect()
      const m = marker.getBoundingClientRect()
      return Math.max(Math.abs(a.left - m.left), Math.abs(a.width - m.width))
    }, { chip, marker: '.tabs-active-marker' })
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
