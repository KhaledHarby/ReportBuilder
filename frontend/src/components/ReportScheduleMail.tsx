import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { ReportScheduleWrite, UserScopeProperty } from '../api/types'
import { Field } from './AdminFields'
import { localizedText } from '../i18n/localized'
import { useLocale } from '../i18n/LocaleProvider'
import type { MessageKey } from '../i18n/messages'
import { scheduleScopeProperties, userScopePropertyLabelKey } from '../lib/userScope'

function isEmailAddress(value: string) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)
}

const days: MessageKey[] = [
  'scheduleSunday',
  'scheduleMonday',
  'scheduleTuesday',
  'scheduleWednesday',
  'scheduleThursday',
  'scheduleFriday',
  'scheduleSaturday',
]

export function ReportScheduleMail({ reportId }: { reportId: string }) {
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const options = useQuery({ queryKey: ['smtp-options'], queryFn: api.listSmtpOptions })
  const templates = useQuery({ queryKey: ['email-template-options'], queryFn: api.listEmailTemplateOptions })
  const schedule = useQuery({
    queryKey: ['report-schedule', reportId],
    queryFn: () => api.getReportSchedule(reportId),
  })
  const [enabled, setEnabled] = useState(false)
  const [cadence, setCadence] = useState('Daily')
  const [dayOfWeek, setDayOfWeek] = useState(1)
  const [dayOfMonth, setDayOfMonth] = useState(1)
  const [localTime, setLocalTime] = useState('08:00')
  const [format, setFormat] = useState('Csv')
  const [language, setLanguage] = useState('en')
  const [smtpProfileId, setSmtpProfileId] = useState('')
  const [emailTemplateId, setEmailTemplateId] = useState('')
  const [userIds, setUserIds] = useState<string[]>([])
  const [roleIds, setRoleIds] = useState<string[]>([])
  const [emails, setEmails] = useState<string[]>([])
  const [scopeField, setScopeField] = useState('')
  const [scopeProperty, setScopeProperty] = useState<UserScopeProperty>('userName')
  const [draftEmail, setDraftEmail] = useState('')
  const [roleSearch, setRoleSearch] = useState('')
  const [personSearch, setPersonSearch] = useState('')
  const [notice, setNotice] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setRoleSearch('')
    setPersonSearch('')
  }, [reportId])

  useEffect(() => {
    const data = schedule.data
    if (!data) {
      return
    }
    setEnabled(data.enabled)
    setCadence(data.cadence || 'Daily')
    setDayOfWeek(data.dayOfWeek ?? 1)
    setDayOfMonth(data.dayOfMonth ?? 1)
    setLocalTime((data.localTime || '08:00').slice(0, 5))
    setFormat(data.format || 'Csv')
    setLanguage(data.language || 'en')
    setSmtpProfileId(data.smtpProfileId ?? '')
    setEmailTemplateId(data.emailTemplateId ?? '')
    setUserIds(data.recipients.map((item) => item.userId).filter((id): id is string => Boolean(id)))
    setRoleIds(data.recipients.map((item) => item.roleId).filter((id): id is string => Boolean(id)))
    setEmails(data.recipients.map((item) => item.email).filter((email): email is string => Boolean(email)))
    setScopeField(data.scopeField ?? '')
    setScopeProperty((data.scopeProperty as UserScopeProperty) || 'userName')
  }, [schedule.data])

  const save = useMutation({
    mutationFn: () => {
      const body: ReportScheduleWrite = {
        enabled,
        cadence,
        dayOfWeek: cadence === 'Weekly' ? dayOfWeek : null,
        dayOfMonth: cadence === 'Monthly' ? dayOfMonth : null,
        localTime: localTime.slice(0, 5),
        format,
        language,
        smtpProfileId: smtpProfileId || null,
        emailTemplateId: emailTemplateId || null,
        userIds,
        roleIds,
        emails: schedule.data?.userScoped || scopeField ? [] : emails,
        scopeField: scopeField || null,
        scopeProperty: scopeField ? scopeProperty : null,
      }
      return api.saveReportSchedule(reportId, body)
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['report-schedule', reportId], data)
      setError(null)
      setNotice(t('scheduleSaved'))
    },
    onError: (err: Error) => {
      setNotice(null)
      setError(err.message)
    },
  })

  const send = useMutation({
    mutationFn: () => api.sendReportSchedule(reportId),
    onSuccess: (data) => {
      queryClient.setQueryData(['report-schedule', reportId], data)
      const latest = data.runs[0]
      if (latest?.status === 'Sent') {
        setError(null)
        setNotice(t('scheduleSent'))
      } else {
        setNotice(null)
        setError(latest?.message || t('scheduleRunFailed'))
      }
    },
    onError: (err: Error) => {
      setNotice(null)
      setError(err.message)
    },
  })

  const servers = options.data ?? []
  const defaultName = servers.find((item) => item.isDefault)?.name
  const templateOptions = templates.data ?? []
  const defaultTemplate = templateOptions.find((item) => item.isDefault)?.name
  const people = schedule.data?.people ?? []
  const roles = schedule.data?.roles ?? []
  const userScoped = schedule.data?.userScoped === true
  const scopeColumns = schedule.data?.scopeColumns ?? []
  const scopeColumn = scopeColumns.find(
    (column) => column.name.toLowerCase() === scopeField.toLowerCase(),
  )
  const scopeProperties = scopeColumn ? scheduleScopeProperties(scopeColumn.sqlType) : []
  const ownRows = userScoped || Boolean(scopeField)
  const roleQuery = roleSearch.trim().toLowerCase()
  const personQuery = personSearch.trim().toLowerCase()
  const visibleRoles = useMemo(
    () =>
      roles.filter((role) =>
        roleQuery ? role.name.toLowerCase().includes(roleQuery) : true,
      ),
    [roles, roleQuery],
  )
  const visiblePeople = useMemo(
    () =>
      people.filter((person) => {
        if (!personQuery) {
          return true
        }
        return `${person.displayName} ${person.userName} ${person.email ?? ''}`
          .toLowerCase()
          .includes(personQuery)
      }),
    [people, personQuery],
  )

  function toggleUser(id: string) {
    setNotice(null)
    setUserIds((current) => (current.includes(id) ? current.filter((item) => item !== id) : [...current, id]))
  }

  function toggleRole(id: string) {
    setNotice(null)
    setRoleIds((current) => (current.includes(id) ? current.filter((item) => item !== id) : [...current, id]))
  }

  function addEmail() {
    const next = draftEmail.trim()
    if (!next) {
      return
    }
    if (!isEmailAddress(next)) {
      setNotice(null)
      setError(t('scheduleEmailInvalid'))
      return
    }
    setDraftEmail('')
    setError(null)
    setNotice(null)
    if (emails.some((item) => item.toLowerCase() === next.toLowerCase())) {
      return
    }
    setEmails((current) => [...current, next])
  }

  return (
      <div className="schedule-panel">
        <p className="muted small">{t('scheduleHint')}</p>
        {schedule.data && !schedule.data.published && <p className="muted small">{t('scheduleDraft')}</p>}
        {userScoped && <p className="muted small">{t('scheduleUserScoped')}</p>}
        <section className="schedule-own-data">
          <div>
            <div className="schedule-people-title">{t('scheduleOwnData')}</div>
            <p className="muted small">{t('scheduleOwnDataHint')}</p>
          </div>
          <div className="schedule-own-data-fields">
            <Field label={t('scheduleScopeColumn')}>
              <select
                value={scopeField}
                onChange={(event) => {
                  const next = event.target.value
                  const column = scopeColumns.find((item) => item.name === next)
                  const properties = column ? scheduleScopeProperties(column.sqlType) : []
                  setScopeField(next)
                  setScopeProperty(properties[0] ?? 'userName')
                }}
              >
                <option value="">{t('scheduleScopeNone')}</option>
                {scopeColumns.map((column) => (
                  <option key={column.name} value={column.name}>
                    {localizedText(lang, column.label, column.labelArabic)} ({column.name})
                  </option>
                ))}
              </select>
            </Field>
            <Field label={t('scheduleScopeProperty')}>
              <select
                value={scopeField && scopeProperties.includes(scopeProperty) ? scopeProperty : (scopeProperties[0] ?? '')}
                disabled={!scopeField || scopeProperties.length === 0}
                onChange={(event) => setScopeProperty(event.target.value as UserScopeProperty)}
              >
                {!scopeField && <option value="">—</option>}
                {scopeProperties.map((property) => (
                  <option key={property} value={property}>
                    {t(userScopePropertyLabelKey(property))}
                  </option>
                ))}
              </select>
            </Field>
          </div>
        </section>
        <label className="check-row">
          <input type="checkbox" checked={enabled} onChange={(event) => setEnabled(event.target.checked)} />
          {t('scheduleEnabled')}
        </label>
        <div className="schedule-grid">
          <Field label={t('scheduleCadence')}>
            <select value={cadence} onChange={(event) => setCadence(event.target.value)}>
              <option value="Daily">{t('scheduleDaily')}</option>
              <option value="Weekly">{t('scheduleWeekly')}</option>
              <option value="Monthly">{t('scheduleMonthly')}</option>
            </select>
          </Field>
          {cadence === 'Weekly' && (
            <Field label={t('scheduleDay')}>
              <select value={dayOfWeek} onChange={(event) => setDayOfWeek(Number(event.target.value))}>
                {days.map((key, index) => (
                  <option key={key} value={index}>
                    {t(key)}
                  </option>
                ))}
              </select>
            </Field>
          )}
          {cadence === 'Monthly' && (
            <Field label={t('scheduleDayOfMonth')}>
              <input
                type="number"
                min={1}
                max={31}
                value={dayOfMonth}
                onChange={(event) => setDayOfMonth(Number(event.target.value) || 1)}
              />
            </Field>
          )}
          <Field label={t('scheduleTime')}>
            <input type="time" value={localTime} onChange={(event) => setLocalTime(event.target.value)} />
          </Field>
          <Field label={t('scheduleFormat')}>
            <select value={format} onChange={(event) => setFormat(event.target.value)}>
              <option value="Csv">{t('scheduleCsv')}</option>
              <option value="Xlsx">{t('scheduleExcel')}</option>
              <option value="Pdf">{t('schedulePdf')}</option>
            </select>
          </Field>
          <Field label={t('scheduleLanguage')}>
            <select value={language} onChange={(event) => setLanguage(event.target.value)}>
              <option value="en">{t('scheduleEnglish')}</option>
              <option value="ar">{t('scheduleArabic')}</option>
            </select>
          </Field>
          <Field label={t('scheduleSmtp')}>
            <select
              value={smtpProfileId}
              disabled={servers.length === 0}
              onChange={(event) => setSmtpProfileId(event.target.value)}
            >
              <option value="">
                {defaultName ? `${t('scheduleSmtpDefault')} (${defaultName})` : t('scheduleSmtpDefault')}
              </option>
              {servers.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                  {item.isDefault ? ` · ${t('smtpDefault')}` : ''}
                </option>
              ))}
            </select>
          </Field>
          <Field label={t('scheduleEmailTemplate')}>
            <select value={emailTemplateId} onChange={(event) => setEmailTemplateId(event.target.value)}>
              <option value="">
                {defaultTemplate
                  ? `${t('scheduleEmailTemplateDefault')} (${defaultTemplate})`
                  : t('scheduleEmailTemplateDefault')}
              </option>
              {templateOptions.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                  {item.isHtml ? ` · ${t('emailTemplateHtml')}` : ''}
                  {item.isDefault ? ` · ${t('emailTemplateDefault')}` : ''}
                </option>
              ))}
            </select>
          </Field>
        </div>
        <p className="muted small">{servers.length === 0 ? t('scheduleSmtpNone') : t('scheduleSmtpHint')}</p>
        <p className="muted small">
          {templateOptions.length === 0 ? t('scheduleEmailTemplateNone') : t('scheduleEmailTemplateHint')}
        </p>
        <div className="schedule-picker">
          <div className="schedule-people-title">
            {t('scheduleRoles')}
            {roleIds.length > 0 ? ` · ${t('scheduleSelectedCount', { n: roleIds.length })}` : ''}
          </div>
          <input
            className="search schedule-picker-search"
            value={roleSearch}
            placeholder={t('scheduleSearchRoles')}
            onChange={(event) => setRoleSearch(event.target.value)}
          />
          <p className="muted small">{t('scheduleRoleHint')}</p>
          <div className="schedule-people">
            {roles.length > 0 && visibleRoles.length === 0 && (
              <p className="muted small">{t('scheduleNoneMatch')}</p>
            )}
            {visibleRoles.map((role) => (
              <label key={role.id} className="check-row">
                <input
                  type="checkbox"
                  checked={roleIds.includes(role.id)}
                  onChange={() => toggleRole(role.id)}
                />
                <span>
                  {role.name}
                  <span className="muted small"> · {t('scheduleRoleCount', { n: role.activeCount })}</span>
                </span>
              </label>
            ))}
          </div>
        </div>
        <div className="schedule-picker">
          <div className="schedule-people-title">
            {t('scheduleRecipients')}
            {userIds.length > 0 ? ` · ${t('scheduleSelectedCount', { n: userIds.length })}` : ''}
          </div>
          <input
            className="search schedule-picker-search"
            value={personSearch}
            placeholder={t('scheduleSearchPeople')}
            onChange={(event) => setPersonSearch(event.target.value)}
          />
          <div className="schedule-people">
            {people.length === 0 && <p className="muted small">{t('noMatchingPeople')}</p>}
            {people.length > 0 && visiblePeople.length === 0 && (
              <p className="muted small">{t('scheduleNoneMatch')}</p>
            )}
            {visiblePeople.map((person) => (
              <label key={person.id} className="check-row">
                <input
                  type="checkbox"
                  checked={userIds.includes(person.id)}
                  disabled={!person.email}
                  onChange={() => toggleUser(person.id)}
                />
                <span>
                  {person.displayName}
                  {person.userName && person.userName !== person.displayName ? (
                    <span className="muted small"> · {person.userName}</span>
                  ) : null}
                  <span className="muted small"> · {person.email || t('scheduleNoEmail')}</span>
                </span>
              </label>
            ))}
          </div>
        </div>
        {!ownRows && (
          <div className="schedule-added">
            <div className="schedule-people-title">{t('scheduleAdded')}</div>
            <div className="schedule-email-row">
              <input
                value={draftEmail}
                placeholder={t('scheduleEmailPlaceholder')}
                onChange={(event) => {
                  setDraftEmail(event.target.value)
                  setError((current) => (current === t('scheduleEmailInvalid') ? null : current))
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    event.preventDefault()
                    addEmail()
                  }
                }}
              />
              <button type="button" className="btn" onClick={addEmail}>
                {t('scheduleAddEmail')}
              </button>
            </div>
            <div className="schedule-email-list">
              {emails.map((email) => (
                <span key={email} className="schedule-email-chip">
                  <span>{email}</span>
                  <button
                    type="button"
                    className="schedule-email-remove"
                    aria-label={t('removeNamed', { name: email })}
                    title={t('remove')}
                    onClick={() => setEmails((current) => current.filter((item) => item !== email))}
                  >
                    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
                      <path d="M6 6l12 12M18 6 6 18" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
                    </svg>
                  </button>
                </span>
              ))}
            </div>
          </div>
        )}
        {error && <p className="banner error">{error}</p>}
        {notice && <p className="banner">{notice}</p>}
        <div className="schedule-email-row">
          <button type="button" className="btn primary" disabled={save.isPending || send.isPending} onClick={() => save.mutate()}>
            {save.isPending ? t('saving') : t('saveSettings')}
          </button>
          <button
            type="button"
            className="btn"
            disabled={send.isPending || save.isPending || !schedule.data?.published || (schedule.data?.recipients.length ?? 0) === 0}
            onClick={() => send.mutate()}
          >
            {send.isPending ? t('scheduleSending') : t('scheduleSendNow')}
          </button>
        </div>
      </div>
  )
}
