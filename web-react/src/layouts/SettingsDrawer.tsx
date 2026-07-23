import { type ReactNode } from 'react'
import { App, Button, ColorPicker, Divider, Drawer, Input, Popconfirm, Segmented, Select, Switch, Tabs } from 'antd'
import { CheckOutlined, CopyOutlined, DesktopOutlined, MoonOutlined, ReloadOutlined, SunOutlined } from '@ant-design/icons'
import { useTranslation } from 'react-i18next'
import { useAppStore, type Density, type FormStyle, type PageTransition, type ThemeScheme } from '@/stores/app'
import { ACCENTS } from '@/theme/accents'
import { LayoutModeCards } from './LayoutModeCards'
import './settings-drawer.css'

// 设置项行:左标签 + 右控件。整行 role=group + aria-label,读屏聚焦控件时能读到设置名。单一使用者,内联不单开文件。
function SettingRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="setting-row" role="group" aria-label={label}>
      <span className="setting-row-label">{label}</span>
      <div className="setting-row-ctl">{children}</div>
    </div>
  )
}

/**
 * 设置抽屉(对齐 Vue SettingsDrawer.vue)。三 tab:外观(主题模式/主色/密度)、布局(布局模式卡片 + 界面开关)、
 * 通用(水印 + 表单形态)。所有控件都接了生效的壳(布局模式→D2a 壳、水印/fixedHeader/pageTransition/breadcrumb 均已接线)。
 * 「复制配置」仅 DEV 显示:导出当前外观值,粘回 stores/app.ts 的 DEFAULTS 即为消费者的新默认。
 */
export function SettingsDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const themeScheme = useAppStore((s) => s.themeScheme)
  const accent = useAppStore((s) => s.accent)
  const density = useAppStore((s) => s.density)
  const showBreadcrumb = useAppStore((s) => s.showBreadcrumb)
  const showTabs = useAppStore((s) => s.showTabs)
  const fixedHeader = useAppStore((s) => s.fixedHeader)
  const pageTransition = useAppStore((s) => s.pageTransition)
  const grayscale = useAppStore((s) => s.grayscale)
  const collapsed = useAppStore((s) => s.collapsed)
  const watermark = useAppStore((s) => s.watermark)
  const watermarkText = useAppStore((s) => s.watermarkText)
  const formStyle = useAppStore((s) => s.formStyle)
  const setThemeScheme = useAppStore((s) => s.setThemeScheme)
  const setAccent = useAppStore((s) => s.setAccent)
  const setDensity = useAppStore((s) => s.setDensity)
  const setSetting = useAppStore((s) => s.setSetting)
  const toggleCollapsed = useAppStore((s) => s.toggleCollapsed)
  const resetSettings = useAppStore((s) => s.resetSettings)

  // 「复制配置」是给消费者 fork 后改 DEFAULTS 用的开发期动作,不该出现在终端管理员的设置里 —— 仅 DEV 显示。
  const showCopyConfig = import.meta.env.DEV && typeof navigator !== 'undefined' && !!navigator.clipboard
  async function copyConfig() {
    await navigator.clipboard.writeText(JSON.stringify(useAppStore.getState().exportSettings(), null, 2))
    message.success(t('settings.configCopied'))
  }

  const appearance = (
    <div className="settings-pane">
      <Divider titlePlacement="start">{t('settings.themeMode')}</Divider>
      <Segmented
        block
        value={themeScheme}
        onChange={(v) => setThemeScheme(v as ThemeScheme)}
        options={[
          { value: 'light', label: t('app.light'), icon: <SunOutlined /> },
          { value: 'dark', label: t('app.dark'), icon: <MoonOutlined /> },
          { value: 'auto', label: t('settings.auto'), icon: <DesktopOutlined /> },
        ]}
      />

      <Divider titlePlacement="start">{t('settings.themeColor')}</Divider>
      <div className="accent-dots">
        {ACCENTS.map((c) => (
          <button
            key={c}
            type="button"
            className={`accent-dot${accent === c ? ' on' : ''}`}
            style={{ background: c }}
            aria-label={`${t('settings.themeColor')} ${c}`}
            aria-pressed={accent === c}
            onClick={() => setAccent(c)}
          >
            {accent === c ? <CheckOutlined className="accent-tick" /> : null}
          </button>
        ))}
      </div>
      <ColorPicker
        value={accent}
        format="hex"
        disabledAlpha
        presets={[{ label: t('settings.custom'), colors: [...ACCENTS] }]}
        onChange={(_, css) => setAccent(css)}
      />

      <Divider titlePlacement="start">{t('app.density')}</Divider>
      <Segmented
        block
        value={density}
        onChange={(v) => setDensity(v as Density)}
        options={[
          { value: 'comfortable', label: t('app.comfortable') },
          { value: 'compact', label: t('app.compact') },
        ]}
      />
    </div>
  )

  const layout = (
    <div className="settings-pane">
      <Divider titlePlacement="start">{t('settings.layout')}</Divider>
      <LayoutModeCards />

      <Divider titlePlacement="start">{t('settings.interface')}</Divider>
      <SettingRow label={t('settings.showBreadcrumb')}>
        <Switch size="small" checked={showBreadcrumb} onChange={(v) => setSetting('showBreadcrumb', v)} />
      </SettingRow>
      <SettingRow label={t('settings.showTabs')}>
        <Switch size="small" checked={showTabs} onChange={(v) => setSetting('showTabs', v)} />
      </SettingRow>
      <SettingRow label={t('settings.fixedHeader')}>
        <Switch size="small" checked={fixedHeader} onChange={(v) => setSetting('fixedHeader', v)} />
      </SettingRow>
      <SettingRow label={t('settings.pageTransition')}>
        <Select
          size="small"
          style={{ width: 120 }}
          value={pageTransition}
          onChange={(v) => setSetting('pageTransition', v as PageTransition)}
          options={[
            { value: 'none', label: t('settings.transitionNone') },
            { value: 'fade', label: t('settings.transitionFade') },
            { value: 'fade-slide', label: t('settings.transitionFadeSlide') },
          ]}
        />
      </SettingRow>
      <SettingRow label={t('settings.grayscale')}>
        <Switch size="small" checked={grayscale} onChange={(v) => setSetting('grayscale', v)} />
      </SettingRow>
      <SettingRow label={t('app.collapse')}>
        <Switch size="small" checked={collapsed} onChange={() => toggleCollapsed()} />
      </SettingRow>
    </div>
  )

  const general = (
    <div className="settings-pane">
      <Divider titlePlacement="start">{t('settings.watermark')}</Divider>
      <SettingRow label={t('settings.watermark')}>
        <Switch size="small" checked={watermark} onChange={(v) => setSetting('watermark', v)} />
      </SettingRow>
      {watermark ? (
        <SettingRow label={t('settings.watermarkText')}>
          <Input
            size="small"
            style={{ width: 140 }}
            value={watermarkText}
            placeholder={t('settings.watermarkPlaceholder')}
            onChange={(e) => setSetting('watermarkText', e.target.value)}
          />
        </SettingRow>
      ) : null}

      <Divider titlePlacement="start">{t('settings.formStyle')}</Divider>
      {/* FormContainer 全局形态:所有 CRUD 表单弹层一键在弹窗/抽屉间切换 */}
      <Segmented
        block
        value={formStyle}
        onChange={(v) => setSetting('formStyle', v as FormStyle)}
        options={[
          { value: 'modal', label: t('settings.formStyleModal') },
          { value: 'drawer', label: t('settings.formStyleDrawer') },
        ]}
      />
    </div>
  )

  return (
    <Drawer
      title={t('settings.title')}
      placement="right"
      open={open}
      onClose={onClose}
      styles={{ section: { width: 'min(400px, 90vw)' } }}
      footer={
        <div className="settings-footer">
          {showCopyConfig ? (
            <Button block icon={<CopyOutlined />} onClick={copyConfig}>
              {t('settings.copyConfig')}
            </Button>
          ) : null}
          <Popconfirm
            title={t('settings.resetConfirm')}
            onConfirm={resetSettings}
            okText={t('common.confirm')}
            cancelText={t('common.cancel')}
          >
            <Button block icon={<ReloadOutlined />}>
              {t('common.reset')}
            </Button>
          </Popconfirm>
        </div>
      }
    >
      <Tabs
        centered
        items={[
          { key: 'appearance', label: t('settings.tabAppearance'), children: appearance },
          { key: 'layout', label: t('settings.tabLayout'), children: layout },
          { key: 'general', label: t('settings.tabGeneral'), children: general },
        ]}
      />
    </Drawer>
  )
}
