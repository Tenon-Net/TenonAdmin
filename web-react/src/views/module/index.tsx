import { useState } from 'react'
import { App, Button, Card, Empty, Spin, Tag, Typography } from 'antd'
import { ArrowLeftOutlined } from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '@/stores/auth'
import { useUserStore } from '@/stores/user'
import { switchModule, setDefault } from '@/composables/useModule'
import { AppIcon } from '@/components/AppIcon'
import { authApi } from '@/api'
import { beginVoluntaryLogout } from '@/composables/useRealtime'
import { translateError } from '@/utils/error'

/**
 * 应用选择页(门户)。决策阶梯(直接进/进默认/弹这里)在 `useModule.enterInitial`,这里只管**呈现与切换**。
 * 对应 Vue 侧 `views/module/index.vue`。
 *
 * 图标:`module.icon` 是 iconify 字符串,经 AppIcon 离线渲染;兜底 `ph:app-window-duotone`(与 Vue 版一致)。
 */
export default function ModuleChooser() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const modules = useAuthStore((s) => s.modules)
  const currentModuleId = useAuthStore((s) => s.currentModuleId)
  const defaultModuleId = useAuthStore((s) => s.defaultModuleId)
  const routesReady = useAuthStore((s) => s.routesReady)
  const [busy, setBusy] = useState(false)

  // 三个异步动作共用 `busy` 门:任一进行中就不接第二次点击(快连点、进应用途中又点设默认…),
  // 并统一驱动 `Spin`。设默认/登出以前没这道门,rapid click 会重复发。
  async function pick(id: number) {
    if (busy) return
    setBusy(true)
    try {
      // switchModule = 建路由 + (D1)清标签 + 返回新应用首页,这里负责导航(useModule 无 router 上下文)。
      const home = await switchModule(id)
      navigate(home, { replace: true })
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setBusy(false)
    }
  }

  async function onSetDefault(id: number) {
    if (busy) return
    setBusy(true)
    try {
      await setDefault(id)
      message.success(t('common.success'))
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setBusy(false)
    }
  }

  async function logout() {
    if (busy) return
    setBusy(true)
    await beginVoluntaryLogout()
    try {
      await authApi.logout()
    } catch {
      // 后端登出失败不阻断前端清理:本地会话照清,跳登录。
    }
    useAuthStore.getState().reset()
    useUserStore.getState().clear()
    navigate('/login', { replace: true })
    // 不 setBusy(false):已经跳走,组件即将卸载。
  }

  return (
    <div style={{ minHeight: '100vh', padding: '80px 24px', display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 32 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('module.choose')}
        </Typography.Title>
        {/* 从应用内点九宫格进来的(路由已就绪)才给返回;登录直落选择页时无处可返。 */}
        {routesReady ? (
          <Button type="link" size="small" icon={<ArrowLeftOutlined />} onClick={() => navigate(-1)}>
            {t('module.back')}
          </Button>
        ) : null}
      </div>

      <Spin spinning={busy}>
        {modules.length === 0 ? (
          <Empty description={t('module.empty')} style={{ padding: '60px 0' }}>
            <Button size="small" onClick={() => void logout()}>
              {t('app.logout')}
            </Button>
          </Empty>
        ) : (
          <div
            style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 16, width: '100%', maxWidth: 900 }}
            data-testid="module-grid"
          >
            {modules.map((m) => (
              <Card
                key={m.id}
                hoverable
                data-testid={`module-card-${m.id}`}
                style={m.id === currentModuleId ? { borderColor: 'var(--color-primary)' } : undefined}
                onClick={() => void pick(m.id)}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => {
                  // 只认卡片**自己**被聚焦时的按键。内层「设为默认」按钮的 keydown 会冒泡到这里,
                  // 若不挡,卡片的 preventDefault 会压掉按钮原生的 Enter→click → 键盘用户设不了默认、反被带进应用。
                  // (鼠标路径靠按钮的 stopPropagation;键盘路径靠这条 target 守卫,两条各管一半。)
                  if (e.target !== e.currentTarget) return
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault()
                    void pick(m.id)
                  }
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
                  <AppIcon icon={m.icon} size={28} fallback="ph:app-window-duotone" style={{ color: 'var(--color-primary)' }} />
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ fontWeight: 600 }}>{m.title}</div>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {m.code}
                    </Typography.Text>
                  </div>
                  {m.id === defaultModuleId ? (
                    <Tag color="blue">
                      {t('module.isDefault')}
                    </Tag>
                  ) : (
                    <Button
                      type="link"
                      size="small"
                      // 阻止冒泡:卡片本身可点(进应用),设默认不该顺带把人带进去。
                      onClick={(e) => {
                        e.stopPropagation()
                        void onSetDefault(m.id)
                      }}
                    >
                      {t('module.setDefault')}
                    </Button>
                  )}
                </div>
              </Card>
            ))}
          </div>
        )}
      </Spin>
    </div>
  )
}
