import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { AppBranding, AppBrandingWrite } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { Field } from './AdminFields'
import { useLocale } from '../i18n/LocaleProvider'

const maxLogoBytes = 512 * 1024
const logoTypes = ['image/png', 'image/jpeg', 'image/webp', 'image/gif']

const emptyBranding = (): AppBranding => ({
  applicationName: 'MECS',
  applicationNameArabic: '',
  studioName: 'Report studio',
  studioNameArabic: '',
  organizationName: '',
  organizationNameArabic: '',
  defaultLanguage: 'en',
  hasLogo: false,
  logoVersion: 0,
})

export function GeneralSettingsPanel() {
  const { can } = useAuth()
  const { t } = useLocale()
  const queryClient = useQueryClient()
  const canUpdate = can('settings.update')
  const branding = useQuery({ queryKey: ['branding'], queryFn: api.getBranding })
  const [form, setForm] = useState<AppBranding>(emptyBranding)
  const [logoBase64, setLogoBase64] = useState<string | null>(null)
  const [logoPreview, setLogoPreview] = useState<string | null>(null)
  const [removeLogo, setRemoveLogo] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    if (!branding.data) {
      return
    }
    setForm(branding.data)
    setLogoBase64(null)
    setLogoPreview(null)
    setRemoveLogo(false)
  }, [branding.data])

  const save = useMutation({
    mutationFn: () => {
      const body: AppBrandingWrite = {
        applicationName: form.applicationName,
        applicationNameArabic: form.applicationNameArabic,
        studioName: form.studioName,
        studioNameArabic: form.studioNameArabic,
        organizationName: form.organizationName,
        organizationNameArabic: form.organizationNameArabic,
        defaultLanguage: form.defaultLanguage,
        logoBase64,
        removeLogo,
      }
      return api.updateBranding(body)
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['branding'], data)
      setForm(data)
      setLogoBase64(null)
      setLogoPreview(null)
      setRemoveLogo(false)
      setError(null)
      setNotice(t('generalSaved'))
    },
    onError: (err: Error) => {
      setNotice(null)
      setError(err.message)
    },
  })

  function onLogo(file: File | undefined) {
    setNotice(null)
    if (!file) {
      return
    }
    if (!logoTypes.includes(file.type) || file.size > maxLogoBytes) {
      setError(t('generalLogoInvalid'))
      return
    }
    const reader = new FileReader()
    reader.onload = () => {
      const result = String(reader.result ?? '')
      const comma = result.indexOf(',')
      setLogoBase64(comma >= 0 ? result.slice(comma + 1) : result)
      setLogoPreview(result)
      setRemoveLogo(false)
      setError(null)
    }
    reader.readAsDataURL(file)
  }

  const savedLogo = form.hasLogo && !removeLogo && !logoPreview
    ? api.brandingLogoUrl(form.logoVersion)
    : null
  const preview = logoPreview || savedLogo

  if (branding.isLoading) {
    return <p className="muted">{t('loadingApp')}</p>
  }

  if (branding.isError) {
    return <p className="banner error">{(branding.error as Error).message}</p>
  }

  return (
    <article className="settings-card general-settings">
      {error && <p className="banner error">{error}</p>}
      {notice && <p className="banner">{notice}</p>}
      <div className="settings-card-head">
        <div>
          <h2>{t('generalIdentity')}</h2>
          <p className="muted small">{t('generalIdentityHint')}</p>
        </div>
      </div>
      <div className="settings-fields">
        <Field label={t('generalAppName')}>
          <input
            value={form.applicationName}
            disabled={!canUpdate}
            maxLength={80}
            onChange={(event) => setForm({ ...form, applicationName: event.target.value })}
          />
        </Field>
        <Field label={t('generalAppNameArabic')}>
          <input
            dir="rtl"
            lang="ar"
            value={form.applicationNameArabic}
            disabled={!canUpdate}
            maxLength={80}
            onChange={(event) => setForm({ ...form, applicationNameArabic: event.target.value })}
          />
        </Field>
        <Field label={t('generalStudio')}>
          <input
            value={form.studioName}
            disabled={!canUpdate}
            maxLength={80}
            onChange={(event) => setForm({ ...form, studioName: event.target.value })}
          />
        </Field>
        <Field label={t('generalStudioArabic')}>
          <input
            dir="rtl"
            lang="ar"
            value={form.studioNameArabic}
            disabled={!canUpdate}
            maxLength={80}
            onChange={(event) => setForm({ ...form, studioNameArabic: event.target.value })}
          />
        </Field>
        <Field label={t('generalOrganization')}>
          <input
            value={form.organizationName}
            disabled={!canUpdate}
            maxLength={160}
            onChange={(event) => setForm({ ...form, organizationName: event.target.value })}
          />
        </Field>
        <Field label={t('generalOrganizationArabic')}>
          <input
            dir="rtl"
            lang="ar"
            value={form.organizationNameArabic}
            disabled={!canUpdate}
            maxLength={160}
            onChange={(event) => setForm({ ...form, organizationNameArabic: event.target.value })}
          />
        </Field>
        <Field label={t('generalDefaultLanguage')}>
          <select
            value={form.defaultLanguage}
            disabled={!canUpdate}
            onChange={(event) =>
              setForm({ ...form, defaultLanguage: event.target.value === 'ar' ? 'ar' : 'en' })
            }
          >
            <option value="en">{t('scheduleEnglish')}</option>
            <option value="ar">{t('scheduleArabic')}</option>
          </select>
          <span className="muted small">{t('generalDefaultLanguageHint')}</span>
        </Field>
      </div>

      <div className="mail-section">
        <div className="mail-section-title">{t('generalLogo')}</div>
        <p className="muted small">{t('generalLogoHint')}</p>
        <div className="general-logo-row">
          {preview ? (
            <img className="general-logo-preview" src={preview} alt="" />
          ) : (
            <span className="general-logo-empty">{form.applicationName.trim().charAt(0) || 'M'}</span>
          )}
          {canUpdate && (
            <div className="general-logo-actions">
              <label className="btn">
                {t('generalLogoChoose')}
                <input
                  type="file"
                  accept="image/png,image/jpeg,image/webp,image/gif"
                  onChange={(event) => {
                    onLogo(event.target.files?.[0])
                    event.target.value = ''
                  }}
                />
              </label>
              {(preview || form.hasLogo) && !removeLogo && (
                <button
                  type="button"
                  className="btn"
                  onClick={() => {
                    setLogoBase64(null)
                    setLogoPreview(null)
                    setRemoveLogo(true)
                  }}
                >
                  {t('generalLogoRemove')}
                </button>
              )}
            </div>
          )}
        </div>
      </div>

      {canUpdate && (
        <div className="row-actions">
          <button type="button" className="btn primary" disabled={save.isPending} onClick={() => save.mutate()}>
            {save.isPending ? t('saving') : t('saveSettings')}
          </button>
        </div>
      )}
    </article>
  )
}
