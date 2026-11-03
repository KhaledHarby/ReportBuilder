import { Navigate, Outlet } from 'react-router-dom'
import type { ReactNode } from 'react'
import { Spinner } from '../components/Spinner'
import { useAuth } from './AuthProvider'
import { useLocale } from '../i18n/LocaleProvider'

export function RequireAuth() {
  const { user, ready } = useAuth()
  const { t } = useLocale()

  if (!ready) {
    return <Spinner fullscreen label={t('loadingApp')} />
  }

  if (!user) {
    return <Navigate to="/login" replace />
  }

  return <Outlet />
}

export function RequirePerm({
  permission,
  children,
}: {
  permission: string
  children: ReactNode
}) {
  const { can } = useAuth()
  const { t } = useLocale()
  if (!can(permission)) {
    return (
      <section className="page">
        <header className="page-header">
          <div>
            <div className="kicker">{t('accessKicker')}</div>
            <h1>{t('restrictedTitle')}</h1>
            <p className="lede">{t('restrictedLede', { permission })}</p>
          </div>
        </header>
      </section>
    )
  }
  return children
}
