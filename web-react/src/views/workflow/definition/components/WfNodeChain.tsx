// 单条局部链的递归展示层:只上抛稳定 Id/节点类型/名称,不修改传入模型。对应 Vue 侧 WfNodeChain.vue。
import type { FocusEvent, KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import type { WfInsertableNodeType, WfNode } from '@/workflow/schema'
import { WfAddNode } from './WfAddNode'
import { WfNodeCard } from './WfNodeCard'
import './wf-designer.css'
import '../../wf-identity.css'

/** 分支臂与并行臂的显示交集(并行臂没有 isDefault)。 */
interface WfDisplayArm {
  id: string
  name: string
  next?: WfNode | null
  isDefault?: boolean
}

export interface WfNodeChainProps {
  root?: WfNode | null
  selectedId?: string | null
  errorSet: Set<string>
  terminal?: boolean
  readonly?: boolean
  visitedSet?: Set<string>
  currentSet?: Set<string>
  /** 设计器默认可插并行;并行臂内递归显式传 false 禁止嵌套。 */
  allowParallel?: boolean
  onSelect: (nodeId: string) => void
  onAddAfter: (afterId: string, type: WfInsertableNodeType) => void
  onAddAtArmHead: (ownerId: string, armId: string, type: WfInsertableNodeType) => void
  onRemoveNode: (nodeId: string) => void
  onAddArm: (ownerId: string) => void
  onRemoveArm: (ownerId: string, armId: string) => void
  onRenameArm: (ownerId: string, armId: string, name: string) => void
}

/** 本组件只展开当前局部 next 链;分支臂由下面递归渲染,避免把臂节点重复画进主链。 */
function chainOf(root: WfNode | null | undefined): WfNode[] {
  const nodes: WfNode[] = []
  let current = root ?? null
  while (current) {
    nodes.push(current)
    current = current.next ?? null
  }
  return nodes
}

function armsFor(node: WfNode): WfDisplayArm[] {
  if (node.type === 'branch') return node.conditions ?? []
  if (node.type === 'parallel') return node.parallelArms ?? []
  return []
}

export function WfNodeChain(props: WfNodeChainProps) {
  const {
    root, selectedId, errorSet, terminal, readonly, visitedSet, currentSet, allowParallel = true,
    onSelect, onAddAfter, onAddAtArmHead, onRemoveNode, onAddArm, onRemoveArm, onRenameArm,
  } = props
  const { t } = useTranslation()

  return (
    <div className="wf-chain">
      {chainOf(root).map((node) => {
        const arms = armsFor(node)
        const isOwner = node.type === 'branch' || node.type === 'parallel'
        // 并行臂内禁止再嵌套并行;非并行节点沿用父链的许可。
        const armAllowParallel = node.type !== 'parallel' && allowParallel
        const addArmKey = node.type === 'parallel' ? 'workflow.designer.addParallelArm' : 'workflow.designer.addArm'

        return (
          <div key={node.id} className="wf-chain-node">
            <WfNodeCard
              node={node}
              active={selectedId === node.id}
              error={errorSet.has(node.id)}
              readonly={readonly}
              visited={visitedSet?.has(node.id) ?? false}
              current={currentSet?.has(node.id) ?? false}
              onSelect={() => onSelect(node.id)}
              onRemove={() => onRemoveNode(node.id)}
            />

            {isOwner ? (
              <section
                className={`wf-branch${node.type === 'parallel' ? ' wf-parallel' : ''}`}
                aria-label={t(`workflow.node.${node.type}`)}
              >
                <div className="wf-branch-toolbar">
                  <span>
                    {node.type === 'parallel'
                      ? t('workflow.designer.parallelArmCount', { count: arms.length })
                      : t('workflow.designer.armCount', { count: arms.length })}
                  </span>
                  {!readonly ? (
                    <button
                      type="button"
                      className="wf-arm-action wf-arm-add"
                      aria-label={t(addArmKey)}
                      onClick={() => onAddArm(node.id)}
                    >
                      <AppIcon icon="ph:plus-bold" size={13} />
                      <span>{t(addArmKey)}</span>
                    </button>
                  ) : null}
                </div>

                <div className="wf-branch-arms">
                  {arms.map((arm) => (
                    <article key={arm.id} className="wf-arm">
                      <header className="wf-arm-head">
                        {!readonly ? (
                          <ArmNameInput
                            armId={arm.id}
                            name={arm.name}
                            placeholder={t('workflow.designer.armName')}
                            onRename={(name) => onRenameArm(node.id, arm.id, name)}
                          />
                        ) : (
                          <span className="wf-arm-name">{arm.name || t('workflow.designer.armName')}</span>
                        )}
                        {arm.isDefault ? (
                          <span className="wf-arm-default">{t('common.isDefault')}</span>
                        ) : !readonly ? (
                          <button
                            type="button"
                            className="wf-arm-action wf-arm-remove"
                            aria-label={t('common.delete')}
                            title={t('common.delete')}
                            onClick={() => onRemoveArm(node.id, arm.id)}
                          >
                            <AppIcon icon="ph:x" size={13} />
                          </button>
                        ) : null}
                      </header>

                      <div className="wf-arm-body">
                        {!readonly ? (
                          <WfAddNode
                            allowParallel={armAllowParallel}
                            onAdd={(type) => onAddAtArmHead(node.id, arm.id, type)}
                          />
                        ) : null}
                        {arm.next ? (
                          <WfNodeChain
                            {...props}
                            root={arm.next}
                            allowParallel={armAllowParallel}
                            terminal={false}
                          />
                        ) : null}
                        <div className="wf-arm-merge">
                          <span className="wf-arm-merge-dot" />
                          <span>{t('workflow.designer.merge')}</span>
                        </div>
                      </div>
                    </article>
                  ))}
                </div>
              </section>
            ) : null}

            {!readonly ? (
              <WfAddNode allowParallel={allowParallel} onAdd={(type) => onAddAfter(node.id, type)} />
            ) : null}
          </div>
        )
      })}

      {terminal ? (
        <div className="wf-chain-terminal">
          <span className="wf-chain-terminal-dot" />
          <span>{t('workflow.designer.end')}</span>
        </div>
      ) : null}
    </div>
  )
}

interface ArmNameInputProps {
  armId: string
  name: string
  placeholder: string
  onRename: (name: string) => void
}

/**
 * 臂名输入:**非受控**。每次按键都上抛会让整棵树 clone + 重挂,输入焦点与光标随之丢失
 * (Vue 侧用的是 `@change`,同一语义),因此只在失焦与回车时结算。
 */
function ArmNameInput({ armId, name, placeholder, onRename }: ArmNameInputProps) {
  const commit = (event: FocusEvent<HTMLInputElement> | KeyboardEvent<HTMLInputElement>) => {
    const value = event.currentTarget.value
    if (value !== name) onRename(value)
  }
  return (
    <input
      // armId 作 key:重命名后模型换了新对象但 key 不变,不会把用户正在编辑的输入重挂回旧值。
      key={armId}
      className="wf-arm-name"
      type="text"
      defaultValue={name}
      placeholder={placeholder}
      aria-label={placeholder}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === 'Enter') commit(event)
      }}
    />
  )
}
