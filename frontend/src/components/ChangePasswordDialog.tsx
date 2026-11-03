import { useEffect, useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { useLocale } from '../i18n/LocaleProvider'
import { Field } from './AdminFields'
import { PasswordInput } from './PasswordInput'

type Props = {
  open: boolean
  onClose: () => void
}

export function ChangePasswordDialog({ open, onClose }: Props) {
  const { t } = useLocale()
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    if (!open) {
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
      setError(null)
      setPending(false)
      setSaved(false)
      return
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) {
    return null
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (newPassword !== confirmPassword) {
      setError(t('passwordMismatch'))
      return
    }
    setPending(true)
    setError(null)
    try {
      await api.changePassword(currentPassword, newPassword)
      setSaved(true)
      window.setTimeout(onClose, 1200)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="grants-overlay" onClick={onClose} role="presentation">
      <form
        className="grants-dialog password-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="password-title"
        onClick={(event) => event.stopPropagation()}
        onSubmit={(event) => void onSubmit(event)}
      >
        <header className="grants-chrome">
          <div className="grants-head">
            <div>
              <div className="kicker">{t('accountKicker')}</div>
              <h2 id="password-title">{t('changePassword')}</h2>
              <p className="muted small">{t('passwordHint')}</p>
            </div>
            <button type="button" className="icon-btn" aria-label={t('close')} onClick={onClose}>
              <span aria-hidden="true">×</span>
            </button>
          </div>
        </header>
        <div className="password-dialog-body">
          {error && <div className="banner error">{error}</div>}
          {saved && <div className="banner ok">{t('passwordChanged')}</div>}
          <Field label={t('currentPassword')}>
            <PasswordInput
              autoComplete="current-password"
              value={currentPassword}
              onChange={(event) => setCurrentPassword(event.target.value)}
              required
              disabled={pending || saved}
            />
          </Field>
          <Field label={t('newPassword')}>
            <PasswordInput
              autoComplete="new-password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              required
              disabled={pending || saved}
            />
          </Field>
          <Field label={t('confirmPassword')}>
            <PasswordInput
              autoComplete="new-password"
              value={confirmPassword}
              onChange={(event) => setConfirmPassword(event.target.value)}
              required
              disabled={pending || saved}
            />
          </Field>
        </div>
        <footer className="password-dialog-foot">
          <button type="button" className="btn" onClick={onClose} disabled={pending}>
            {t('cancel')}
          </button>
          <button type="submit" className="btn primary" disabled={pending || saved}>
            {pending ? t('saving') : t('save')}
          </button>
        </footer>
      </form>
    </div>
  )
}
