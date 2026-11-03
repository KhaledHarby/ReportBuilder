import { useEffect, useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthProvider'
import { homePath } from '../auth/homePath'
import { PasswordInput } from '../components/PasswordInput'
import { Spinner } from '../components/Spinner'
import { ThemePicker } from '../components/ThemePicker'
import {
  IconChart,
  IconColumns,
  IconGrid,
  IconLive,
  IconShield,
} from '../components/Icons'
import { LangToggle } from '../i18n/LangToggle'
import { useLocale } from '../i18n/LocaleProvider'
import type { MessageKey } from '../i18n/messages'
import skylinePhoto from '../assets/login/skyline-photo.png'

function formatClock(now: Date, lang: 'en' | 'ar') {
  const time = now.toLocaleTimeString('en-GB', {
    hour12: false,
    hour: '2-digit',
    minute: '2-digit',
  })
  const date = now.toLocaleDateString(lang === 'ar' ? 'ar-AE' : 'en-GB', {
    weekday: 'long',
    day: '2-digit',
    month: 'long',
  })
  return { time, date }
}

const FEATURES: Array<{
  icon: typeof IconGrid
  title: MessageKey
  body: MessageKey
}> = [
  { icon: IconColumns, title: 'login2FeatDesign', body: 'login2FeatDesignBody' },
  { icon: IconLive, title: 'login2FeatLive', body: 'login2FeatLiveBody' },
  { icon: IconGrid, title: 'login2FeatBilingual', body: 'login2FeatBilingualBody' },
  { icon: IconShield, title: 'login2FeatSecure', body: 'login2FeatSecureBody' },
]

export function Login2() {
  const { user, ready, login, can } = useAuth()
  const { lang, dir, t } = useLocale()
  const navigate = useNavigate()
  const [now, setNow] = useState(() => new Date())
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [forgotHint, setForgotHint] = useState(false)
  const [pending, setPending] = useState(false)
  const [portalUrl, setPortalUrl] = useState<string | null>(null)
  const clock = formatClock(now, lang)

  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 1000)
    return () => window.clearInterval(id)
  }, [])

  useEffect(() => {
    setForgotHint(false)
  }, [lang])

  useEffect(() => {
    let cancelled = false
    void api
      .getAuthOptions()
      .then((options) => {
        if (!cancelled) {
          setPortalUrl(options.externalLoginUrl || null)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setPortalUrl(null)
        }
      })
    return () => {
      cancelled = true
    }
  }, [])

  if (!ready) {
    return <Spinner fullscreen label={t('loadingApp')} />
  }

  if (ready && user) {
    return <Navigate to={homePath(can)} replace />
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setPending(true)
    setError(null)
    try {
      const account = await login(userName, password)
      navigate(
        homePath((permission) => account.permissions.includes(permission)),
        { replace: true },
      )
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="login2" dir={dir}>
      <div className="login2-atmosphere" aria-hidden="true">
        <div className="login2-atmosphere__sky" />
        <div className="login2-atmosphere__stars" />
        <div className="login2-atmosphere__grid" />
        <div className="login2-orb login2-orb--a" />
        <div className="login2-orb login2-orb--b" />
        <div className="login2-atmosphere__glow" />
        <img className="login2-atmosphere__skyline" src={skylinePhoto} alt="" />
      </div>

      <header className="login2-top">
        <div className="login2-brands">
          <span className="login2-wordmark">{t('login2Kicker')}</span>
        </div>
        <div className="login2-top-tools">
          <div className="login2-clock">
            <strong>{clock.time}</strong>
            <span>{clock.date}</span>
          </div>
          <LangToggle className="login-langs" />
          <ThemePicker compact />
        </div>
      </header>

      <main className="login2-main">
        <section className="login2-story">
          <p className="login2-kicker">{t('login2Kicker')}</p>
          <h1>
            {t('login2Headline')}
            <span>{t('login2HeadlineAccent')}</span>
          </h1>
          <p className="login2-lead">{t('login2Lead')}</p>

          <ul className="login2-feats">
            {FEATURES.map((feature) => {
              const Icon = feature.icon
              return (
                <li key={feature.title}>
                  <span className="login2-feat-icon" aria-hidden="true">
                    <Icon />
                  </span>
                  <div>
                    <strong>{t(feature.title)}</strong>
                    <span>{t(feature.body)}</span>
                  </div>
                </li>
              )
            })}
          </ul>

          <div className="login2-preview" aria-hidden="true">
            <div className="login2-preview__bar">
              <span className="login2-live">
                <IconLive />
                {t('login2PreviewMeta')}
              </span>
              <span className="login2-preview__title">{t('login2PreviewTitle')}</span>
            </div>
            <div className="login2-preview__grid">
              <div className="login2-chart">
                <IconChart />
                <div className="login2-bars">
                  <i style={{ height: '42%' }} />
                  <i style={{ height: '68%' }} />
                  <i style={{ height: '54%' }} />
                  <i style={{ height: '86%' }} />
                  <i style={{ height: '61%' }} />
                </div>
              </div>
              <table>
                <thead>
                  <tr>
                    <th>{t('login2ColReport')}</th>
                    <th>{t('login2ColStatus')}</th>
                    <th>{t('login2ColRows')}</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>{t('login2RowOps')}</td>
                    <td>
                      <em className="login2-pill">{t('login2Active')}</em>
                    </td>
                    <td>12.4k</td>
                  </tr>
                  <tr>
                    <td>{t('login2RowFinance')}</td>
                    <td>
                      <em className="login2-pill">{t('login2Active')}</em>
                    </td>
                    <td>8.1k</td>
                  </tr>
                  <tr>
                    <td>{t('login2RowWorkforce')}</td>
                    <td>
                      <em className="login2-pill is-muted">{t('login2Review')}</em>
                    </td>
                    <td>3.6k</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </section>

        <form className="login2-card" onSubmit={onSubmit}>
          {pending && <Spinner overlay label={t('signingIn')} />}
          <div className="login2-card__mark" aria-hidden="true">
            R
          </div>
          <h2>{t('login2SignIn')}</h2>
          <p>{t('login2SignInHint')}</p>
          {error && <div className="login-error">{error}</div>}
          <label className="login2-field">
            {t('username')}
            <input
              id="login2-username"
              className="form-control"
              autoComplete="username"
              placeholder={t('usernamePh')}
              value={userName}
              onChange={(event) => setUserName(event.target.value)}
              required
            />
          </label>
          <label className="login2-field">
            {t('password')}
            <PasswordInput
              id="login2-password"
              className="form-control"
              autoComplete="current-password"
              placeholder={t('passwordPh')}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
            />
          </label>
          <div className="login-row-between">
            <span />
            <button
              type="button"
              className="forgot-link"
              onClick={() => setForgotHint(true)}
            >
              {t('forgot')}
            </button>
          </div>
          {forgotHint && <p className="login-forgot-hint">{t('forgotHint')}</p>}
          <button type="submit" className="login2-submit" disabled={pending}>
            {pending && <Spinner size="sm" />}
            {pending ? t('signingIn') : t('login')}
          </button>
          {portalUrl && (
            <>
              <p className="login-or">{t('loginOr')}</p>
              <a className="login2-portal" href={portalUrl}>
                {t('login2Portal')}
              </a>
            </>
          )}
        </form>
      </main>
    </div>
  )
}
