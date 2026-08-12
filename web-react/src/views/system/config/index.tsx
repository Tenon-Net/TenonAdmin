// 分类配置中心:按分类 Tab 组织。base/security/upload = 结构化表单(字段级 UI,运维不需知道 config key);
// other = 任意 key 扁平 CRUD 兜底。对齐 Vue 侧 config/index.vue。
// antd Tabs 默认 destroyOnHidden=false → 面板懒挂(首访才 mount+拉数据)+ 保活(切走只隐藏不销毁),
// 各面板未保存改动跨 Tab 切换存活,正合 Vue 的 display-directive="show:lazy"。
// 面板内不再套「与 Tab 标签同名的 Card 标题」(Vue 那层 n-card 标题与 tab 名重复),页级一个 Card 收边。
import { useMemo } from 'react'
import { Card, Tabs } from 'antd'
import { useTranslation } from 'react-i18next'
import SysBaseConfig from './SysBaseConfig'
import SecurityConfig from './SecurityConfig'
import ExternalAuthConfig from './ExternalAuthConfig'
import UploadConfig from './UploadConfig'
import JobConfig from './JobConfig'
import OtherConfig from './OtherConfig'

export default function ConfigPage() {
  const { t } = useTranslation()
  const items = useMemo(
    () => [
      { key: 'base', label: t('config.tab.base'), children: <SysBaseConfig /> },
      { key: 'security', label: t('config.tab.security'), children: <SecurityConfig /> },
      { key: 'externalAuth', label: t('config.tab.externalAuth'), children: <ExternalAuthConfig /> },
      { key: 'upload', label: t('config.tab.upload'), children: <UploadConfig /> },
      { key: 'job', label: t('config.tab.job'), children: <JobConfig /> },
      { key: 'other', label: t('config.tab.other'), children: <OtherConfig /> },
    ],
    [t],
  )
  return (
    <Card>
      <Tabs type="line" items={items} />
    </Card>
  )
}
