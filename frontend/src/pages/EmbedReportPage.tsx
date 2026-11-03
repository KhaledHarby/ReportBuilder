import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthProvider'
import { getAccessToken } from '../auth/session'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'
import { ReportViewPage } from './ReportViewPage'

export function EmbedReportPage() {
  const [search, setSearch] = useSearchParams()
  const { ready, user, acceptSession } = useAuth()
  const { t, setLang } = useLocale()
  const code = search.get('code')?.trim() ?? ''
  const [phase, setPhase] = useState<'pending' | 'ready' | 'failed'>(code ? 'pending' : 'ready')
  const [message, setMessage] = useState<string | null>(null)

  useEffect(() => {
    const lang = search.get('lang')
    if (lang === 'ar' || lang === 'en') {
      setLang(lang)
    }
  }, [search, setLang])

  useEffect(() => {
    if (!ready) {
      return
    }
    if (!code) {
      if (!getAccessToken()) {
        setMessage(t('ssoFailed'))
        setPhase('failed')
      } else {
        setPhase('ready')
      }
      return
    }

    let cancelled = false
    void (async () => {
      try {
        const session = await api.exchangeSso(code)
        acceptSession(session)
        if (cancelled) {
          return
        }
        setSearch(
          (current) => {
            const next = new URLSearchParams(current)
            next.delete('code')
            return next
          },
          { replace: true },
        )
        setPhase('ready')
      } catch (err) {
        if (cancelled) {
          return
        }
        if (getAccessToken()) {
          setSearch(
            (current) => {
              const next = new URLSearchParams(current)
              next.delete('code')
              return next
            },
            { replace: true },
          )
          setPhase('ready')
          return
        }
        setMessage((err as Error).message || t('ssoFailed'))
        setPhase('failed')
      }
    })()

    return () => {
      cancelled = true
    }
  }, [acceptSession, code, ready, setSearch, t])

  if (!ready || phase === 'pending' || (phase === 'ready' && !user)) {
    if (phase === 'failed') {
      return (
        <section className="embed-report">
          <p className="banner error">{message || t('ssoFailed')}</p>
        </section>
      )
    }
    return (
      <section className="embed-report">
        <Spinner label={t('ssoSigningIn')} />
      </section>
    )
  }

  if (phase === 'failed') {
    return (
      <section className="embed-report">
        <p className="banner error">{message || t('ssoFailed')}</p>
      </section>
    )
  }

  return <ReportViewPage embedded />
}
