import { useState } from 'react'
import { App, Button, Card, Empty, Spin, Tag, Typography } from 'antd'
import { AppstoreOutlined, ArrowLeftOutlined } from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '@/stores/auth'
import { useUserStore } from '@/stores/user'
import { switchModule, setDefault } from '@/composables/useModule'
import { authApi } from '@/api'
import { translateError } from '@/utils/error'

/**
 * 应用选择页(门户)。决策阶梯(直接进/进默认/弹这里)在 `useModule.enterInitial`,这里只管**呈现与切换**。
 * 对应 Vue 侧 `views/module/index.vue`。
 *
 * 图标:`module.icon` 是 iconify 字符串(Vue 用 @iconify)。本模板暂未引 iconify,统一用通用图标占位;
 * 图标映射(iconify → 组件)留后续批次,别当已做完。
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

  async function pick(id: number) {
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
    try {
      await setDefault(id)
      message.success(t('common.success'))
    } catch (e) {
      message.error(translateError(e))
    }
  }

  async function logout() {
    try {
      await authApi.logout()
    } catch {
      // 后端登出失败不阻断前端清理:本地会话照清,跳登录。
    }
    useAuthStore.getState().reset()
    useUserStore.getState().clear()
    navigate('/login', { replace: true })
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
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault()
                    void pick(m.id)
                  }
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
                  <AppstoreOutlined style={{ fontSize: 28, color: 'var(--color-primary)' }} />
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ fontWeight: 600 }}>{m.title}</div>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {m.code}
                    </Typography.Text>
                  </div>
                  {m.id === defaultModuleId ? (
                    <Tag color="blue" bordered={false}>
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
