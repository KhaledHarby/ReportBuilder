import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { ReportDeliveryRow } from '../api/types'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'
import { formatStamp, relativeTime } from '../lib/format'
import type { MessageKey } from '../i18n/messages'
import type { Lang } from '../i18n/locale'

type Filter = 'all' | 'failed' | 'on' | 'off'
type SortKey = 'name' | 'schedule' | 'next' | 'last'
type SortDir = 'asc' | 'desc'
type Sort = { key: SortKey; dir: SortDir }

const DAY_KEYS = [
  'scheduleSunday',
  'scheduleMonday',
  'scheduleTuesday',
  'scheduleWednesday',
  'scheduleThursday',
  'scheduleFriday',
  'scheduleSaturday',
] as const satisfies readonly MessageKey[]

export function DeliveryPage() {
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState<Filter>('all')
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState<Sort | null>(null)
  const health = useQuery({
    queryKey: ['delivery-health'],
    queryFn: api.listDeliveryHealth,
  })
  const retry = useMutation({
    mutationFn: (id: string) => api.sendReportSchedule(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['delivery-health'] })
    },
  })

  const rows = health.data ?? []
  const failedCount = rows.filter((row) => row.lastStatus === 'Failed').length
  const onCount = rows.filter((row) => row.enabled && row.published).length
  const offCount = rows.length - onCount
  const visible = useMemo(() => {
    const q = query.trim().toLowerCase()
    return rows.filter((row) => {
      if (filter === 'failed' && row.lastStatus !== 'Failed') return false
      if (filter === 'on' && !(row.enabled && row.published)) return false
      if (filter === 'off' && row.enabled && row.published) return false
      if (!q) return true
      return `${row.name} ${row.nameArabic ?? ''}`.toLowerCase().includes(q)
    })
  }, [rows, filter, query])
  const sorted = useMemo(() => sortRows(visible, sort, lang), [visible, sort, lang])

  function toggleSort(key: SortKey) {
    setSort((current) => {
      if (current?.key !== key) {
        return { key, dir: key === 'last' ? 'desc' : 'asc' }
      }
      return { key, dir: current.dir === 'asc' ? 'desc' : 'asc' }
    })
  }

  return (
    <section className="page catalog delivery">
      <header className="page-header">
        <div>
          <div className="kicker">{t('deliveryKicker')}</div>
          <h1>{t('deliveryTitle')}</h1>
          <p className="lede">{t('deliveryLede')}</p>
        </div>
      </header>

      <div className="stats user-stats">
        <StatButton
          label={t('deliveryAll')}
          value={rows.length}
          active={filter === 'all'}
          onClick={() => setFilter('all')}
        />
        <StatButton
          label={t('deliveryFailed')}
          value={failedCount}
          active={filter === 'failed'}
          onClick={() => setFilter('failed')}
        />
        <StatButton
          label={t('deliveryOn')}
          value={onCount}
          active={filter === 'on'}
          onClick={() => setFilter('on')}
        />
        <StatButton
          label={t('deliveryOff')}
          value={offCount}
          active={filter === 'off'}
          onClick={() => setFilter('off')}
        />
      </div>

      {(health.isError || retry.isError) && (
        <div className="banner error">
          {((retry.error ?? health.error) as Error).message}
        </div>
      )}

      <div className="page-header catalog-toolbar">
        <h2>{t('deliveryKicker')}</h2>
        <input
          className="search"
          placeholder={t('search')}
          value={query}
          onChange={(event) => setQuery(event.target.value)}
        />
      </div>

      {health.isLoading ? (
        <Spinner page label={t('loadingApp')} />
      ) : rows.length === 0 ? (
        <p className="muted">{t('deliveryEmpty')}</p>
      ) : visible.length === 0 ? (
        <p className="muted">{t('deliveryNone')}</p>
      ) : (
        <div className="catalog-card">
          <table className="data-table">
            <thead>
              <tr>
                <SortHeader label={t('name')} column="name" sort={sort} onSort={toggleSort} />
                <SortHeader label={t('scheduleTitle')} column="schedule" sort={sort} onSort={toggleSort} />
                <SortHeader label={t('deliveryNext')} column="next" sort={sort} onSort={toggleSort} />
                <SortHeader label={t('deliveryLast')} column="last" sort={sort} onSort={toggleSort} />
                <th />
              </tr>
            </thead>
            <tbody>
              {sorted.map((row) => (
                <tr key={row.reportId} className={row.lastStatus === 'Failed' ? 'delivery-failed' : undefined}>
                  <td>
                    <div className="delivery-name">
                      <Link to={`/reports/${row.reportId}`}>
                        {localizedText(lang, row.name, row.nameArabic)}
                      </Link>
                      <span className={`badge ${row.published ? 'published' : 'draft'}`}>
                        {row.published ? t('published') : t('draft')}
                      </span>
                    </div>
                  </td>
                  <td>
                    <div>{cadenceLabel(row, t)}</div>
                    <div className="muted small delivery-sub">
                      {formatLabel(row.format, t)}
                      {' · '}
                      {t('deliveryRecipients', { n: row.recipientCount })}
                      {' · '}
                      {row.enabled && row.published ? t('deliveryOn') : t('deliveryOff')}
                    </div>
                  </td>
                  <td>
                    {row.nextRunAt ? (
                      <time dateTime={row.nextRunAt} title={formatStamp(row.nextRunAt, lang)}>
                        {relativeTime(row.nextRunAt, lang)}
                      </time>
                    ) : (
                      <span className="muted">{t('deliveryNoNext')}</span>
                    )}
                  </td>
                  <td>
                    <div className="delivery-last">
                      {row.lastStatus ? (
                        <span className={statusClass(row.lastStatus)}>{statusLabel(row.lastStatus, t)}</span>
                      ) : (
                        <span className="muted">{t('deliveryNever')}</span>
                      )}
                      {row.lastRunAt && (
                        <time dateTime={row.lastRunAt} title={formatStamp(row.lastRunAt, lang)}>
                          {relativeTime(row.lastRunAt, lang)}
                        </time>
                      )}
                    </div>
                    {row.lastMessage && (
                      <div className="muted small delivery-note" title={row.lastMessage}>
                        {row.lastMessage}
                      </div>
                    )}
                  </td>
                  <td className="delivery-action">
                    {row.busy ? (
                      <span className="badge warn">{t('deliveryBusy')}</span>
                    ) : row.canRetry ? (
                      <button
                        type="button"
                        className="btn"
                        disabled={retry.isPending}
                        onClick={() => retry.mutate(row.reportId)}
                      >
                        {retry.isPending && retry.variables === row.reportId
                          ? t('deliveryRetrying')
                          : t('deliveryRetry')}
                      </button>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

function SortHeader({
  label,
  column,
  sort,
  onSort,
}: {
  label: string
  column: SortKey
  sort: Sort | null
  onSort: (key: SortKey) => void
}) {
  const active = sort?.key === column
  return (
    <th aria-sort={!sort ? 'none' : active ? (sort.dir === 'asc' ? 'ascending' : 'descending') : 'none'}>
      <button type="button" className={`th-sort${active ? ' active' : ''}`} onClick={() => onSort(column)}>
        <span>{label}</span>
        <span className="th-sort-icon" aria-hidden="true">
          {active ? (sort.dir === 'asc' ? '↑' : '↓') : '↕'}
        </span>
      </button>
    </th>
  )
}

function sortRows(rows: ReportDeliveryRow[], sort: Sort | null, lang: Lang) {
  if (!sort) return rows
  const copy = rows.slice()
  copy.sort((a, b) => {
    const cmp =
      sort.key === 'name'
        ? localizedText(lang, a.name, a.nameArabic).localeCompare(
            localizedText(lang, b.name, b.nameArabic),
            lang,
          )
        : sort.key === 'schedule'
          ? scheduleCmp(a, b)
          : timeCmp(
              sort.key === 'next' ? a.nextRunAt : a.lastRunAt,
              sort.key === 'next' ? b.nextRunAt : b.lastRunAt,
              sort.dir,
            )
    if (sort.key === 'next' || sort.key === 'last') {
      return cmp || a.name.localeCompare(b.name, lang)
    }
    if (cmp !== 0) return sort.dir === 'asc' ? cmp : -cmp
    return a.name.localeCompare(b.name, lang)
  })
  return copy
}

function scheduleCmp(a: ReportDeliveryRow, b: ReportDeliveryRow) {
  const rank = scheduleRank(a) - scheduleRank(b)
  if (rank !== 0) return rank
  const day = (a.dayOfWeek ?? a.dayOfMonth ?? 0) - (b.dayOfWeek ?? b.dayOfMonth ?? 0)
  if (day !== 0) return day
  return a.localTime.localeCompare(b.localTime)
}

function timeCmp(a: string | null | undefined, b: string | null | undefined, dir: SortDir) {
  const left = a ? Date.parse(a) : Number.NaN
  const right = b ? Date.parse(b) : Number.NaN
  const leftMissing = Number.isNaN(left)
  const rightMissing = Number.isNaN(right)
  if (leftMissing || rightMissing) {
    if (leftMissing && rightMissing) return 0
    return leftMissing ? 1 : -1
  }
  const diff = left - right
  return dir === 'asc' ? diff : -diff
}

function scheduleRank(row: ReportDeliveryRow) {
  if (row.cadence === 'Weekly') return 1
  if (row.cadence === 'Monthly') return 2
  return 0
}

function StatButton({
  label,
  value,
  active,
  onClick,
}: {
  label: string
  value: number
  active: boolean
  onClick: () => void
}) {
  return (
    <button type="button" className={`stat${active ? ' is-on' : ''}`} aria-pressed={active} onClick={onClick}>
      <span className="muted small">{label}</span>
      <b>{value}</b>
    </button>
  )
}

function cadenceLabel(row: ReportDeliveryRow, t: (key: MessageKey, vars?: Record<string, string | number>) => string) {
  const time = row.localTime
  if (row.cadence === 'Weekly') {
    const day = DAY_KEYS[row.dayOfWeek ?? 0] ?? 'scheduleSunday'
    return `${t('scheduleWeekly')} · ${t(day)} · ${time}`
  }
  if (row.cadence === 'Monthly') {
    return `${t('scheduleMonthly')} · ${row.dayOfMonth ?? 1} · ${time}`
  }
  return `${t('scheduleDaily')} · ${time}`
}

function formatLabel(format: string, t: (key: MessageKey) => string) {
  if (format === 'Pdf') return t('schedulePdf')
  if (format === 'Xlsx') return t('scheduleExcel')
  return t('scheduleCsv')
}

function statusClass(status: string) {
  if (status === 'Failed') return 'badge error'
  if (status === 'Sent') return 'badge published'
  return 'badge draft'
}

function statusLabel(status: string, t: (key: MessageKey) => string) {
  if (status === 'Failed') return t('scheduleRunFailed')
  if (status === 'Sent') return t('scheduleRunSent')
  if (status === 'Skipped') return t('scheduleRunSkipped')
  return status
}
