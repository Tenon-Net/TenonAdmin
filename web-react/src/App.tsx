import { useState } from 'react'
import { Alert, Button, Card, ConfigProvider, Empty, Input, Segmented, Space, Table, Tag, Typography, theme } from 'antd'
import { ACCENTS } from '@/theme/accents'
import { useAntdTheme } from '@/theme/useAntdTheme'
import type { Density } from '@/theme/antd-theme'

/**
 * B2 的主题桥探针页。**故意不是 hello world**:只渲染文字的壳在下面任何一条假设坏掉时照样绿。
 *
 * 状态先用本地 `useState`,**B3 落 app store 后换成 store**(useAntdTheme 的入参形状不变)。
 * 到 B8 布局壳落地时整页删掉。
 */
export default function App() {
  const [dark, setDark] = useState(false)
  const [accent, setAccent] = useState<string>(ACCENTS[0])
  const [density, setDensity] = useState<Density>('comfortable')

  const themeConfig = useAntdTheme({ dark, accent, density })

  return (
    <ConfigProvider theme={themeConfig}>
      <Probe dark={dark} setDark={setDark} accent={accent} setAccent={setAccent} density={density} setDensity={setDensity} />
    </ConfigProvider>
  )
}

/** 必须是 ConfigProvider 的**子组件** —— `theme.useToken()` 读的是最近的 Provider,同层读不到。 */
function Probe(p: {
  dark: boolean; setDark: (v: boolean) => void
  accent: string; setAccent: (v: string) => void
  density: Density; setDensity: (v: Density) => void
}) {
  const { token } = theme.useToken()
  const t = token as unknown as Record<string, string>

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

  const probes = [
    { ok: shadowOk, title: '派生阴影量级', desc: `boxShadowDrawerLeft alpha = [${alphas.join(', ')}],应为 [0.08, 0.12, 0.05]` },
    { ok: !/255,\s*255,\s*255/.test(t.boxShadowDrawerLeft ?? ''), title: '阴影不是白色发光', desc: t.colorShadow ?? '(空)' },
    { ok: brokenIds.length === 0, title: 'alias 恒等未被单边掰断', desc: brokenIds.length ? brokenIds.map(([a, b]) => `${a}(${t[a]}) ≠ ${b}(${t[b]})`).join(' | ') : `${identities.length} 对全相等` },
    { ok: Number.isFinite(token.borderRadius) && Number.isFinite(token.fontSizeLG), title: '尺寸不是 NaN', desc: `borderRadius=${token.borderRadius} fontSizeLG=${token.fontSizeLG}` },
    { ok: t.colorBgContainer !== '#000000' && !!t.colorPrimary, title: 'tokens.css 进了文档', desc: `bgContainer=${t.colorBgContainer} primary=${t.colorPrimary}` },
  ]

  return (
    <div style={{ padding: 24, minHeight: '100vh', background: token.colorBgLayout }}>
      <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
        <Space wrap data-testid="controls">
          <Segmented value={p.dark ? 'dark' : 'light'} onChange={(v) => p.setDark(v === 'dark')} options={['light', 'dark']} data-testid="seg-theme" />
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
          <div style={{ marginTop: 12 }}><Empty /></div>
        </Card>

        <Typography.Paragraph type="secondary">v{__APP_VERSION__}</Typography.Paragraph>
      </Space>
    </div>
  )
}
