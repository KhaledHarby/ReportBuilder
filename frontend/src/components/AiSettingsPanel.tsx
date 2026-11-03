import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { AiConnection, AiConnectionWrite } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { Field } from './AdminFields'
import { PasswordInput } from './PasswordInput'
import { useLocale } from '../i18n/LocaleProvider'

const emptyDraft = (): AiConnectionWrite & { hasApiKey?: boolean } => ({
  name: '',
  baseUrl: 'https://api.openai.com/v1/',
  model: 'gpt-4o-mini',
  apiKey: '',
  isEnabled: true,
  isDefault: false,
})

export function AiSettingsPanel() {
  const { can } = useAuth()
  const { t } = useLocale()
  const queryClient = useQueryClient()
  const canUpdate = can('settings.update')
  const connections = useQuery({ queryKey: ['ai-connections'], queryFn: api.listAiConnections })
  const [draft, setDraft] = useState(emptyDraft)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: async () => {
      const body: AiConnectionWrite = {
        name: draft.name,
        baseUrl: draft.baseUrl,
        model: draft.model,
        apiKey: draft.apiKey,
        isEnabled: draft.isEnabled,
        isDefault: draft.isEnabled && draft.isDefault,
      }
      if (draft.id) {
        return api.updateAiConnection(draft.id, body)
      }
      return api.createAiConnection(body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['ai-connections'] })
      await queryClient.invalidateQueries({ queryKey: ['ai-status'] })
      setDraft(emptyDraft())
      setError(null)
      setNotice(t('aiSettingsSaved'))
    },
    onError: (err: Error) => {
      setNotice(null)
      setError(err.message)
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteAiConnection(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['ai-connections'] })
      await queryClient.invalidateQueries({ queryKey: ['ai-status'] })
      setDraft(emptyDraft())
      setError(null)
      setNotice(t('aiSettingsSaved'))
    },
    onError: (err: Error) => setError(err.message),
  })

  const test = useMutation({
    mutationFn: () =>
      api.testAiConnection({
        id: draft.id,
        name: draft.name || 'test',
        baseUrl: draft.baseUrl,
        model: draft.model,
        apiKey: draft.apiKey,
        isEnabled: draft.isEnabled,
        isDefault: false,
      }),
    onSuccess: (result) => {
      setError(null)
      setNotice(t('aiSettingsTestReply', { reply: result.reply }))
    },
    onError: (err: Error) => {
      setNotice(null)
      setError(err.message)
    },
  })

  function edit(connection: AiConnection) {
    setDraft({
      id: connection.id,
      name: connection.name,
      baseUrl: connection.baseUrl,
      model: connection.model,
      apiKey: '',
      isEnabled: connection.isEnabled,
      isDefault: connection.isDefault,
      hasApiKey: connection.hasApiKey,
    })
    setError(null)
    setNotice(null)
  }

  return (
    <div className="mail-studio">
      <aside className="mail-catalog">
        <div className="mail-catalog-head">
          <h2>{t('aiSettingsTitle')}</h2>
          {canUpdate && (
            <button type="button" className="btn" onClick={() => setDraft(emptyDraft())}>
              {t('aiSettingsAdd')}
            </button>
          )}
        </div>
        {(connections.data ?? []).length === 0 ? (
          <p className="mail-empty muted">{t('aiSettingsEmpty')}</p>
        ) : (
          <div className="mail-catalog-list">
            {(connections.data ?? []).map((connection) => (
              <button
                key={connection.id}
                type="button"
                className={`mail-item${draft.id === connection.id ? ' selected' : ''}`}
                onClick={() => edit(connection)}
              >
                <span className="mail-item-name">{connection.name}</span>
                <span className="mail-item-meta">
                  {connection.isDefault && <span className="badge table">{t('aiSettingsDefault')}</span>}
                  {!connection.isEnabled && <span className="badge draft">{t('aiSettingsDisabled')}</span>}
                </span>
              </button>
            ))}
          </div>
        )}
      </aside>
      {canUpdate && (
        <section className="settings-card mail-editor">
          <div className="settings-card-head">
            <h2>{draft.id ? draft.name || t('aiSettingsTitle') : t('aiSettingsAdd')}</h2>
          </div>
          {error && <p className="banner error">{error}</p>}
          {notice && <p className="banner">{notice}</p>}
          <div className="mail-section">
            <div className="settings-fields">
              <Field label={t('aiSettingsName')}>
                <input value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value })} />
              </Field>
              <Field label={t('aiSettingsEndpoint')}>
                <input
                  value={draft.baseUrl}
                  placeholder={t('aiSettingsEndpointHint')}
                  onChange={(event) => setDraft({ ...draft, baseUrl: event.target.value })}
                />
              </Field>
              <Field label={t('aiSettingsModel')}>
                <input value={draft.model} onChange={(event) => setDraft({ ...draft, model: event.target.value })} />
              </Field>
              <Field label={t('aiSettingsApiKey')}>
                <PasswordInput
                  value={draft.apiKey ?? ''}
                  autoComplete="new-password"
                  placeholder={draft.hasApiKey ? t('aiSettingsApiKeyKeep') : ''}
                  onChange={(event) => setDraft({ ...draft, apiKey: event.target.value })}
                />
              </Field>
            </div>
            <label className="check-row">
              <input
                type="checkbox"
                checked={draft.isEnabled}
                onChange={(event) =>
                  setDraft({
                    ...draft,
                    isEnabled: event.target.checked,
                    isDefault: event.target.checked ? draft.isDefault : false,
                  })
                }
              />
              {t('aiSettingsEnabled')}
            </label>
            <label className="check-row">
              <input
                type="checkbox"
                checked={draft.isDefault}
                disabled={!draft.isEnabled}
                onChange={(event) =>
                  setDraft({
                    ...draft,
                    isDefault: event.target.checked,
                    isEnabled: event.target.checked ? true : draft.isEnabled,
                  })
                }
              />
              {t('aiSettingsMakeDefault')}
            </label>
          </div>
          <div className="row-actions">
            <button type="button" className="btn primary" disabled={save.isPending} onClick={() => save.mutate()}>
              {save.isPending ? t('saving') : t('saveSettings')}
            </button>
            <button type="button" className="btn" disabled={test.isPending} onClick={() => test.mutate()}>
              {test.isPending ? t('settingsTesting') : t('aiSettingsTest')}
            </button>
            {draft.id && (
              <button
                type="button"
                className="btn danger"
                disabled={remove.isPending}
                onClick={() => remove.mutate(draft.id!)}
              >
                {t('aiSettingsDelete')}
              </button>
            )}
          </div>
        </section>
      )}
    </div>
  )
}
