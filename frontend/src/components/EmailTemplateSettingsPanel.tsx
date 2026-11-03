import { useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { EmailTemplate, EmailTemplateWrite } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { Field } from './AdminFields'
import { useLocale } from '../i18n/LocaleProvider'
import type { Lang } from '../i18n/locale'
import type { MessageKey } from '../i18n/messages'

const rawHtmlTokens = new Set(['logo'])

const sampleValues = (lang: Lang, branding?: { applicationName: string; applicationNameArabic: string; studioName: string; studioNameArabic: string; organizationName: string; organizationNameArabic: string; hasLogo: boolean; logoVersion: number }): Record<string, string> => {
  const applicationName = branding?.applicationName || 'MECS'
  const applicationNameArabic = branding?.applicationNameArabic || applicationName
  const logo = branding?.hasLogo
    ? `<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 16px;"><tr><td align="center" style="text-align:center;padding:0;"><img src="${api.brandingLogoUrl(branding.logoVersion)}" alt="${encodeHtml(applicationName)}" width="160" style="display:block;margin:0 auto;border:0;width:160px;max-width:160px;height:auto;"></td></tr></table>`
    : ''
  return {
    reportName: lang === 'ar' ? 'تقرير تجريبي' : 'Sample report',
    reportNameEnglish: 'Sample report',
    reportNameArabic: 'تقرير تجريبي',
    reportDate: sampleReportDate(lang),
    reportDateEnglish: sampleReportDate('en'),
    reportDateArabic: sampleReportDate('ar'),
    userName: 'analyst',
    displayName: 'Report analyst',
    displayNameArabic: 'محلل التقارير',
    email: 'analyst@example.com',
    reportUrl: 'https://example.com/reports/sample/view',
    resetUrl: 'https://example.com/reset-password?token=sample',
    expiresMinutes: '30',
    fileNote: lang === 'ar' ? 'الملف مرفق.' : 'The report file is attached.',
    applicationName,
    applicationNameArabic,
    studioName: branding?.studioName || 'Report studio',
    studioNameArabic: branding?.studioNameArabic || 'استوديو التقارير',
    organizationName: branding?.organizationName || '',
    organizationNameArabic: branding?.organizationNameArabic || '',
    logo,
  }
}

function sampleReportDate(lang: Lang) {
  const parts = new Intl.DateTimeFormat(lang === 'ar' ? 'ar-AE' : 'en-GB', {
    timeZone: 'Asia/Dubai',
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(new Date())
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((item) => item.type === type)?.value ?? ''
  return `${part('day')} ${part('month')} ${part('year')} ${part('hour')}:${part('minute')}`
}

function encodeHtml(value: string) {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

function applyTokens(source: string, html: boolean, values: Record<string, string>) {
  let text = source
  for (const [key, raw] of Object.entries(values)) {
    const value = rawHtmlTokens.has(key)
      ? html
        ? raw
        : ''
      : html
        ? encodeHtml(raw).replace(/\r\n/g, '<br>').replace(/\n/g, '<br>')
        : raw
    text = text.replace(new RegExp(`\\{${key}\\}`, 'gi'), () => value)
  }
  return text
}

function previewMessage(subject: string, body: string, isHtml: boolean, values: Record<string, string>) {
  const nextSubject = applyTokens(subject, false, values).trim() || values.reportName
  let nextBody = applyTokens(body, isHtml, values)
  if (!/\{fileNote\}/i.test(body) && values.fileNote) {
    nextBody += isHtml ? `<p>${encodeHtml(values.fileNote)}</p>` : `\n\n${values.fileNote}`
  }
  return { subject: nextSubject, body: nextBody }
}

const tokenGroups: { title: MessageKey; items: { token: string; hint: MessageKey }[] }[] = [
  {
    title: 'emailTokenGroupRecipient',
    items: [
      { token: '{userName}', hint: 'emailTokenUserName' },
      { token: '{displayName}', hint: 'emailTokenDisplayName' },
      { token: '{displayNameArabic}', hint: 'emailTokenDisplayNameArabic' },
      { token: '{email}', hint: 'emailTokenEmail' },
    ],
  },
  {
    title: 'emailTokenGroupReport',
    items: [
      { token: '{reportName}', hint: 'emailTokenReportName' },
      { token: '{reportNameEnglish}', hint: 'emailTokenReportNameEnglish' },
      { token: '{reportNameArabic}', hint: 'emailTokenReportNameArabic' },
      { token: '{reportDate}', hint: 'emailTokenReportDate' },
      { token: '{reportDateEnglish}', hint: 'emailTokenReportDateEnglish' },
      { token: '{reportDateArabic}', hint: 'emailTokenReportDateArabic' },
      { token: '{reportUrl}', hint: 'emailTokenReportUrl' },
      { token: '{fileNote}', hint: 'emailTokenFileNote' },
    ],
  },
  {
    title: 'emailTokenGroupBranding',
    items: [
      { token: '{applicationName}', hint: 'emailTokenApplicationName' },
      { token: '{applicationNameArabic}', hint: 'emailTokenApplicationNameArabic' },
      { token: '{studioName}', hint: 'emailTokenStudioName' },
      { token: '{studioNameArabic}', hint: 'emailTokenStudioNameArabic' },
      { token: '{organizationName}', hint: 'emailTokenOrganizationName' },
      { token: '{organizationNameArabic}', hint: 'emailTokenOrganizationNameArabic' },
      { token: '{logo}', hint: 'emailTokenLogo' },
    ],
  },
  {
    title: 'emailTokenGroupReset',
    items: [
      { token: '{resetUrl}', hint: 'emailTokenResetUrl' },
      { token: '{expiresMinutes}', hint: 'emailTokenExpiresMinutes' },
    ],
  },
]

const emptyDraft = (): EmailTemplateWrite & { id?: string } => ({
  name: '',
  subject: '',
  body: '',
  isHtml: false,
  isDefault: false,
})

export function EmailTemplateSettingsPanel() {
  const { can } = useAuth()
  const { t, lang } = useLocale()
  const queryClient = useQueryClient()
  const canUpdate = can('settings.update')
  const templates = useQuery({ queryKey: ['email-templates'], queryFn: api.listEmailTemplates })
  const branding = useQuery({ queryKey: ['branding'], queryFn: api.getBranding, staleTime: 60_000 })
  const [draft, setDraft] = useState(emptyDraft)
  const [showPreview, setShowPreview] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const bodyRef = useRef<HTMLTextAreaElement>(null)
  const preview = useMemo(
    () => previewMessage(draft.subject, draft.body, draft.isHtml, sampleValues(lang, branding.data)),
    [draft.subject, draft.body, draft.isHtml, lang, branding.data],
  )

  const save = useMutation({
    mutationFn: async () => {
      const body: EmailTemplateWrite = {
        name: draft.name,
        subject: draft.subject,
        body: draft.body,
        isHtml: draft.isHtml,
        isDefault: draft.isDefault,
      }
      if (draft.id) {
        return api.updateEmailTemplate(draft.id, body)
      }
      return api.createEmailTemplate(body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['email-templates'] })
      await queryClient.invalidateQueries({ queryKey: ['email-template-options'] })
      setDraft(emptyDraft())
      setShowPreview(false)
      setError(null)
      setNotice(t('emailTemplateSaved'))
    },
    onError: (err: Error) => {
      setNotice(null)
      setError(err.message)
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteEmailTemplate(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['email-templates'] })
      await queryClient.invalidateQueries({ queryKey: ['email-template-options'] })
      setDraft(emptyDraft())
      setShowPreview(false)
    },
    onError: (err: Error) => setError(err.message),
  })

  function edit(template: EmailTemplate) {
    setDraft({
      id: template.id,
      name: template.name,
      subject: template.subject,
      body: template.body,
      isHtml: template.isHtml,
      isDefault: template.isDefault,
    })
    setShowPreview(template.isHtml)
    setError(null)
    setNotice(null)
  }

  function insertToken(token: string) {
    const field = bodyRef.current
    const start = field?.selectionStart ?? draft.body.length
    const end = field?.selectionEnd ?? draft.body.length
    const next = `${draft.body.slice(0, start)}${token}${draft.body.slice(end)}`
    setDraft({ ...draft, body: next })
    requestAnimationFrame(() => {
      field?.focus()
      const cursor = start + token.length
      field?.setSelectionRange(cursor, cursor)
    })
  }

  return (
    <div className="mail-studio">
      <aside className="mail-catalog">
        <div className="mail-catalog-head">
          <h2>{t('emailTemplateTitle')}</h2>
          {canUpdate && (
            <button
              type="button"
              className="btn"
              onClick={() => {
                setDraft(emptyDraft())
                setShowPreview(false)
              }}
            >
              {t('emailTemplateAdd')}
            </button>
          )}
        </div>
        {(templates.data ?? []).length === 0 ? (
          <p className="mail-empty muted">{t('emailTemplateEmpty')}</p>
        ) : (
          <div className="mail-catalog-list">
            {(templates.data ?? []).map((template) => (
              <button
                key={template.id}
                type="button"
                className={`mail-item${draft.id === template.id ? ' selected' : ''}`}
                onClick={() => edit(template)}
              >
                <span className="mail-item-name">{template.name}</span>
                <span className="muted small mail-item-subject">{template.subject}</span>
                <span className="mail-item-meta">
                  <span className="badge draft">
                    {template.isHtml ? t('emailTemplateHtml') : t('emailTemplatePlain')}
                  </span>
                  {template.isDefault && <span className="badge table">{t('emailTemplateDefault')}</span>}
                </span>
              </button>
            ))}
          </div>
        )}
      </aside>
      {canUpdate && (
        <section className="settings-card mail-editor">
          <div className="settings-card-head">
            <h2>{draft.id ? draft.name || t('emailTemplateTitle') : t('emailTemplateAdd')}</h2>
          </div>
          {error && <p className="banner error">{error}</p>}
          {notice && <p className="banner">{notice}</p>}
          <div className="settings-fields">
            <Field label={t('emailTemplateName')}>
              <input value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value })} />
            </Field>
            <Field label={t('emailTemplateSubject')}>
              <input
                value={draft.subject}
                onChange={(event) => setDraft({ ...draft, subject: event.target.value })}
              />
            </Field>
          </div>
          <div className="mail-section">
            <div className="mail-section-title">{t('emailTemplateFormat')}</div>
            <div className="mail-format" role="group" aria-label={t('emailTemplateFormat')}>
              <button
                type="button"
                className={!draft.isHtml ? 'active' : ''}
                aria-pressed={!draft.isHtml}
                onClick={() => setDraft({ ...draft, isHtml: false })}
              >
                {t('emailTemplatePlain')}
              </button>
              <button
                type="button"
                className={draft.isHtml ? 'active' : ''}
                aria-pressed={draft.isHtml}
                onClick={() => {
                  setDraft({ ...draft, isHtml: true })
                  setShowPreview(true)
                }}
              >
                {t('emailTemplateHtml')}
              </button>
              <button
                type="button"
                className={`mail-preview-btn${showPreview ? ' active' : ''}`}
                aria-pressed={showPreview}
                onClick={() => setShowPreview((open) => !open)}
              >
                {t('preview')}
              </button>
            </div>
          </div>
          <Field label={t('emailTemplateBody')}>
            <textarea
              ref={bodyRef}
              className="mail-body"
              rows={12}
              value={draft.body}
              onChange={(event) => setDraft({ ...draft, body: event.target.value })}
            />
          </Field>
          <div className="mail-token-groups">
            {tokenGroups.map((group) => (
              <div key={group.title}>
                <div className="mail-token-group-title">{t(group.title)}</div>
                <div className="mail-tokens">
                  {group.items.map((item) => (
                    <button
                      key={item.token}
                      type="button"
                      className="mail-token"
                      title={t(item.hint)}
                      data-hint={t(item.hint)}
                      onClick={() => insertToken(item.token)}
                    >
                      {item.token}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>
          <p className="muted small">{t('emailTemplateTokens')}</p>
          {showPreview && (
            <div className="mail-preview">
              <div className="mail-preview-subject">
                <span className="muted small">{t('emailTemplateSubject')}</span>
                <strong>{preview.subject}</strong>
              </div>
              {draft.isHtml ? (
                <iframe
                  className="mail-preview-frame"
                  sandbox=""
                  title={t('preview')}
                  srcDoc={preview.body}
                />
              ) : (
                <pre className="mail-preview-text">{preview.body}</pre>
              )}
              <p className="muted small mail-preview-hint">{t('emailTemplatePreviewHint')}</p>
            </div>
          )}
          <label className="check-row">
            <input
              type="checkbox"
              checked={draft.isDefault}
              onChange={(event) => setDraft({ ...draft, isDefault: event.target.checked })}
            />
            {t('emailTemplateMakeDefault')}
          </label>
          <div className="row-actions">
            <button type="button" className="btn primary" disabled={save.isPending} onClick={() => save.mutate()}>
              {save.isPending ? t('saving') : t('saveSettings')}
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
