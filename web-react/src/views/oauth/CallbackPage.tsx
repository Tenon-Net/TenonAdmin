import { useEffect, useRef, useState } from 'react'
import { Spin } from 'antd'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { externalAuthApi } from '@/api'
import { useUserStore } from '@/stores/user'
import { t } from '@/locales'
import { translateError } from '@/utils/error'

const OAUTH_ERROR_KEYS: Record<number, string> = {
  40013: 'error.auth.oauthProviderDisabled',
  40014: 'error.auth.oauthStateInvalid',
  40015: 'error.auth.oauthExchangeFailed',
  40016: 'error.auth.oauthAccountNotBound',
  40017: 'error.auth.oauthAlreadyBound',
}

/** 公开 IdP 回调:兑换一次性票据,或显示简短的本地化错误。 */
export default function CallbackPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [errorText, setErrorText] = useState<string | null>(null)
  const started = useRef(false)

  useEffect(() => {
    if (started.current) return
    started.current = true

    const ticket = searchParams.get('ticket') ?? ''
    const bind = searchParams.get('bind') ?? ''
    const error = searchParams.get('error') ?? ''
    const fail = (text: string) => setErrorText(text)

    if (error) {
      const key = OAUTH_ERROR_KEYS[Number(error)]
      fail(key ? t(key) : t('oauth.failed'))
    } else if (bind) {
      navigate('/personal/bindings', { replace: true })
    } else if (ticket) {
      externalAuthApi.exchange(ticket)
        .then((session) => {
          useUserStore.getState().setSession(session)
          navigate('/', { replace: true })
        })
        .catch((err: unknown) => fail(translateError(err)))
    } else {
      fail(t('oauth.failed'))
    }

  }, [navigate, searchParams])

  useEffect(() => {
    if (!errorText) return
    const timer = window.setTimeout(() => navigate('/login', { replace: true }), 2600)
    return () => window.clearTimeout(timer)
  }, [errorText, navigate])

  return (
    <div style={{ display: 'grid', minHeight: '100vh', placeItems: 'center', padding: 24 }}>
      {errorText ? (
        <div style={{ textAlign: 'center' }}>
          <p>{errorText}</p>
          <p style={{ color: 'var(--color-text-tertiary)' }}>{t('oauth.backToLogin')}</p>
        </div>
      ) : <Spin size="large" description={t('oauth.processing')} />}
    </div>
  )
}
