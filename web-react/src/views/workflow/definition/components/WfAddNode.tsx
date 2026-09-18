// 节点间大圆加号;Popover 提供当前可插入节点。对应 Vue 侧 WfAddNode.vue。
import { useState } from 'react'
import { Popover } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import type { WfInsertableNodeType } from '@/workflow/schema'
import './wf-designer.css'
import '../../wf-identity.css'

const MENU: ReadonlyArray<{ type: WfInsertableNodeType; icon: string }> = [
  { type: 'approval', icon: 'ph:user-circle-check' },
  { type: 'cc', icon: 'ph:paper-plane-tilt' },
  { type: 'branch', icon: 'ph:git-branch' },
  { type: 'parallel', icon: 'ph:git-merge' },
  { type: 'webhook', icon: 'ph:webhooks-logo' },
]

export interface WfAddNodeProps {
  /**
   * 默认展示并行;仅并行臂内由父链显式传 false。Vue 侧曾因布尔缺省吞掉菜单项而整条并行入口消失,
   * 这里同样**必须**默认 true——调用方漏传不能退化成禁用。
   */
  allowParallel?: boolean
  onAdd: (type: WfInsertableNodeType) => void
}

export function WfAddNode({ allowParallel = true, onAdd }: WfAddNodeProps) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)

  const pick = (type: WfInsertableNodeType) => {
    setOpen(false)
    onAdd(type)
  }

  const content = (
    <div className="wf-add-grid">
      {MENU.filter((item) => item.type !== 'parallel' || allowParallel !== false).map((item) => (
        <button key={item.type} type="button" className="wf-add-item" onClick={() => pick(item.type)}>
          <span className={`wf-add-icon is-${item.type}`}><AppIcon icon={item.icon} size={22} /></span>
          <span className="wf-add-label">{t(`workflow.node.${item.type}`)}</span>
        </button>
      ))}
    </div>
  )

  return (
    <div className="wf-add">
      <Popover open={open} onOpenChange={setOpen} trigger="click" placement="rightTop" arrow={false} content={content}>
        <button type="button" className="wf-add-btn" aria-label={t('workflow.designer.addNode')} title={t('workflow.designer.addNode')}>
          <AppIcon icon="ph:plus-bold" size={16} />
        </button>
      </Popover>
    </div>
  )
}
