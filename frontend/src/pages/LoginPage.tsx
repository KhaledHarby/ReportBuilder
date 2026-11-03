import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { Navigate, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthProvider'
import { homePath } from '../auth/homePath'
import { PasswordInput } from '../components/PasswordInput'
import { Spinner } from '../components/Spinner'
import { ThemePicker } from '../components/ThemePicker'
import { LangToggle } from '../i18n/LangToggle'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'
import { usePageTitle } from '../lib/pageTitle'
import dmtLogo from '../assets/login/dmt-logo.png'
import skylinePhoto from '../assets/login/skyline-photo.png'
import tammLogo from '../assets/login/tamm-logo.png'

function formatClock(now: Date, lang: 'en' | 'ar') {
  const time = now.toLocaleTimeString('en-GB', {
    hour12: false,
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
  const date = now
    .toLocaleDateString(lang === 'ar' ? 'ar-AE' : 'en-GB', {
      weekday: 'short',
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    })
    .toUpperCase()
  return { time, date }
}

export function LoginPage() {
  const { user, ready, login, can } = useAuth()
  const { lang, dir, t } = useLocale()
  const branding = useQuery({ queryKey: ['branding'], queryFn: api.getBranding, staleTime: 60_000 })
  const organization = branding.data
    ? localizedText(lang, branding.data.organizationName, branding.data.organizationNameArabic)
    : ''
  const logoUrl = branding.data?.hasLogo ? api.brandingLogoUrl(branding.data.logoVersion) : null
  usePageTitle(t('login'))
  const navigate = useNavigate()
  const [now, setNow] = useState(() => new Date())
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [forgot, setForgot] = useState(false)
  const [forgotSent, setForgotSent] = useState(false)
  const [pending, setPending] = useState(false)
  const [portalUrl, setPortalUrl] = useState<string | null>(null)
  const clock = formatClock(now, lang)

  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 1000)
    return () => window.clearInterval(id)
  }, [])

  useEffect(() => {
    let cancelled = false
    void api.getAuthOptions()
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
      if (forgot) {
        await api.requestPasswordReset(userName.trim())
        setForgotSent(true)
        return
      }
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
    <form className="loginPage" dir={dir} onSubmit={onSubmit}>
      <div className="login-shell">
        {pending && <Spinner overlay label={forgot ? t('forgotSending') : t('signingIn')} />}
        <div className="login-visual">
          <img className="login-visual__bg" src={skylinePhoto} alt="" />
          <div className="login-logos">
            <img src={dmtLogo} alt="DMT" />
            <img src={tammLogo} alt="TAMM" />
          </div>
          <div className="login-visual__copy">
            <span className="login-visual__eyebrow">{t('loginEyebrow')}</span>
            <h2 className="login-visual__title">{organization || t('loginTitle')}</h2>
            <p className="login-visual__subtitle">{t('loginSubtitle')}</p>
          </div>
          <svg
            className="login-visual__skyline"
            viewBox="0 0 600 90"
            preserveAspectRatio="none"
            aria-hidden="true"
          >
            <path d="M0,90 L0,55 L20,55 L20,35 L40,35 L40,55 L60,55 L60,20 L70,20 L70,55 L95,55 L95,45 L110,45 L110,55 L140,55 L140,10 L150,10 L150,55 L175,55 L175,30 L190,30 L190,55 L220,55 L220,40 L235,40 L235,55 L260,55 L260,25 L280,25 L280,55 L300,55 L300,15 L315,15 L315,55 L340,55 L340,45 L360,45 L360,55 L390,55 L390,35 L405,35 L405,55 L430,55 L430,20 L450,20 L450,55 L480,55 L480,40 L495,40 L495,55 L520,55 L520,30 L540,30 L540,55 L565,55 L565,45 L580,45 L580,55 L600,55 L600,90 Z" />
          </svg>
        </div>
        <div className="login-panel">
          <div className="login-panel__meta">
            <div className="login-clock">
              <span className="login-clock__time">{clock.time}</span>
              <span className="login-clock__date">{clock.date}</span>
            </div>
            <LangToggle className="login-langs" />
          </div>
          <ThemePicker compact />
          <div className="login-panel__header">
            {logoUrl && <img className="login-app-logo" src={logoUrl} alt="" />}
            <h1>{forgot ? t('forgotTitle') : t('loginWelcome')}</h1>
            <p>{forgot ? t('forgotLead') : t('loginContinue')}</p>
          </div>
          <div className="login-form">
            {error && <div className="login-error">{error}</div>}
            <div className="login-txtbox">
              <label htmlFor="login-username">{t('username')}</label>
              <input
                id="login-username"
                className="form-control"
                autoComplete="username"
                placeholder={t('usernamePh')}
                value={userName}
                onChange={(event) => setUserName(event.target.value)}
                required
              />
            </div>
            {!forgot && (
            <div className="login-txtbox">
              <label htmlFor="login-password">{t('password')}</label>
              <PasswordInput
                id="login-password"
                className="form-control"
                autoComplete="current-password"
                placeholder={t('passwordPh')}
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                required
              />
            </div>
            )}
            <div className="login-row-between">
              {forgot ? (
                <button
                  type="button"
                  className="forgot-link"
                  onClick={() => {
                    setForgot(false)
                    setForgotSent(false)
                    setError(null)
                  }}
                >
                  {t('forgotBack')}
                </button>
              ) : (
                <span />
              )}
              {!forgot && (
                <button
                  type="button"
                  className="forgot-link"
                  onClick={() => {
                    setForgot(true)
                    setForgotSent(false)
                    setError(null)
                  }}
                >
                  {t('forgot')}
                </button>
              )}
            </div>
            {forgotSent && <p className="login-forgot-hint">{t('forgotSent')}</p>}
            <div className="loginBtn">
              <button type="submit" className="login-btn" disabled={pending || forgotSent}>
                {pending && <Spinner size="sm" />}
                {pending
                  ? forgot
                    ? t('forgotSending')
                    : t('signingIn')
                  : forgot
                    ? t('forgotSend')
                    : t('login')}
              </button>
              {!forgot && portalUrl && (
                <>
                  <p className="login-or">{t('loginOr')}</p>
                  <a className="login-btn login-btn--portal" href={portalUrl}>
                    {t('loginWithPortal')}
                  </a>
                </>
              )}
            </div>
          </div>
        </div>
      </div>
    </form>
  )
}
