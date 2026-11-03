import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Fragment, useState } from 'react'
import { api } from '../api/client'
import type { ActivityLogRow } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { useLocale } from '../i18n/LocaleProvider'
import type { MessageKey } from '../i18n/messages'
import { formatStamp } from '../lib/format'
import { IconInfo } from '../components/Icons'
import { Spinner } from '../components/Spinner'

const PAGE_SIZE = 50

const LEVELS = ['Information', 'Warning', 'Error'] as const
const CATEGORIES = [
  'auth',
  'users',
  'roles',
  'permissions',
  'reports',
  'mail',
  'datasets',
  'analytics',
  'ai',
  'http',
  'system',
] as const

const categoryKeys: Record<(typeof CATEGORIES)[number], MessageKey> = {
  auth: 'logCatAuth',
  users: 'logCatUsers',
  roles: 'logCatRoles',
  permissions: 'logCatPermissions',
  reports: 'logCatReports',
  mail: 'logCatMail',
  datasets: 'logCatDatasets',
  analytics: 'logCatAnalytics',
  ai: 'logCatAi',
  http: 'logCatHttp',
  system: 'logCatSystem',
}

const levelKeys: Record<(typeof LEVELS)[number], MessageKey> = {
  Information: 'logLevelInformation',
  Warning: 'logLevelWarning',
  Error: 'logLevelError',
}

function subjectUser(row: ActivityLogRow) {
  if (row.targetUserId) {
    return {
      id: row.targetUserId,
      name: row.targetUserName || row.actorUserName || row.targetUserId,
    }
  }
  if (row.category === 'auth' && row.actorUserId) {
    return { id: row.actorUserId, name: row.actorUserName || row.actorUserId }
  }
  return null
}

function levelClass(level: string) {
  if (level === 'Error') {
    return 'badge error'
  }
  if (level === 'Warning') {
    return 'badge warn'
  }
  return 'badge'
}

function toIso(local: string) {
  if (!local) {
    return undefined
  }
  const value = new Date(local)
  return Number.isNaN(value.getTime()) ? undefined : value.toISOString()
}

export function ActivityPage() {
  const { user: current, can } = useAuth()
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const [skip, setSkip] = useState(0)
  const [q, setQ] = useState('')
  const [level, setLevel] = useState('')
  const [category, setCategory] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [retentionDraft, setRetentionDraft] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)

  const canManage = can('logs.manage')

  const logs = useQuery({
    queryKey: ['logs', skip, q, level, category, from, to],
    queryFn: () =>
      api.listLogs({
        skip,
        take: PAGE_SIZE,
        q: q.trim() || undefined,
        level: level || undefined,
        category: category || undefined,
        from: toIso(from),
        to: toIso(to),
      }),
  })

  const settings = useQuery({
    queryKey: ['log-settings'],
    queryFn: api.getLogSettings,
  })

  const retention = retentionDraft ?? settings.data?.retentionDays ?? 30

  const saveRetention = useMutation({
    mutationFn: () => api.updateLogSettings(retention),
    onSuccess: (saved) => {
      queryClient.setQueryData(['log-settings'], saved)
      queryClient.invalidateQueries({ queryKey: ['logs'] })
      setRetentionDraft(saved.retentionDays)
      setError(null)
    },
    onError: (err) => setError((err as Error).message),
  })

  const items = logs.data?.items ?? []
  const total = logs.data?.total ?? 0
  const fromRow = total === 0 ? 0 : skip + 1
  const toRow = Math.min(skip + items.length, total)

  async function deactivate(id: string, name: string) {
    if (!confirm(t('deactivateUserConfirm', { name }))) {
      return
    }
    try {
      await api.activateUser(id, false)
      queryClient.invalidateQueries({ queryKey: ['logs'] })
      queryClient.invalidateQueries({ queryKey: ['users'] })
      setError(null)
    } catch (err) {
      setError((err as Error).message)
    }
  }

  async function signOut(id: string, name: string) {
    if (!confirm(t('signOutEverywhereConfirm', { name }))) {
      return
    }
    try {
      await api.signOutUser(id)
      queryClient.invalidateQueries({ queryKey: ['logs'] })
      setError(null)
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <section className="page activity">
      <header className="page-header">
        <div>
          <div className="kicker">{t('activityKicker')}</div>
          <h1>{t('activityTitle')}</h1>
          <p className="lede">{t('activityLede')}</p>
        </div>
        {settings.data && (
          <form
            className="activity-retention"
            onSubmit={(event) => {
              event.preventDefault()
              if (canManage) {
                saveRetention.mutate()
              }
            }}
          >
            <label>
              {t('logRetention')}
              <input
                type="number"
                min={settings.data.minRetentionDays}
                max={settings.data.maxRetentionDays}
                value={retention}
                disabled={!canManage || saveRetention.isPending}
                onChange={(event) => setRetentionDraft(Number(event.target.value))}
              />
            </label>
            {canManage && (
              <button type="submit" className="btn" disabled={saveRetention.isPending}>
                {saveRetention.isPending ? t('saving') : t('save')}
              </button>
            )}
            <p className="muted small">{t('logRetentionHint')}</p>
          </form>
        )}
      </header>

      {(logs.isError || error) && (
        <div className="banner error">{error || (logs.error as Error).message}</div>
      )}

      <div className="activity-filters">
        <input
          className="search"
          placeholder={t('logSearch')}
          value={q}
          onChange={(event) => {
            setSkip(0)
            setQ(event.target.value)
          }}
        />
        <label>
          {t('logLevel')}
          <select
            value={level}
            onChange={(event) => {
              setSkip(0)
              setLevel(event.target.value)
            }}
          >
            <option value="">{t('logAllLevels')}</option>
            {LEVELS.map((item) => (
              <option key={item} value={item}>
                {t(levelKeys[item])}
              </option>
            ))}
          </select>
        </label>
        <label>
          {t('logCategory')}
          <select
            value={category}
            onChange={(event) => {
              setSkip(0)
              setCategory(event.target.value)
            }}
          >
            <option value="">{t('logAllCategories')}</option>
            {CATEGORIES.map((item) => (
              <option key={item} value={item}>
                {t(categoryKeys[item])}
              </option>
            ))}
          </select>
        </label>
        <label>
          {t('logFrom')}
          <input
            type="datetime-local"
            value={from}
            onChange={(event) => {
              setSkip(0)
              setFrom(event.target.value)
            }}
          />
        </label>
        <label>
          {t('logTo')}
          <input
            type="datetime-local"
            value={to}
            onChange={(event) => {
              setSkip(0)
              setTo(event.target.value)
            }}
          />
        </label>
      </div>

      {logs.isLoading ? (
        <Spinner page label={t('loadingApp')} />
      ) : (
        <div className="activity-panel">
          <div className="activity-table-scroll">
            <table className="data-table compact">
              <thead>
                <tr>
                  <th>{t('logTime')}</th>
                  <th>{t('logLevel')}</th>
                  <th>{t('logCategory')}</th>
                  <th>{t('logActor')}</th>
                  <th>{t('logAction')}</th>
                  <th>{t('logMessage')}</th>
                  <th>{t('logStatus')}</th>
                  {canManage && <th />}
                </tr>
              </thead>
              <tbody>
                {items.map((row) => {
                  const open = openId === row.id
                  const subject = subjectUser(row)
                  const canAct = Boolean(canManage && subject && subject.id !== current?.id)
                  return (
                    <Fragment key={row.id}>
                      <tr
                        className={open ? 'log-row is-open' : 'log-row'}
                        onClick={() => setOpenId(open ? null : row.id)}
                      >
                        <td>{formatStamp(row.occurredAt, lang)}</td>
                        <td>
                          <span className={levelClass(row.level)}>
                            {levelKeys[row.level as keyof typeof levelKeys]
                              ? t(levelKeys[row.level as keyof typeof levelKeys])
                              : row.level}
                          </span>
                        </td>
                        <td>
                          {categoryKeys[row.category as keyof typeof categoryKeys]
                            ? t(categoryKeys[row.category as keyof typeof categoryKeys])
                            : row.category}
                        </td>
                        <td>{row.actorUserName || '—'}</td>
                        <td>
                          <code>{row.action}</code>
                          {exportKind(row) && <span className="badge">{exportKind(row)}</span>}
                        </td>
                        <td className="log-message">{row.message}</td>
                        <td>{row.statusCode ?? '—'}</td>
                        {canManage && (
                          <td>
                            {canAct && subject && (
                              <div
                                className="row-actions"
                                onClick={(event) => event.stopPropagation()}
                              >
                                <button
                                  type="button"
                                  className="btn"
                                  onClick={() => void deactivate(subject.id, subject.name)}
                                >
                                  {t('deactivate')}
                                </button>
                                <button
                                  type="button"
                                  className="btn"
                                  onClick={() => void signOut(subject.id, subject.name)}
                                >
                                  {t('signOutEverywhere')}
                                </button>
                              </div>
                            )}
                          </td>
                        )}
                      </tr>
                      {open && (
                        <tr className="log-detail-row">
                          <td colSpan={canManage ? 8 : 7}>
                            <LogDetail row={row} />
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  )
                })}
              </tbody>
            </table>
          </div>
          {!items.length && (
            <div className="empty">
              <IconInfo />
              <h2>{t('logNoRows')}</h2>
              <p>{t('logNoRowsHint')}</p>
            </div>
          )}
        </div>
      )}

      {total > 0 && (
        <div className="pager">
          <span className="muted">{t('pagerRange', { from: fromRow, to: toRow, total })}</span>
          <div className="pager-actions">
            <button
              type="button"
              className="btn"
              disabled={skip === 0}
              onClick={() => setSkip(Math.max(0, skip - PAGE_SIZE))}
            >
              {t('previous')}
            </button>
            <button
              type="button"
              className="btn"
              disabled={skip + PAGE_SIZE >= total}
              onClick={() => setSkip(skip + PAGE_SIZE)}
            >
              {t('next')}
            </button>
          </div>
        </div>
      )}
    </section>
  )
}

function exportKind(row: ActivityLogRow) {
  if (row.action !== 'report.export' || !row.details) {
    return null
  }
  try {
    const parsed = JSON.parse(row.details) as { format?: string }
    switch ((parsed.format ?? '').toLowerCase()) {
      case 'pdf':
        return 'pdf'
      case 'csv':
        return 'csv'
      case 'xlsx':
      case 'excel':
        return 'excel'
      case 'docx':
      case 'word':
        return 'word'
      default:
        return parsed.format?.trim() || null
    }
  } catch {
    return null
  }
}

function LogDetail({ row }: { row: ActivityLogRow }) {
  const { t } = useLocale()
  const bits = [
    row.targetUserName ? [t('logTarget'), row.targetUserName] : null,
    row.entityName ? [t('logEntity'), row.entityName] : null,
    row.path ? [t('logPath'), `${row.httpMethod ?? ''} ${row.path}`.trim()] : null,
    row.ipAddress ? [t('logIp'), row.ipAddress] : null,
    row.correlationId ? [t('logCorrelation'), row.correlationId] : null,
    row.durationMs != null ? [t('logDuration'), t('logMs', { n: row.durationMs })] : null,
    row.details ? [t('logDetails'), row.details] : null,
  ].filter(Boolean) as [string, string][]

  if (!bits.length) {
    return <p className="muted small">{row.message}</p>
  }

  return (
    <dl className="log-detail">
      {bits.map(([label, value]) => (
        <div key={label}>
          <dt>{label}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  )
}
