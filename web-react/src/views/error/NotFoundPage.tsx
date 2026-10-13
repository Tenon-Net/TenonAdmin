import { Button, Result } from 'antd'
import { useLocation, useNavigate } from 'react-router-dom'
import { t } from '@/locales'
import { isKnownPagePath } from '@/router/knownPage'

/** 布局壳内的未知路由。真实页面未授权时显示 403，其余仍是 404。 */
export default function NotFoundPage() {
  const navigate = useNavigate()
  const denied = isKnownPagePath(useLocation().pathname)
  return (
    <div style={{ display: 'grid', minHeight: 320, placeItems: 'center', padding: 24 }}>
      <Result
        status={denied ? '403' : '404'}
        title={denied ? '403' : '404'}
        subTitle={denied ? t('forbidden.desc') : t('notFound.desc')}
        extra={<Button type="primary" onClick={() => navigate('/', { replace: true })}>{t('notFound.back')}</Button>}
      />
    </div>
  )
}
