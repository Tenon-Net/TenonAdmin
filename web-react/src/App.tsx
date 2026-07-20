import { Alert, App as AntdApp, Button, Card, ConfigProvider, DatePicker, Empty, Input, Segmented, Space, Table, Tag, Typography, theme } from 'antd'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import antdZhCN from 'antd/locale/zh_CN'
import antdEnUS from 'antd/locale/en_US'
import { useTranslation } from 'react-i18next'
import { i18n } from '@/locales'
import { ACCENTS } from '@/theme/accents'
import { useAntdTheme } from '@/theme/useAntdTheme'
import { useAppStore, isDark, type Density } from '@/stores/app'
import LoginPage from '@/views/login/LoginPage'

/**
 * B2 的主题桥探针页。**故意不是 hello world**:只渲染文字的壳在下面任何一条假设坏掉时照样绿。
 *
 * 状态先用本地 `useState`,**B3 落 app store 后换成 store**(useAntdTheme 的入参形状不变)。
 * 到 B8 布局壳落地时整页删掉。
 */
export default function App() {
  // B3:改从 app store 取(原先是本地 useState)。**三条状态订阅 + 三条 action 选择器**
  //(action 的引用建店时定死、此后永不替换,那三条永远不会触发渲染)。
  // 分开订阅而不是整体订阅 —— 后者任何无关字段(collapsed / locale / 界面开关)变动
  // 都会重建整棵 ConfigProvider。
  const dark = useAppStore(isDark)
  const accent = useAppStore((s) => s.accent)
  const density = useAppStore((s) => s.density)
  const setThemeScheme = useAppStore((s) => s.setThemeScheme)
  const setAccent = useAppStore((s) => s.setAccent)
  const setDensity = useAppStore((s) => s.setDensity)
  const themeScheme = useAppStore((s) => s.themeScheme)
  // 带上 auto 档。B3 起 `themeScheme: 'auto'` 是**持久化的默认值**,只给 light/dark 两个按钮的话
  // 谁点一下就把 auto 永久换掉了(还会落盘)。
  const setScheme = (v: string) => setThemeScheme(v as 'light' | 'dark' | 'auto')
  const locale = useAppStore((s) => s.locale)
  const setLocale = useAppStore((s) => s.setLocale)

  const themeConfig = useAntdTheme({ dark, accent, density })

  return (
    // antd 自带文案(空态/分页/日期)是**另一套 locale**,与我们的 i18n 无关,必须一起切 ——
    // 否则是「中文界面 + No data」。
    <ConfigProvider theme={themeConfig} locale={locale === 'en-US' ? antdEnUS : antdZhCN}>
      {/* antd 的 `App` 提供 `message`/`modal`/`notification` 的**上下文实例** —— 静态的
          `message.success()` 拿不到 ConfigProvider 的主题与 locale(v5 起就会告警),
          所以全站统一走 `App.useApp()`,这层壳必须在 ConfigProvider 之内。 */}
      <AntdApp>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            {/* B2/B3/B4 的探针页暂居兜底路由;B6 落动态路由、B8 落布局壳时整个删掉。 */}
            <Route path="*" element={<Probe locale={locale} setLocale={setLocale} dark={dark} themeScheme={themeScheme} setScheme={setScheme} accent={accent} setAccent={setAccent} density={density} setDensity={setDensity} />} />
          </Routes>
        </BrowserRouter>
      </AntdApp>
    </ConfigProvider>
  )
}

/** 必须是 ConfigProvider 的**子组件** —— `theme.useToken()` 读的是最近的 Provider,同层读不到。 */
function Probe(p: {
  dark: boolean; themeScheme: string; setScheme: (v: string) => void
  locale: string; setLocale: (v: 'zh-CN' | 'en-US') => void
  accent: string; setAccent: (v: string) => void
  density: Density; setDensity: (v: Density) => void
}) {
  const { t: tr } = useTranslation()
  const { token } = theme.useToken()
  const t = token as unknown as Record<string, string>
  const cssVar = (n: string) => getComputedStyle(document.documentElement).getPropertyValue(n).trim()
  const blur = (sh?: string) => Math.max(0, ...[...(sh ?? '').matchAll(/(\d+)px/g)].map((m) => Number(m[1])))

  // 派生阴影的 alpha。基色带 alpha 时 antd 会把它乘进每一层,三档会整体塌下去。
  const alphas = [...(t.boxShadowDrawerLeft ?? '').matchAll(/rgba?\([^)]*?,\s*([\d.]+)\s*\)/g)].map((m) => Number(m[1]))
  const shadowOk = JSON.stringify(alphas) === JSON.stringify([0.08, 0.12, 0.05])

  // 恒等对抽样(全量在 antd-theme.spec.ts;这里只把最容易单边掰断的几对渲染成肉眼可见)。
  const identities: [string, string][] = [
    ['colorFillAlter', 'colorFillQuaternary'],
    ['colorBgContainerDisabled', 'colorFillTertiary'],
    ['colorBgTextHover', 'colorFillSecondary'],
    ['controlItemBgActive', 'colorPrimaryBg'],
    ['colorBorder', 'colorBorderDisabled'],
  ]
  const brokenIds = identities.filter(([a, b]) => String(t[a]) !== String(t[b]))

  // 单花括号插值坏掉的话这里会原样吐出「你好,{name}」—— 不抛错,只是页面上挂个花括号。
  const greeting = tr('workbench.welcome', { name: '张三' })
  // 期望值取自 **store**,不取自 `i18n.language`。后者正是被测变量,拿它推期望值 = 自指等式恒成立:
  // 删掉 init 里的 `lng: useAppStore.getState().locale`,`i18n.language` 并不是 undefined 而是被
  // `fallbackLng` 顶成 `'en-US'`,于是期望值跟着一起挪,中文下也照样绿。改比 store 之后,
  // 「i18n 说英文而 store 说中文」这件事本身就是红的 —— 那正是那次变异的真实症状。
  const greetingOk = greeting === (p.locale === 'en-US' ? 'Hello, 张三' : '你好,张三')

  const probes = [
    { ok: shadowOk, title: '派生阴影量级', desc: `boxShadowDrawerLeft alpha = [${alphas.join(', ')}],应为 [0.08, 0.12, 0.05]` },
    { ok: !/255,\s*255,\s*255/.test(t.boxShadowDrawerLeft ?? ''), title: '阴影不是白色发光', desc: t.colorShadow ?? '(空)' },
    { ok: brokenIds.length === 0, title: 'alias 恒等未被单边掰断', desc: brokenIds.length ? brokenIds.map(([a, b]) => `${a}(${t[a]}) ≠ ${b}(${t[b]})`).join(' | ') : `${identities.length} 对全相等` },
    { ok: Number.isFinite(token.borderRadius) && Number.isFinite(token.fontSizeLG), title: '尺寸不是 NaN', desc: `borderRadius=${token.borderRadius} fontSizeLG=${token.fontSizeLG}` },
    // 这条以前断的是 `colorBgContainer !== '#000000' && !!colorPrimary` —— 两句都**恒真**:
    // colorPrimary 必有值(defined 必留它),而 bgContainer 在明暗两种 algorithm 下都不可能是纯黑。
    // 也就是说它在自己声称检测的失败模式(tokens.css 没加载)下照样绿。直接问文档要变量才有判别力。
    { ok: cssVar('--color-shadow') !== '', title: 'tokens.css 进了文档', desc: `--color-shadow=${cssVar('--color-shadow') || '(读不到)'} bgContainer=${t.colorBgContainer}` },
    // 三个具名阴影是**角色名不是序数**:boxShadow→Modal(最重)、Secondary→弹层、Tertiary→message/
    // Segmented 滑块(最轻)。按 1/2/3 序号搬过来会把它们装反,而那个缺陷就长在本页顶部三个 Segmented 上。
    // desc 里两个都列出来:红的时候要一眼看出是插值坏了,还是 i18n 与 store 脱钩了。
    { ok: greetingOk, title: '共享文案 + 单花括号插值', desc: `store=${p.locale} i18n=${i18n.language} → ${greeting}` },
    { ok: blur(t.boxShadow) > blur(t.boxShadowSecondary) && blur(t.boxShadowSecondary) > blur(t.boxShadowTertiary), title: '具名阴影按角色由重到轻', desc: `Modal ${blur(t.boxShadow)}px > 弹层 ${blur(t.boxShadowSecondary)}px > 滑块 ${blur(t.boxShadowTertiary)}px` },
  ]

  return (
    <div style={{ padding: 24, minHeight: '100vh', background: token.colorBgLayout }}>
      <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
        <Space wrap data-testid="controls">
          <Segmented value={p.themeScheme} onChange={(v) => p.setScheme(String(v))} options={['auto', 'light', 'dark']} data-testid="seg-theme" />
          <Segmented value={p.locale} onChange={(v) => p.setLocale(v as 'zh-CN' | 'en-US')} options={[{ label: '中文', value: 'zh-CN' }, { label: 'EN', value: 'en-US' }]} data-testid="seg-locale" />
          <Segmented value={p.density} onChange={(v) => p.setDensity(v as Density)} options={['comfortable', 'compact']} data-testid="seg-density" />
          <Segmented value={p.accent} onChange={(v) => p.setAccent(String(v))} options={ACCENTS.map((a) => ({ label: a.replace('#', ''), value: a }))} data-testid="seg-accent" />
        </Space>

        {probes.map((x) => (
          <Alert key={x.title} type={x.ok ? 'success' : 'error'} showIcon title={x.title} description={x.desc} />
        ))}

        <Card title="填充阶(静息 vs hover):Tag / 禁用输入框走静息,菜单项 hover 走 hover 色">
          <Space wrap>
            <Tag>静息填充</Tag>
            <Button>默认</Button>
            <Button type="primary">主要</Button>
            <Button disabled>禁用</Button>
            <Input placeholder="占位色" style={{ width: 140 }} />
            <Input disabled placeholder="禁用底色" style={{ width: 140 }} />
          </Space>
        </Card>

        <Card title="表格 + 空态(边框/阴影/密度)">
          <Table
            size={p.density === 'compact' ? 'small' : 'middle'}
            dataSource={[{ key: 1, a: '行一', b: '值' }, { key: 2, a: '行二', b: '值' }]}
            columns={[{ title: '名称', dataIndex: 'a' }, { title: '值', dataIndex: 'b' }]}
            pagination={false}
          />
          {/* antd 自带文案的可见证据:空态与日期选择器都归 ConfigProvider 的 locale 管,不归我们的 i18n。 */}
          <div style={{ marginTop: 12 }}><Empty /></div>
          <DatePicker style={{ marginTop: 8 }} />
        </Card>

        <Typography.Paragraph type="secondary">v{__APP_VERSION__}</Typography.Paragraph>
      </Space>
    </div>
  )
}
