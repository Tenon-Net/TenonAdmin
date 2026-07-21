import { useState, type ReactNode } from 'react'
import { Button, Drawer, Modal, Space } from 'antd'
import { useTranslation } from 'react-i18next'
import { useAppStore, type FormStyle } from '@/stores/app'

/**
 * onConfirm 协议:跑完 onConfirm 后**是否关闭**。抽成纯函数,变异钉住 `!== false` 与 catch→false 两处判定。
 *   - 无 onConfirm → 直接关(true)
 *   - onConfirm resolve(false) → 不关(表单校验放 onConfirm 首行,失败 `return false` 即挡住关闭)
 *   - onConfirm 抛错 → 不关(false;API 错误 toast 由业务 onConfirm 内负责)
 *   - 其余(含 resolve(undefined)/resolve(true))→ 关
 */
export async function shouldCloseAfterConfirm(onConfirm?: () => unknown | Promise<unknown>): Promise<boolean> {
  if (!onConfirm) return true
  try {
    return (await onConfirm()) !== false
  } catch {
    return false
  }
}

export interface FormContainerProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  /** 不传 → 跟随 app.formStyle 全局偏好(设置抽屉可切) */
  variant?: FormStyle
  /** 实际生效 min(width, 90vw) */
  width?: number
  /** 见 shouldCloseAfterConfirm 协议;不传则确认钮直接关闭 */
  onConfirm?: () => unknown | Promise<unknown>
  confirmText?: string
  cancelText?: string
  /** 默认 false:表单防误触丢输入 */
  maskClosable?: boolean
  children: ReactNode
}

/**
 * 弹窗/抽屉二合一表单容器:消化 CRUD 页「saving state + 手写 footer + Modal 包装」样板。
 * 形态默认跟随全局偏好(app.formStyle),variant 按实例覆盖。**onConfirm owns loading + close**:
 * 确认钮自动 loading,提交中禁止一切关闭途径(esc/遮罩/关闭钮/取消钮),防中途关闭致状态错乱。
 * 对齐 Vue 侧 FormContainer;keep-alive 页签切走强制收起那一段留到 D1(标签页容器落地时)。
 */
export function FormContainer({
  open, onOpenChange, title, variant, width, onConfirm, confirmText, cancelText, maskClosable, children,
}: FormContainerProps) {
  const { t } = useTranslation()
  const mode = useAppStore((s) => variant ?? s.formStyle)
  const [loading, setLoading] = useState(false)

  const close = () => { if (!loading) onOpenChange(false) }
  async function handleConfirm() {
    setLoading(true)
    try {
      if (await shouldCloseAfterConfirm(onConfirm)) onOpenChange(false)
    } finally {
      setLoading(false)
    }
  }

  // 窄屏不溢出:min(width, 90vw)。表单弹层极少需要跟随实时 resize,渲染时读一次即可。
  const vw = typeof window !== 'undefined' ? window.innerWidth : 0
  const w = vw ? Math.min(width ?? 560, Math.round(vw * 0.9)) : (width ?? 560)
  const maskClose = (maskClosable ?? false) && !loading

  if (mode === 'modal') {
    return (
      <Modal
        open={open}
        title={title}
        width={w}
        confirmLoading={loading}
        okText={confirmText ?? t('common.confirm')}
        cancelText={cancelText ?? t('common.cancel')}
        onOk={handleConfirm}
        onCancel={close}
        mask={{ closable: maskClose }}
        keyboard={!loading}
        closable={!loading}
        cancelButtonProps={{ disabled: loading }}
        destroyOnHidden
      >
        {children}
      </Modal>
    )
  }
  return (
    <Drawer
      open={open}
      title={title}
      size={w}
      onClose={close}
      mask={{ closable: maskClose }}
      keyboard={!loading}
      closable={!loading}
      destroyOnHidden
      footer={
        <Space style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <Button disabled={loading} onClick={close}>
            {cancelText ?? t('common.cancel')}
          </Button>
          <Button type="primary" loading={loading} onClick={handleConfirm}>
            {confirmText ?? t('common.confirm')}
          </Button>
        </Space>
      }
    >
      {children}
    </Drawer>
  )
}
