import { t } from '@/locales'

/** 后端菜单配置的组件不存在时,保留原路由并给出可见诊断。 */
export function MissingRoute({ component }: { component: string }) {
  return <div role="alert">{t('notFound.component', { component })}</div>
}
