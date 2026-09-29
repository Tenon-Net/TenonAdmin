// 一次性凭据展示:完整凭据只在发放/轮换的这一次响应里出现。它只活在本组件的局部 state 里,
// 关闭即清空;不写 zustand / localStorage / sessionStorage,不打任何日志。
// 既没复制、也没点「我已妥善保存」就要关(Esc / 关闭钮 / 取消)时先二次确认,免得凭据就此丢失。
// 本模板用 BrowserRouter,拦不住离开本页的导航;缓存页切走只是隐藏,而弹窗挂在 body 上会盖到别的页面,
// 所以不在打开它的页面时先收起、凭据留在内存,切回来再显示。
import { forwardRef, useImperativeHandle, useRef, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { Alert, App, Button, Input } from 'antd'
import { useTranslation } from 'react-i18next'
import { FormContainer } from '@/components/FormContainer'
import { useConfirm } from '@/hooks/useConfirm'
import type { OpenAppCredentialIssued } from '@/types/integration'

export interface SecretModalHandle {
  open: (issued: OpenAppCredentialIssued) => void
}

export const SecretModal = forwardRef<SecretModalHandle>(function SecretModal(_, ref) {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { ask } = useConfirm()
  const { pathname } = useLocation()
  const [open, setOpen] = useState(false)
  const [openedOn, setOpenedOn] = useState<string | null>(null)
  const [apiKey, setApiKey] = useState('')
  const settled = useRef(false)
  const asking = useRef(false)

  useImperativeHandle(ref, () => ({
    open: (issued) => {
      settled.current = false
      setApiKey(issued.apiKey ?? '')
      setOpenedOn(pathname)
      setOpen(true)
    },
  }), [pathname])

  /** 容器的关闭请求都经这里(open 受控,不改就不会关):未复制/未确认保存先问一句,确认了才真关。 */
  const onOpenChange = async (next: boolean) => {
    if (!next && !settled.current) {
      if (asking.current) return
      asking.current = true
      try {
        if (!(await ask({ content: t('integration.credential.secretCloseConfirm') }))) return
      } finally {
        asking.current = false
      }
    }
    setOpen(next)
    if (!next) setApiKey('')   // 关闭即丢弃原文
  }

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(apiKey)
      settled.current = true
      message.success(t('integration.credential.copied'))
    } catch {
      message.error(t('integration.credential.copyFailed'))
    }
  }

  return (
    <FormContainer
      open={open && pathname === openedOn}
      onOpenChange={(next) => void onOpenChange(next)}
      variant="modal"
      centered
      title={t('integration.credential.secretTitle')}
      width={600}
      onConfirm={() => { settled.current = true }}
      confirmText={t('integration.credential.secretAck')}
    >
      <Alert type="warning" showIcon={false} title={t('integration.credential.secretWarning')} style={{ marginBottom: 12 }} />
      <Input
        readOnly
        value={apiKey}
        data-testid="integration-secret"
        style={{ fontFamily: 'var(--font-mono, ui-monospace, monospace)' }}
        suffix={<Button type="link" size="small" onClick={() => void copy()}>{t('integration.credential.copy')}</Button>}
      />
      <p style={{ margin: '8px 0 0', fontSize: 13, color: 'var(--color-text-secondary)' }}>{t('integration.credential.usageHint')}</p>
    </FormContainer>
  )
})
