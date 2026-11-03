import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
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

function passwordReady(value: string) {
  return value.length >= 8 && /[A-Za-z]/.test(value) && /\d/.test(value)
}

export function ResetPasswordPage() {
  const { lang, dir, t } = useLocale()
  const branding = useQuery({ queryKey: ['branding'], queryFn: api.getBranding, staleTime: 60_000 })
  const organization = branding.data
    ? localizedText(lang, branding.data.organizationName, branding.data.organizationNameArabic)
    : ''
  const logoUrl = branding.data?.hasLogo ? api.brandingLogoUrl(branding.data.logoVersion) : null
  usePageTitle(t('resetPasswordTitle'))
  const [params] = useSearchParams()
  const token = params.get('token')?.trim() ?? ''
  const [now, setNow] = useState(() => new Date())
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const [pending, setPending] = useState(false)
  const clock = formatClock(now, lang)

  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 1000)
    return () => window.clearInterval(id)
  }, [])

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (!token) {
      setError(t('resetPasswordMissing'))
      return
    }
    if (password !== confirm) {
      setError(t('resetPasswordMismatch'))
      return
    }
    if (!passwordReady(password)) {
      setError(t('resetPasswordRule'))
      return
    }
    setPending(true)
    setError(null)
    try {
      await api.resetPassword(token, password)
      setDone(true)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setPending(false)
    }
  }

  return (
    <form className="loginPage" dir={dir} onSubmit={onSubmit}>
      <div className="login-shell">
        {pending && <Spinner overlay label={t('resetPasswordSaving')} />}
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
            <h1>{t('resetPasswordTitle')}</h1>
            <p>{t('resetPasswordHint')}</p>
          </div>
          <div className="login-form">
            {!token && <div className="login-error">{t('resetPasswordMissing')}</div>}
            {error && <div className="login-error">{error}</div>}
            {done ? (
              <p className="login-forgot-hint">{t('resetPasswordDone')}</p>
            ) : (
              <>
                <div className="login-txtbox">
                  <label htmlFor="reset-password">{t('resetPasswordNew')}</label>
                  <PasswordInput
                    id="reset-password"
                    className="form-control"
                    autoComplete="new-password"
                    placeholder={t('passwordPh')}
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                    required
                    disabled={!token}
                  />
                </div>
                <div className="login-txtbox">
                  <label htmlFor="reset-password-confirm">{t('resetPasswordConfirm')}</label>
                  <PasswordInput
                    id="reset-password-confirm"
                    className="form-control"
                    autoComplete="new-password"
                    placeholder={t('passwordPh')}
                    value={confirm}
                    onChange={(event) => setConfirm(event.target.value)}
                    required
                    disabled={!token}
                  />
                </div>
                <div className="loginBtn">
                  <button type="submit" className="login-btn" disabled={pending || !token}>
                    {pending && <Spinner size="sm" />}
                    {pending ? t('resetPasswordSaving') : t('resetPasswordSubmit')}
                  </button>
                </div>
              </>
            )}
            <div className="login-row-between">
              <Link className="forgot-link" to="/login">
                {done ? t('resetPasswordSignIn') : t('forgotBack')}
              </Link>
            </div>
          </div>
        </div>
      </div>
    </form>
  )
}
