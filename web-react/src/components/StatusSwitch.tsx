import { useState } from 'react'
import { App, Switch } from 'antd'
import { useTranslation } from 'react-i18next'
import { useConfirm } from '@/hooks/useConfirm'
import { translateError } from '@/utils/error'

export interface StatusSwitchProps {
  value: boolean
  /** 更新请求;resolve 即成功 */
  request: (next: boolean) => Promise<unknown>
  /** 提供则切换前先二次确认;函数形态按目标态出文案,返回空值(null/'')则本次跳过确认 */
  confirm?: string | ((next: boolean) => string | null | undefined)
  /** 无权限等场景由业务置灰(行内比移除 DOM 更合适) */
  disabled?: boolean
  size?: 'small' | 'default'
  /** false = 成功不弹 toast */
  successMsg?: string | false
  /** 成功后回调(父据此重拉/更新行);失败不调 = 值从未变 = 零成本回滚 */
  onChange?: (next: boolean) => void
}

/**
 * 表格行内启停开关:**悲观更新** —— checked 绑父传 value,点击不先翻转,请求成功才 onChange;
 * 失败不 onChange = UI 保持原值即回滚,没有「闪一下又弹回」;loading 期间自锁防连点。
 * 对齐 Vue 侧 StatusSwitch。二次确认建在 useConfirm 的 `ask` 上(仅确认不执行,自管后续)。
 */
export function StatusSwitch({
  value, request, confirm, disabled, size = 'small', successMsg, onChange,
}: StatusSwitchProps) {
  const { t } = useTranslation()
  const { ask } = useConfirm()
  const { message } = App.useApp()
  const [loading, setLoading] = useState(false)

  async function onSwitch(next: boolean) {
    if (confirm) {
      const content = typeof confirm === 'function' ? confirm(next) : confirm
      if (content && !(await ask({ content }))) return
    }
    setLoading(true)
    try {
      await request(next)
      onChange?.(next)
      if (successMsg !== false) message.success(successMsg ?? t('common.success'))
    } catch (e) {
      message.error(translateError(e)) // 不 onChange → 值从未变过,零成本回滚
    } finally {
      setLoading(false)
    }
  }

  return <Switch checked={value} loading={loading} disabled={disabled || loading} size={size} onChange={onSwitch} />
}
