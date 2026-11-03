import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { AnalyticsUsageInsight, DatasetFreshnessInsight, ReportUsageInsight } from '../api/types'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'
import { formatStamp, relativeTime } from '../lib/format'
import type { Lang } from '../i18n/locale'
import type { MessageKey } from '../i18n/messages'

type UsageFilter = 'active' | 'unused' | 'all'
type FreshnessFilter = 'attention' | 'cached' | 'all'
type UsageSort = 'activity' | 'runs' | 'exports' | 'name' | 'recent'
type AnalyticsSort = 'activity' | 'views' | 'queries' | 'exports' | 'name' | 'recent'

const STALE_HOURS = 24

function formatWhen(
  value: string | null | undefined,
  neverLabel: string,
  lang: Lang,
) {
  if (!value) return { primary: neverLabel, title: neverLabel }
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return { primary: neverLabel, title: neverLabel }
  return {
    primary: relativeTime(value, lang),
    title: formatStamp(value, lang),
  }
}

function isStale(row: DatasetFreshnessInsight) {
  if (!row.isCached || row.cacheStatus !== 'Ready' || !row.lastSuccessAt) {
    return false
  }
  const ageMs = Date.now() - new Date(row.lastSuccessAt).getTime()
  return ageMs > STALE_HOURS * 60 * 60 * 1000
}

function needsAttention(row: DatasetFreshnessInsight) {
  if (!row.isCached) return false
  return (
    row.cacheStatus === 'Failed'
    || row.cacheStatus === 'Refreshing'
    || Boolean(row.lastError)
    || isStale(row)
  )
}

function UsageBar({ value, max }: { value: number; max: number }) {
  const width = max <= 0 ? 0 : Math.max(6, Math.round((value / max) * 100))
  return (
    <div className="insights-bar" aria-hidden="true">
      <span style={{ width: `${width}%` }} />
    </div>
  )
}

function StatusBadge({
  status,
  t,
}: {
  status: string
  t: (key: MessageKey, vars?: Record<string, string | number>) => string
}) {
  const key =
    status === 'Published'
      ? 'published'
      : status === 'Draft'
        ? 'draft'
        : null
  return (
    <span className={`badge ${status === 'Published' ? 'published' : 'draft'}`}>
      {key ? t(key) : status}
    </span>
  )
}

function ServingBadge({
  servingFrom,
  t,
}: {
  servingFrom: string
  t: (key: MessageKey) => string
}) {
  const fromCache = servingFrom === 'cache'
  return (
    <span className={`badge ${fromCache ? 'published' : 'draft'}`}>
      {fromCache ? t('datasetCacheServingCache') : t('datasetCacheServingLive')}
    </span>
  )
}

function CacheStatusBadge({
  row,
  t,
}: {
  row: DatasetFreshnessInsight
  t: (key: MessageKey) => string
}) {
  if (!row.isCached) {
    return <span className="badge draft">{t('insightsCacheOff')}</span>
  }
  if (row.cacheStatus === 'Ready' && isStale(row)) {
    return <span className="badge warn">{t('insightsStale')}</span>
  }
  if (row.cacheStatus === 'Failed') {
    return <span className="badge error">{t('insightsFailed')}</span>
  }
  if (row.cacheStatus === 'Ready') {
    return <span className="badge published">{t('insightsReady')}</span>
  }
  if (row.cacheStatus === 'Refreshing') {
    return <span className="badge warn">{t('insightsRefreshing')}</span>
  }
  return <span className="badge draft">{row.cacheStatus ?? t('insightsCacheOff')}</span>
}

export function InsightsPage() {
  const { lang, t } = useLocale()
  const [days, setDays] = useState(30)
  const [usageFilter, setUsageFilter] = useState<UsageFilter>('active')
  const [freshnessFilter, setFreshnessFilter] = useState<FreshnessFilter>('all')
  const [usageSort, setUsageSort] = useState<UsageSort>('activity')
  const [usageQuery, setUsageQuery] = useState('')
  const [freshnessQuery, setFreshnessQuery] = useState('')
  const [analyticsFilter, setAnalyticsFilter] = useState<UsageFilter>('active')
  const [analyticsSort, setAnalyticsSort] = useState<AnalyticsSort>('activity')
  const [analyticsQuery, setAnalyticsQuery] = useState('')

  const insights = useQuery({
    queryKey: ['insights', days],
    queryFn: () => api.getInsights(days),
  })

  const summary = insights.data?.summary
  const reports = insights.data?.reports ?? []
  const analytics = insights.data?.analytics ?? []
  const freshness = insights.data?.freshness ?? []

  const unusedCount = useMemo(
    () => reports.filter((row) => row.runCount === 0 && row.exportCount === 0).length,
    [reports],
  )
  const unusedAnalyticsCount = useMemo(
    () =>
      analytics.filter(
        (row) => row.viewCount === 0 && row.queryCount === 0 && row.exportCount === 0,
      ).length,
    [analytics],
  )
  const attentionCount = useMemo(
    () => freshness.filter(needsAttention).length,
    [freshness],
  )
  const maxActivity = useMemo(
    () => Math.max(1, ...reports.map((row) => row.runCount + row.exportCount)),
    [reports],
  )

  const filteredReports = useMemo(() => {
    const q = usageQuery.trim().toLowerCase()
    let list = reports.slice()
    if (usageFilter === 'active') {
      list = list.filter((row) => row.runCount > 0 || row.exportCount > 0)
    } else if (usageFilter === 'unused') {
      list = list.filter((row) => row.runCount === 0 && row.exportCount === 0)
    }
    if (q) {
      list = list.filter((row) =>
        `${row.reportName} ${row.reportNameArabic ?? ''} ${row.datasetName} ${row.status}`
          .toLowerCase()
          .includes(q),
      )
    }
    list.sort((a, b) => {
      if (usageSort === 'runs') return b.runCount - a.runCount || a.reportName.localeCompare(b.reportName)
      if (usageSort === 'exports') return b.exportCount - a.exportCount || a.reportName.localeCompare(b.reportName)
      if (usageSort === 'name') return a.reportName.localeCompare(b.reportName)
      if (usageSort === 'recent') {
        const aTime = a.lastRunAt ? new Date(a.lastRunAt).getTime() : 0
        const bTime = b.lastRunAt ? new Date(b.lastRunAt).getTime() : 0
        return bTime - aTime || b.runCount - a.runCount
      }
      return (
        b.runCount + b.exportCount - (a.runCount + a.exportCount)
        || a.reportName.localeCompare(b.reportName)
      )
    })
    return list
  }, [reports, usageFilter, usageQuery, usageSort])

  const filteredAnalytics = useMemo(() => {
    const q = analyticsQuery.trim().toLowerCase()
    let list = analytics.slice()
    if (analyticsFilter === 'active') {
      list = list.filter((row) => row.viewCount > 0 || row.queryCount > 0 || row.exportCount > 0)
    } else if (analyticsFilter === 'unused') {
      list = list.filter((row) => row.viewCount === 0 && row.queryCount === 0 && row.exportCount === 0)
    }
    if (q) {
      list = list.filter((row) =>
        `${row.dashboardName} ${row.dashboardNameArabic ?? ''} ${row.status}`
          .toLowerCase()
          .includes(q),
      )
    }
    list.sort((a, b) => {
      if (analyticsSort === 'views') return b.viewCount - a.viewCount || a.dashboardName.localeCompare(b.dashboardName)
      if (analyticsSort === 'queries') return b.queryCount - a.queryCount || a.dashboardName.localeCompare(b.dashboardName)
      if (analyticsSort === 'exports') return b.exportCount - a.exportCount || a.dashboardName.localeCompare(b.dashboardName)
      if (analyticsSort === 'name') return a.dashboardName.localeCompare(b.dashboardName)
      if (analyticsSort === 'recent') {
        const aTime = a.lastViewAt ? new Date(a.lastViewAt).getTime() : 0
        const bTime = b.lastViewAt ? new Date(b.lastViewAt).getTime() : 0
        return bTime - aTime || b.viewCount - a.viewCount
      }
      return (
        b.viewCount + b.queryCount + b.exportCount - (a.viewCount + a.queryCount + a.exportCount)
        || a.dashboardName.localeCompare(b.dashboardName)
      )
    })
    return list
  }, [analytics, analyticsFilter, analyticsQuery, analyticsSort])

  const maxAnalyticsActivity = useMemo(
    () => Math.max(1, ...analytics.map((row) => row.viewCount + row.queryCount + row.exportCount)),
    [analytics],
  )

  const filteredFreshness = useMemo(() => {
    const q = freshnessQuery.trim().toLowerCase()
    let list = freshness.slice()
    if (freshnessFilter === 'attention') {
      list = list.filter(needsAttention)
    } else if (freshnessFilter === 'cached') {
      list = list.filter((row) => row.isCached)
    }
    if (q) {
      list = list.filter((row) =>
        `${row.datasetName} ${row.datasetNameArabic ?? ''} ${row.objectKey} ${row.cacheStatus ?? ''}`
          .toLowerCase()
          .includes(q),
      )
    }
    return list.sort((a, b) => {
      const rank = (row: DatasetFreshnessInsight) => {
        if (row.cacheStatus === 'Failed') return 0
        if (isStale(row)) return 1
        if (row.cacheStatus === 'Refreshing') return 2
        if (row.isCached) return 3
        return 4
      }
      return rank(a) - rank(b) || a.datasetName.localeCompare(b.datasetName)
    })
  }, [freshness, freshnessFilter, freshnessQuery])

  return (
    <section className="page insights">
      <header className="page-header">
        <div>
          <div className="kicker">{t('insightsKicker')}</div>
          <h1>{t('insightsTitle')}</h1>
          <p className="lede">{t('insightsLede')}</p>
        </div>
        <label className="muted small insights-days">
          {t('insightsDays')}
          <select
            value={days}
            onChange={(event) => setDays(Number.parseInt(event.target.value, 10))}
          >
            <option value={7}>{t('insightsDays7')}</option>
            <option value={30}>{t('insightsDays30')}</option>
            <option value={90}>{t('insightsDays90')}</option>
          </select>
        </label>
      </header>

      {insights.isError && (
        <div className="banner error">{(insights.error as Error).message}</div>
      )}

      {insights.isLoading ? (
        <Spinner page label={t('loadingApp')} />
      ) : (
        <>
          {(attentionCount > 0 || unusedCount > 0 || unusedAnalyticsCount > 0) && (
            <div className="insights-alerts">
              {attentionCount > 0 && (
                <button
                  type="button"
                  className="banner warn insights-alert"
                  onClick={() => setFreshnessFilter('attention')}
                >
                  {t('insightsAlertFreshness', { n: attentionCount })}
                </button>
              )}
              {unusedCount > 0 && (
                <button
                  type="button"
                  className="banner insights-alert"
                  onClick={() => setUsageFilter('unused')}
                >
                  {t('insightsAlertUnused', { n: unusedCount, days })}
                </button>
              )}
              {unusedAnalyticsCount > 0 && (
                <button
                  type="button"
                  className="banner insights-alert"
                  onClick={() => setAnalyticsFilter('unused')}
                >
                  {t('insightsAlertUnusedAnalytics', { n: unusedAnalyticsCount, days })}
                </button>
              )}
            </div>
          )}

          <div className="insights-stat-groups">
            <section className="insights-stat-group">
              <h2>{t('insightsUsageTitle')}</h2>
              <div className="stats insights-stats">
                <div className="stat">
                  <span className="muted small">{t('insightsStatRuns')}</span>
                  <b>{summary?.reportRuns ?? 0}</b>
                </div>
                <div className="stat">
                  <span className="muted small">{t('insightsStatExports')}</span>
                  <b>{summary?.reportExports ?? 0}</b>
                </div>
                <div className="stat">
                  <span className="muted small">{t('insightsStatReports')}</span>
                  <b>{summary?.uniqueReportsRun ?? 0}</b>
                </div>
                <div className="stat">
                  <span className="muted small">{t('insightsStatUsers')}</span>
                  <b>{summary?.activeUsers ?? 0}</b>
                </div>
              </div>
            </section>
            <section className="insights-stat-group">
              <h2>{t('insightsFreshnessTitle')}</h2>
              <div className="stats insights-stats">
                <div className="stat">
                  <span className="muted small">{t('insightsStatCached')}</span>
                  <b>{summary?.cachedDatasets ?? 0}</b>
                </div>
                <div className="stat">
                  <span className="muted small">{t('insightsStatReady')}</span>
                  <b>{summary?.cacheReady ?? 0}</b>
                </div>
                <div className="stat">
                  <span className="muted small">{t('insightsStatFailed')}</span>
                  <b>{summary?.cacheFailed ?? 0}</b>
                </div>
                <div className="stat">
                  <span className="muted small">{t('insightsStatServingCache')}</span>
                  <b>{summary?.servingFromCache ?? 0}</b>
                </div>
              </div>
            </section>
            <section className="insights-stat-group">
              <h2>{t('insightsAnalyticsTitle')}</h2>
              <div className="stats insights-stats">
                <div className="stat">
                  <span className="muted small">{t('insightsStatAnalyticsViews')}</span>
                  <b>{summary?.analyticsViews ?? 0}</b>
                </div>
                <div className="stat">
                  <span className="muted small">{t('insightsStatAnalyticsQueries')}</span>
                  <b>{summary?.analyticsQueries ?? 0}</b>
                </div>
                <div className="stat">
                  <span className="muted small">{t('insightsStatAnalyticsExports')}</span>
                  <b>{summary?.analyticsExports ?? 0}</b>
                </div>
                <div className="stat">
                  <span className="muted small">{t('insightsStatDashboards')}</span>
                  <b>{summary?.uniqueDashboardsViewed ?? 0}</b>
                </div>
              </div>
            </section>
          </div>

          <div className="insights-panels">
            <section className="card insights-panel">
              <div className="insights-panel-head">
                <div>
                  <h2>{t('insightsUsageTitle')}</h2>
                  <p className="muted small">
                    {t('insightsUsageCount', {
                      shown: filteredReports.length,
                      total: reports.length,
                    })}
                  </p>
                </div>
                <div className="insights-toolbar">
                  <div className="view-toggle" role="group" aria-label={t('insightsUsageFilter')}>
                    {([
                      ['active', 'insightsFilterActive'],
                      ['unused', 'insightsFilterUnused'],
                      ['all', 'insightsFilterAll'],
                    ] as const).map(([value, label]) => (
                      <button
                        key={value}
                        type="button"
                        className={usageFilter === value ? 'active' : ''}
                        aria-pressed={usageFilter === value}
                        onClick={() => setUsageFilter(value)}
                      >
                        {t(label)}
                      </button>
                    ))}
                  </div>
                  <select
                    value={usageSort}
                    aria-label={t('insightsSort')}
                    onChange={(event) => setUsageSort(event.target.value as UsageSort)}
                  >
                    <option value="activity">{t('insightsSortActivity')}</option>
                    <option value="runs">{t('insightsSortRuns')}</option>
                    <option value="exports">{t('insightsSortExports')}</option>
                    <option value="recent">{t('insightsSortRecent')}</option>
                    <option value="name">{t('insightsSortName')}</option>
                  </select>
                  <input
                    className="search"
                    placeholder={t('insightsSearchReports')}
                    value={usageQuery}
                    onChange={(event) => setUsageQuery(event.target.value)}
                  />
                </div>
              </div>

              {filteredReports.length === 0 ? (
                <p className="muted">
                  {usageFilter === 'active'
                    ? t('insightsNoUsage')
                    : t('insightsNoMatches')}
                </p>
              ) : (
                <div className="table-scroll">
                  <table className="data-table compact">
                    <thead>
                      <tr>
                        <th>{t('insightsReport')}</th>
                        <th>{t('insightsDataset')}</th>
                        <th>{t('insightsActivity')}</th>
                        <th>{t('insightsRuns')}</th>
                        <th>{t('insightsExports')}</th>
                        <th>{t('insightsLastRun')}</th>
                        <th>{t('insightsLastExport')}</th>
                        <th>{t('insightsServing')}</th>
                        <th>{t('insightsCacheSync')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredReports.map((row) => (
                        <ReportUsageRow
                          key={row.reportId}
                          row={row}
                          maxActivity={maxActivity}
                          lang={lang}
                          t={t}
                        />
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            <section className="card insights-panel">
              <div className="insights-panel-head">
                <div>
                  <h2>{t('insightsAnalyticsTitle')}</h2>
                  <p className="muted small">
                    {t('insightsUsageCount', {
                      shown: filteredAnalytics.length,
                      total: analytics.length,
                    })}
                  </p>
                </div>
                <div className="insights-toolbar">
                  <div className="view-toggle" role="group" aria-label={t('insightsUsageFilter')}>
                    {([
                      ['active', 'insightsFilterActive'],
                      ['unused', 'insightsFilterUnused'],
                      ['all', 'insightsFilterAll'],
                    ] as const).map(([value, label]) => (
                      <button
                        key={value}
                        type="button"
                        className={analyticsFilter === value ? 'active' : ''}
                        aria-pressed={analyticsFilter === value}
                        onClick={() => setAnalyticsFilter(value)}
                      >
                        {t(label)}
                      </button>
                    ))}
                  </div>
                  <select
                    value={analyticsSort}
                    aria-label={t('insightsSort')}
                    onChange={(event) => setAnalyticsSort(event.target.value as AnalyticsSort)}
                  >
                    <option value="activity">{t('insightsSortActivity')}</option>
                    <option value="views">{t('insightsSortViews')}</option>
                    <option value="queries">{t('insightsSortQueries')}</option>
                    <option value="exports">{t('insightsSortExports')}</option>
                    <option value="recent">{t('insightsSortRecent')}</option>
                    <option value="name">{t('insightsSortName')}</option>
                  </select>
                  <input
                    className="search"
                    placeholder={t('insightsSearchDashboards')}
                    value={analyticsQuery}
                    onChange={(event) => setAnalyticsQuery(event.target.value)}
                  />
                </div>
              </div>

              {filteredAnalytics.length === 0 ? (
                <p className="muted">
                  {analyticsFilter === 'active'
                    ? t('insightsNoAnalytics')
                    : t('insightsNoMatches')}
                </p>
              ) : (
                <div className="table-scroll">
                  <table className="data-table compact">
                    <thead>
                      <tr>
                        <th>{t('insightsDashboard')}</th>
                        <th>{t('insightsActivity')}</th>
                        <th>{t('insightsViews')}</th>
                        <th>{t('insightsQueries')}</th>
                        <th>{t('insightsExports')}</th>
                        <th>{t('insightsLastView')}</th>
                        <th>{t('insightsLastExport')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredAnalytics.map((row) => (
                        <AnalyticsUsageRow
                          key={row.dashboardId}
                          row={row}
                          maxActivity={maxAnalyticsActivity}
                          lang={lang}
                          t={t}
                        />
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            <section className="card insights-panel">
              <div className="insights-panel-head">
                <div>
                  <h2>{t('insightsFreshnessTitle')}</h2>
                  <p className="muted small">
                    {t('insightsFreshnessCount', {
                      shown: filteredFreshness.length,
                      total: freshness.length,
                    })}
                  </p>
                </div>
                <div className="insights-toolbar">
                  <div className="view-toggle" role="group" aria-label={t('insightsFreshnessFilter')}>
                    {([
                      ['attention', 'insightsFilterAttention'],
                      ['cached', 'insightsFilterCached'],
                      ['all', 'insightsFilterAll'],
                    ] as const).map(([value, label]) => (
                      <button
                        key={value}
                        type="button"
                        className={freshnessFilter === value ? 'active' : ''}
                        aria-pressed={freshnessFilter === value}
                        onClick={() => setFreshnessFilter(value)}
                      >
                        {t(label)}
                      </button>
                    ))}
                  </div>
                  <input
                    className="search"
                    placeholder={t('insightsSearchDatasets')}
                    value={freshnessQuery}
                    onChange={(event) => setFreshnessQuery(event.target.value)}
                  />
                </div>
              </div>

              {filteredFreshness.length === 0 ? (
                <p className="muted">
                  {freshnessFilter === 'attention'
                    ? t('insightsNoAttention')
                    : t('insightsNoMatches')}
                </p>
              ) : (
                <div className="table-scroll">
                  <table className="data-table compact">
                    <thead>
                      <tr>
                        <th>{t('insightsDataset')}</th>
                        <th>{t('insightsObject')}</th>
                        <th>{t('insightsServing')}</th>
                        <th>{t('insightsCacheSync')}</th>
                        <th>{t('insightsNextRefresh')}</th>
                        <th>{t('insightsRows')}</th>
                        <th>{t('insightsServer')}</th>
                        <th>{t('insightsReportsUsing')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredFreshness.map((row) => (
                        <FreshnessRow key={row.datasetId} row={row} lang={lang} t={t} />
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          </div>
        </>
      )}
    </section>
  )
}

function ReportUsageRow({
  row,
  maxActivity,
  lang,
  t,
}: {
  row: ReportUsageInsight
  maxActivity: number
  lang: Lang
  t: (key: MessageKey, vars?: Record<string, string | number>) => string
}) {
  const lastRun = formatWhen(row.lastRunAt, t('insightsNever'), lang)
  const lastExport = formatWhen(row.lastExportAt, t('insightsNever'), lang)
  const cacheSync = formatWhen(row.cacheLastSuccessAt, t('insightsNever'), lang)
  const activity = row.runCount + row.exportCount

  return (
    <tr className={activity === 0 ? 'insights-row-muted' : undefined}>
      <td>
        <Link className="link" to={`/reports/${row.reportId}/view`}>
          {localizedText(lang, row.reportName, row.reportNameArabic)}
        </Link>
        <div className="insights-inline-meta">
          <StatusBadge status={row.status} t={t} />
        </div>
      </td>
      <td>{row.datasetName}</td>
      <td className="insights-activity-cell">
        <UsageBar value={activity} max={maxActivity} />
        <span className="muted small">{activity}</span>
      </td>
      <td>{row.runCount}</td>
      <td>{row.exportCount}</td>
      <td title={lastRun.title}>
        {lastRun.primary}
        {row.lastRunBy ? <div className="muted small">{row.lastRunBy}</div> : null}
      </td>
      <td title={lastExport.title}>
        {lastExport.primary}
        {row.lastExportBy ? (
          <div className="muted small">{row.lastExportBy}</div>
        ) : null}
      </td>
      <td>
        <ServingBadge servingFrom={row.servingFrom} t={t} />
      </td>
      <td title={cacheSync.title}>
        {row.cacheStatus ? (
          <>
            <div>{row.cacheStatus}</div>
            <div className="muted small">{cacheSync.primary}</div>
          </>
        ) : (
          t('insightsCacheOff')
        )}
      </td>
    </tr>
  )
}

function AnalyticsUsageRow({
  row,
  maxActivity,
  lang,
  t,
}: {
  row: AnalyticsUsageInsight
  maxActivity: number
  lang: Lang
  t: (key: MessageKey, vars?: Record<string, string | number>) => string
}) {
  const lastView = formatWhen(row.lastViewAt, t('insightsNever'), lang)
  const lastExport = formatWhen(row.lastExportAt, t('insightsNever'), lang)
  const activity = row.viewCount + row.queryCount + row.exportCount

  return (
    <tr className={activity === 0 ? 'insights-row-muted' : undefined}>
      <td>
        <Link className="link" to={`/analytics/${row.dashboardId}/view`}>
          {localizedText(lang, row.dashboardName, row.dashboardNameArabic)}
        </Link>
        <div className="insights-inline-meta">
          <StatusBadge status={row.status} t={t} />
        </div>
      </td>
      <td className="insights-activity-cell">
        <UsageBar value={activity} max={maxActivity} />
        <span className="muted small">{activity}</span>
      </td>
      <td>{row.viewCount}</td>
      <td>{row.queryCount}</td>
      <td>{row.exportCount}</td>
      <td title={lastView.title}>
        {lastView.primary}
        {row.lastViewBy ? <div className="muted small">{row.lastViewBy}</div> : null}
      </td>
      <td title={lastExport.title}>
        {lastExport.primary}
        {row.lastExportBy ? <div className="muted small">{row.lastExportBy}</div> : null}
      </td>
    </tr>
  )
}

function FreshnessRow({
  row,
  lang,
  t,
}: {
  row: DatasetFreshnessInsight
  lang: Lang
  t: (key: MessageKey, vars?: Record<string, string | number>) => string
}) {
  const lastSync = formatWhen(row.lastSuccessAt, t('insightsNever'), lang)
  const nextRefresh = formatWhen(
    row.nextRefreshAt,
    t('datasetCacheNotScheduled'),
    lang,
  )

  return (
    <tr className={needsAttention(row) ? 'insights-row-attention' : undefined}>
      <td>
        <Link className="link" to="/catalog">
          {localizedText(lang, row.datasetName, row.datasetNameArabic)}
        </Link>
        <div className="insights-inline-meta">
          {!row.isEnabled ? <span className="badge draft">{t('off')}</span> : null}
        </div>
        {row.lastError ? (
          <div className="dataset-cache-error small" title={row.lastError}>
            {row.lastError}
          </div>
        ) : null}
      </td>
      <td className="object-name">{row.objectKey}</td>
      <td>
        <CacheStatusBadge row={row} t={t} />
      </td>
      <td title={lastSync.title}>{lastSync.primary}</td>
      <td title={nextRefresh.title}>{nextRefresh.primary}</td>
      <td>{row.rowCount != null ? row.rowCount.toLocaleString() : '—'}</td>
      <td>{row.refreshedByServer || '—'}</td>
      <td>{row.reportCount}</td>
    </tr>
  )
}
