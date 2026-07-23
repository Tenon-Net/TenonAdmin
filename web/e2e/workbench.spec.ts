import { test, expect } from '@playwright/test'
import { login, enterApp, SYSTEM_APP } from './helpers'

/**
 * 工作台首页统计:数字必须来自后端,而不是当年那批写死的常量(24/186/312/5)。
 * 前置:后端在跑(默认 :5100),超管账号可登录。
 */

test('工作台:四个数字来自接口,趋势图横轴是最近 7 天真实日期', async ({ page }) => {
  // /workbench 只挂在「系统」应用下:登录落在哪个应用取决于用户的默认应用(全局可变状态,
  // module-switch 那条用例会改它),所以这里显式进「系统」,不靠登录后碰巧落对地方。
  await login(page)
  await enterApp(page, SYSTEM_APP)

  // 接口的真值先抓在手里,再拿去和页面上渲染出来的比对
  const [res] = await Promise.all([
    page.waitForResponse((r) => r.url().includes('/api/v1/dashboard/summary') && r.status() === 200),
    page.goto('/workbench'),
  ])
  const data = (await res.json()).data

  const cards = page.locator('.stat-val')
  await expect(cards).toHaveCount(4)
  const shown = (await cards.allInnerTexts()).map((s) => s.trim())
  expect(shown).toEqual([
    String(data.roles),
    String(data.users),
    String(data.perms),
    String(data.onlineSessions),
  ])

  // 占位常量的指纹:24/186/312/5。真跑起来不该再出现这组数(库里种子远没这么多)
  expect(shown.join(',')).not.toBe('24,186,312,5')

  // 趋势横轴 = 连续 7 天,最后一格是"服务器的今天"。
  // 不拿 Node 的 new Date() 去比:后端按**服务器本地时区**分天(CreateTime 由 AOP 写本地时间),
  // 而测试进程的 TZ 未必与之相同(CI 上常是 UTC/LA),那样比出来的失败是假的。
  // 改判:天数为 7、日期连续、且最后一格算上了刚才这次登录——后者才是"今天在最后"的真凭据。
  expect(data.trendDays).toHaveLength(7)
  expect(new Set(data.trendDays).size).toBe(7)
  const asDay = (s: string) => new Date(`2000-${s}T00:00:00Z`).getTime()
  for (let i = 1; i < 7; i++) {
    expect(asDay(data.trendDays[i]) - asDay(data.trendDays[i - 1])).toBe(86_400_000)
  }
  expect(data.trendLogins[6], '最后一格应当算上本次登录').toBeGreaterThan(0)
  expect(data.trendActiveUsers[6], '最后一格应当算上超管本人').toBeGreaterThan(0)

  // 图表画出来了(ECharts 落 canvas),不是空盒子
  await expect(page.locator('canvas').first()).toBeVisible()
})
