import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { SmtpProfile, SmtpProfileWrite } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { Field } from './AdminFields'
import { PasswordInput } from './PasswordInput'
import { useLocale } from '../i18n/LocaleProvider'

const emptyDraft = (): SmtpProfileWrite & { id?: string; hasPassword?: boolean } => ({
  name: '',
  host: '',
  port: 587,
  username: '',
  password: '',
  fromAddress: '',
  fromName: '',
  enableSsl: true,
  isDefault: false,
})

export function SmtpSettingsPanel() {
  const { can } = useAuth()
  const { t } = useLocale()
  const queryClient = useQueryClient()
  const canUpdate = can('settings.update')
  const profiles = useQuery({ queryKey: ['smtp-profiles'], queryFn: api.listSmtpProfiles })
  const [draft, setDraft] = useState(emptyDraft)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: async () => {
      const body: SmtpProfileWrite = {
        name: draft.name,
        host: draft.host,
        port: Number(draft.port) || 587,
        username: draft.username,
        password: draft.password,
        fromAddress: draft.fromAddress,
        fromName: draft.fromName,
        enableSsl: draft.enableSsl,
        isDefault: draft.isDefault,
      }
      if (draft.id) {
        return api.updateSmtpProfile(draft.id, body)
      }
      return api.createSmtpProfile(body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['smtp-profiles'] })
      await queryClient.invalidateQueries({ queryKey: ['smtp-options'] })
      setDraft(emptyDraft())
      setError(null)
      setNotice(t('smtpSaved'))
    },
    onError: (err: Error) => {
      setNotice(null)
      setError(err.message)
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteSmtpProfile(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['smtp-profiles'] })
      await queryClient.invalidateQueries({ queryKey: ['smtp-options'] })
      setDraft(emptyDraft())
    },
    onError: (err: Error) => setError(err.message),
  })

  const test = useMutation({
    mutationFn: () =>
      api.testSmtpProfile({
        name: draft.name || 'test',
        host: draft.host,
        port: Number(draft.port) || 587,
        username: draft.username,
        password: draft.password,
        fromAddress: draft.fromAddress || 'test@localhost',
        fromName: draft.fromName,
        enableSsl: draft.enableSsl,
        isDefault: false,
      }),
    onSuccess: () => {
      setError(null)
      setNotice(t('settingsTestOk', { provider: draft.host }))
    },
    onError: (err: Error) => {
      setNotice(null)
      setError(err.message)
    },
  })

  function edit(profile: SmtpProfile) {
    setDraft({
      id: profile.id,
      name: profile.name,
      host: profile.host,
      port: profile.port,
      username: profile.username ?? '',
      password: '',
      fromAddress: profile.fromAddress,
      fromName: profile.fromName ?? '',
      enableSsl: profile.enableSsl,
      isDefault: profile.isDefault,
      hasPassword: profile.hasPassword,
    })
    setError(null)
    setNotice(null)
  }

  return (
    <div className="mail-studio">
      <aside className="mail-catalog">
        <div className="mail-catalog-head">
          <h2>{t('smtpTitle')}</h2>
          {canUpdate && (
            <button type="button" className="btn" onClick={() => setDraft(emptyDraft())}>
              {t('smtpAdd')}
            </button>
          )}
        </div>
        {(profiles.data ?? []).length === 0 ? (
          <p className="mail-empty muted">{t('smtpEmpty')}</p>
        ) : (
          <div className="mail-catalog-list">
            {(profiles.data ?? []).map((profile) => (
              <button
                key={profile.id}
                type="button"
                className={`mail-item${draft.id === profile.id ? ' selected' : ''}`}
                onClick={() => edit(profile)}
              >
                <span className="mail-item-name">{profile.name}</span>
                <span className="mail-item-meta">
                  <span className="muted small">
                    {profile.host}:{profile.port}
                  </span>
                  {profile.isDefault && <span className="badge table">{t('smtpDefault')}</span>}
                  {profile.enableSsl && <span className="badge draft">{t('smtpSsl')}</span>}
                </span>
              </button>
            ))}
          </div>
        )}
      </aside>
      {canUpdate && (
        <section className="settings-card mail-editor">
          <div className="settings-card-head">
            <h2>{draft.id ? draft.name || t('smtpTitle') : t('smtpAdd')}</h2>
          </div>
          {error && <p className="banner error">{error}</p>}
          {notice && <p className="banner">{notice}</p>}
          <div className="mail-section">
            <div className="mail-section-title">{t('smtpSectionConnection')}</div>
            <div className="settings-fields">
              <Field label={t('smtpName')}>
                <input value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value })} />
              </Field>
              <Field label={t('smtpHost')}>
                <input value={draft.host} onChange={(event) => setDraft({ ...draft, host: event.target.value })} />
              </Field>
              <Field label={t('smtpPort')}>
                <input
                  type="number"
                  min={1}
                  max={65535}
                  value={draft.port}
                  onChange={(event) => setDraft({ ...draft, port: Number(event.target.value) })}
                />
              </Field>
              <Field label={t('smtpUsername')}>
                <input
                  value={draft.username ?? ''}
                  autoComplete="off"
                  onChange={(event) => setDraft({ ...draft, username: event.target.value })}
                />
              </Field>
              <Field label={t('smtpPassword')}>
                <PasswordInput
                  value={draft.password ?? ''}
                  autoComplete="new-password"
                  placeholder={draft.hasPassword ? t('smtpPasswordKeep') : ''}
                  onChange={(event) => setDraft({ ...draft, password: event.target.value })}
                />
              </Field>
            </div>
            <label className="check-row">
              <input
                type="checkbox"
                checked={draft.enableSsl}
                onChange={(event) => setDraft({ ...draft, enableSsl: event.target.checked })}
              />
              {t('smtpSsl')}
            </label>
          </div>
          <div className="mail-section">
            <div className="mail-section-title">{t('smtpSectionSender')}</div>
            <div className="settings-fields">
              <Field label={t('smtpFrom')}>
                <input
                  value={draft.fromAddress}
                  onChange={(event) => setDraft({ ...draft, fromAddress: event.target.value })}
                />
              </Field>
              <Field label={t('smtpFromName')}>
                <input
                  value={draft.fromName ?? ''}
                  onChange={(event) => setDraft({ ...draft, fromName: event.target.value })}
                />
              </Field>
            </div>
            <label className="check-row">
              <input
                type="checkbox"
                checked={draft.isDefault}
                onChange={(event) => setDraft({ ...draft, isDefault: event.target.checked })}
              />
              {t('smtpMakeDefault')}
            </label>
          </div>
          <div className="row-actions">
            <button type="button" className="btn primary" disabled={save.isPending} onClick={() => save.mutate()}>
              {save.isPending ? t('saving') : t('saveSettings')}
            </button>
            <button type="button" className="btn" disabled={test.isPending} onClick={() => test.mutate()}>
              {test.isPending ? t('settingsTesting') : t('smtpTest')}
            </button>
            {draft.id && (
              <button
                type="button"
                className="btn danger"
                disabled={remove.isPending}
                onClick={() => remove.mutate(draft.id!)}
              >
                {t('smtpDelete')}
              </button>
            )}
          </div>
        </section>
      )}
    </div>
  )
}
