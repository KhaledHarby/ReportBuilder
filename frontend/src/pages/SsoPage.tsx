import { useEffect, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { homePath, safeReturnPath } from '../auth/homePath'
import { useAuth } from '../auth/AuthProvider'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'
import { usePageTitle } from '../lib/pageTitle'

export function SsoPage() {
  const { ready, acceptSession } = useAuth()
  const { dir, t } = useLocale()
  usePageTitle(t('login'))
  const navigate = useNavigate()
  const [search] = useSearchParams()
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!ready) {
      return
    }

    const code = search.get('code')?.trim()
    if (!code) {
      setError(t('ssoMissingCode'))
      return
    }

    let cancelled = false
    void (async () => {
      try {
        const session = await api.exchangeSso(code)
        if (cancelled) {
          return
        }
        acceptSession(session)
        navigate(
          safeReturnPath(search.get('return')) ??
            homePath((permission) => session.user.permissions.includes(permission)),
          { replace: true },
        )
      } catch (err) {
        if (!cancelled) {
          setError((err as Error).message || t('ssoFailed'))
        }
      }
    })()

    return () => {
      cancelled = true
    }
  }, [acceptSession, navigate, ready, search, t])

  return (
    <div className="loginPage ssoPage" dir={dir}>
      <div className="login-shell sso-shell">
        <div className="login-panel">
          <div className="login-panel__header">
            <h1>{error ? t('ssoFailed') : t('ssoSigningIn')}</h1>
            <p>{error ?? t('loginContinue')}</p>
          </div>
          {error ? (
            <Link className="login-btn" to="/login">
              {t('login')}
            </Link>
          ) : (
            <Spinner label={t('ssoSigningIn')} />
          )}
        </div>
      </div>
    </div>
  )
}
