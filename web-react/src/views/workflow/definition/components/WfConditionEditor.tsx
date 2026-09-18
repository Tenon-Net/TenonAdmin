// 分支条件编辑器:组(logic + children)与叶子(field/op/value)递归同构。对应 Vue 侧 WfConditionEditor.vue。
import { useMemo, useState } from 'react'
import { Collapse, Input, InputNumber, Select } from 'antd'
import { useTranslation } from 'react-i18next'
import {
  WF_CONDITION_OPERATOR_META,
  appendConditionChild,
  classifyConditionOp,
  createConditionGroup,
  createConditionLeaf,
  isWfConditionOp,
  removeConditionChild,
  replaceConditionChild,
  setConditionOp,
} from '@/workflow/configuration'
import type { WfConditionExpr, WfConditionLogic, WfConditionOp } from '@/workflow/schema'
import './wf-designer.css'

export interface WfConditionEditorProps {
  value: WfConditionExpr
  onChange: (value: WfConditionExpr) => void
  /** 递归深度;仅决定最外层是否默认展开第一个子项。 */
  depth?: number
}

export function WfConditionEditor({ value, onChange, depth = 0 }: WfConditionEditorProps) {
  const { t } = useTranslation()
  const [activeKey, setActiveKey] = useState<string | undefined>(depth === 0 ? 'child-0' : undefined)

  const isGroup = value.children != null
  const currentOp: WfConditionOp = value.op ?? 'eq'
  const valueKind = classifyConditionOp(currentOp)

  const logicOptions = useMemo(
    () => (['and', 'or'] as WfConditionLogic[]).map((v) => ({ label: t(`workflow.condition.logic.${v}`), value: v })),
    [t],
  )
  const opOptions = useMemo(
    () => WF_CONDITION_OPERATOR_META.map(({ op }) => ({ label: t(`workflow.condition.op.${op}`), value: op })),
    [t],
  )

  const childSummary = (child: WfConditionExpr) =>
    child.children != null
      ? t(`workflow.condition.logic.${child.logic ?? 'and'}`)
      : child.field || t('workflow.condition.addCondition')

  const addChild = (child: WfConditionExpr) => {
    const next = appendConditionChild(value, child)
    if (next) onChange(next)
  }

  const updateChild = (index: number, child: WfConditionExpr) => {
    const next = replaceConditionChild(value, index, child)
    if (next) onChange(next)
  }

  const removeChild = (index: number) => {
    const next = removeConditionChild(value, index)
    if (next) onChange(next)
  }

  if (isGroup) {
    const children = value.children ?? []
    return (
      <div className="wf-condition is-group">
        <div className="wf-condition-toolbar">
          <Select
            className="wf-logic-select"
            size="small"
            value={value.logic ?? 'and'}
            options={logicOptions}
            aria-label={t('workflow.condition.logicLabel')}
            onChange={(logic) => onChange({ ...value, logic })}
          />
          <div className="wf-condition-actions">
            <button
              type="button" className="wf-condition-action"
              title={t('workflow.condition.addCondition')} aria-label={t('workflow.condition.addCondition')}
              onClick={() => addChild(createConditionLeaf())}
            >
              {t('workflow.condition.addCondition')}
            </button>
            <button
              type="button" className="wf-condition-action"
              title={t('workflow.condition.addGroup')} aria-label={t('workflow.condition.addGroup')}
              onClick={() => addChild(createConditionGroup())}
            >
              {t('workflow.condition.addGroup')}
            </button>
          </div>
        </div>

        {children.length ? (
          <Collapse
            accordion
            ghost
            className="wf-condition-children"
            data-depth={depth}
            activeKey={activeKey}
            onChange={(keys) => setActiveKey(Array.isArray(keys) ? keys[0] : keys)}
            items={children.map((child, index) => ({
              // 索引键:条件项无稳定 Id,而增删只发生在末尾/单项,受控输入不会丢值。
              key: `child-${index}`,
              label: <span className="wf-condition-child-summary">{childSummary(child)}</span>,
              children: (
                <div className="wf-condition-child">
                  <WfConditionEditor
                    value={child}
                    depth={depth + 1}
                    onChange={(next) => updateChild(index, next)}
                  />
                  <button
                    type="button" className="wf-condition-delete"
                    title={t('common.delete')} aria-label={t('common.delete')}
                    onClick={() => removeChild(index)}
                  >
                    ×
                  </button>
                </div>
              ),
            }))}
          />
        ) : (
          <div className="wf-condition-empty">{t('workflow.condition.emptyGroup')}</div>
        )}
      </div>
    )
  }

  return (
    <div className="wf-condition">
      <div className="wf-condition-leaf">
        <Input
          size="small"
          value={value.field ?? ''}
          placeholder={t('workflow.condition.field')}
          aria-label={t('workflow.condition.field')}
          onChange={(event) => onChange({ ...value, field: event.target.value })}
        />
        <Select
          size="small"
          value={currentOp}
          options={opOptions}
          aria-label={t('workflow.condition.operator')}
          onChange={(op) => {
            if (isWfConditionOp(op)) onChange(setConditionOp(value, op))
          }}
        />
        {valueKind === 'number' ? (
          <InputNumber
            className="wf-condition-value"
            size="small"
            value={typeof value.value === 'number' ? value.value : 0}
            placeholder={t('workflow.condition.value')}
            aria-label={t('workflow.condition.value')}
            onChange={(next) => onChange({ ...value, value: next ?? 0 })}
          />
        ) : valueKind === 'list' ? (
          // NDynamicTags 的 antd 等价:mode="tags" 自由输入(回车/逗号成标签)。
          <Select
            className="wf-condition-value"
            size="small"
            mode="tags"
            tokenSeparators={[',']}
            suffixIcon={null}
            notFoundContent={null}
            value={Array.isArray(value.value) ? value.value.map(String) : []}
            placeholder={t('workflow.condition.value')}
            aria-label={t('workflow.condition.value')}
            onChange={(next: string[]) => onChange({ ...value, value: next })}
          />
        ) : valueKind === 'none' ? (
          <span className="wf-condition-no-value">{t('workflow.condition.noValue')}</span>
        ) : (
          <Input
            className="wf-condition-value"
            size="small"
            value={typeof value.value === 'string' ? value.value : ''}
            placeholder={t('workflow.condition.value')}
            aria-label={t('workflow.condition.value')}
            onChange={(event) => onChange({ ...value, value: event.target.value })}
          />
        )}
      </div>
    </div>
  )
}
