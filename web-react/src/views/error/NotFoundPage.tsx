import { Button, Result } from 'antd'
import { useNavigate } from 'react-router-dom'
import { t } from '@/locales'

/** 布局壳内的未知路由,提供返回当前应用首页的直接入口。 */
export default function NotFoundPage() {
  const navigate = useNavigate()
  return (
    <div style={{ display: 'grid', minHeight: 320, placeItems: 'center', padding: 24 }}>
      <Result
        status="404"
        title="404"
        subTitle={t('notFound.desc')}
        extra={<Button type="primary" onClick={() => navigate('/', { replace: true })}>{t('notFound.back')}</Button>}
      />
    </div>
  )
}
