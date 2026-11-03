import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, isSessionExpired } from '../api/client'
import type { AnalyticsDashboardSummary, ReportStatus } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { IconGrid, IconList, IconPlus, IconSpark } from '../components/Icons'
import { Spinner } from '../components/Spinner'
import type { StudioView } from '../components/StudioCard'
import { useLocale } from '../i18n/LocaleProvider'
import type { MessageKey } from '../i18n/messages'
import { localizedText } from '../i18n/localized'
import { formatStamp, relativeTime } from '../lib/format'

const VIEW_KEY = 'rb.analyticsView'

function readAnalyticsView(): StudioView {
  if (typeof localStorage === 'undefined') {
    return 'card'
  }
  return localStorage.getItem(VIEW_KEY) === 'list' ? 'list' : 'card'
}

function ViewToggle({
  view,
  onChange,
  label,
  cardsLabel,
  listLabel,
}: {
  view: StudioView
  onChange: (view: StudioView) => void
  label: string
  cardsLabel: string
  listLabel: string
}) {
  return (
    <div className="view-toggle" role="group" aria-label={label}>
      <button
        type="button"
        className={view === 'card' ? 'active' : ''}
        aria-pressed={view === 'card'}
        title={cardsLabel}
        onClick={() => onChange('card')}
      >
        <IconGrid />
        {cardsLabel}
      </button>
      <button
        type="button"
        className={view === 'list' ? 'active' : ''}
        aria-pressed={view === 'list'}
        title={listLabel}
        onClick={() => onChange('list')}
      >
        <IconList />
        {listLabel}
      </button>
    </div>
  )
}

export function AnalyticsListPage() {
  const { can } = useAuth()
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const dashboards = useQuery({
    queryKey: ['analytics'],
    queryFn: api.listAnalyticsDashboards,
  })
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState<'all' | ReportStatus>('all')
  const [layout, setLayout] = useState<StudioView>(readAnalyticsView)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    localStorage.setItem(VIEW_KEY, layout)
  }, [layout])

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteAnalyticsDashboard(id),
    onSuccess: async () => {
      setError(null)
      await queryClient.invalidateQueries({ queryKey: ['analytics'] })
    },
    onError: (err) => setError((err as Error).message),
  })

  const visible = useMemo(() => {
    const list = dashboards.data ?? []
    const q = query.trim().toLowerCase()
    return list.filter((item) => {
      if (statusFilter !== 'all' && item.status !== statusFilter) {
        return false
      }
      if (!q) {
        return true
      }
      return [item.name, item.nameArabic, item.description]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(q))
    })
  }, [dashboards.data, query, statusFilter])

  if (isSessionExpired(dashboards.error)) {
    return <Spinner fullscreen label={t('loadingApp')} />
  }

  return (
    <section className="page analytics-list analytics-list-stage">
      <header className="page-header analytics-list-hero">
        <div>
          <div className="kicker">{t('analyticsKicker')}</div>
          <h1>{t('analyticsTitle')}</h1>
          <p className="lede">{t('analyticsLede')}</p>
        </div>
        <div className="page-actions">
          {can('analytics.create') && (
            <Link className="btn primary" to="/analytics/new">
              <IconPlus />
              {t('analyticsNew')}
            </Link>
          )}
        </div>
      </header>

      <div className="analytics-list-toolbar">
        <input
          className="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder={t('analyticsSearch')}
        />
        <select
          value={statusFilter}
          onChange={(event) =>
            setStatusFilter(event.target.value as 'all' | ReportStatus)
          }
        >
          <option value="all">{t('allStatuses')}</option>
          <option value="draft">{t('draft')}</option>
          <option value="published">{t('published')}</option>
        </select>
        <ViewToggle
          view={layout}
          onChange={setLayout}
          label={t('analyticsView')}
          cardsLabel={t('viewCards')}
          listLabel={t('viewList')}
        />
      </div>

      {(dashboards.isError || error) && (
        <div className="banner error">
          {error || dashboards.error?.message}
        </div>
      )}

      {dashboards.isLoading ? (
        <Spinner label={t('loading')} />
      ) : visible.length === 0 ? (
        <div className="empty">
          <IconSpark />
          <h2>{t('analyticsEmptyTitle')}</h2>
          <p>{t('analyticsEmptyLede')}</p>
          {can('analytics.create') && (
            <p>
              <Link className="btn primary" to="/analytics/new">
                {t('analyticsNew')}
              </Link>
            </p>
          )}
        </div>
      ) : (
        <div className={layout === 'list' ? 'studio-list' : 'catalog-grid'}>
          {visible.map((item) => (
            <DashboardCard
              key={item.id}
              item={item}
              title={localizedText(lang, item.name, item.nameArabic)}
              variant={layout}
              canEdit={item.canEdit}
              canDelete={item.canDelete}
              deleting={remove.isPending}
              updatedLabel={t('updated', { time: relativeTime(item.updatedAt, lang) })}
              updatedStamp={formatStamp(item.updatedAt, lang)}
              onDelete={() => {
                if (!confirm(t('analyticsDeleteConfirm', { name: item.name }))) {
                  return
                }
                remove.mutate(item.id)
              }}
              t={t}
            />
          ))}
        </div>
      )}
    </section>
  )
}

function DashboardCard({
  item,
  title,
  variant,
  canEdit,
  canDelete,
  deleting,
  updatedLabel,
  updatedStamp,
  onDelete,
  t,
}: {
  item: AnalyticsDashboardSummary
  title: string
  variant: StudioView
  canEdit: boolean
  canDelete: boolean
  deleting: boolean
  updatedLabel: string
  updatedStamp: string
  onDelete: () => void
  t: (key: MessageKey, vars?: Record<string, string | number>) => string
}) {
  const counts = t('analyticsMetaCounts', {
    sources: item.sourceCount,
    dimensions: item.dimensionCount,
    measures: item.measureCount,
  })
  const flags = (
    <span className="studio-card-flags">
      <span className={`badge ${item.status === 'published' ? 'published' : 'draft'}`}>
        {item.status === 'published' ? t('published') : t('draft')}
      </span>
      {item.accessMode === 'Roles' ? (
        <span className="badge">{t('selectedRole')}</span>
      ) : item.isRestricted ? (
        <span className="badge">{t('selectedPeople')}</span>
      ) : null}
    </span>
  )
  const actions = (
    <div className="card-actions studio-card-actions">
      <Link className="btn sm primary" to={`/analytics/${item.id}/view`}>
        {t('analyticsOpen')}
      </Link>
      {canEdit && (
        <Link className="btn sm" to={`/analytics/${item.id}`}>
          {t('edit')}
        </Link>
      )}
      {canDelete && (
        <button type="button" className="btn sm danger" disabled={deleting} onClick={onDelete}>
          {t('delete')}
        </button>
      )}
    </div>
  )

  if (variant === 'list') {
    return (
      <article className={`studio-list-row ${item.status === 'draft' ? 'draft' : 'published'}`}>
        <div className="studio-list-copy">
          <h2>
            <Link to={`/analytics/${item.id}/view`}>{title}</Link>
          </h2>
          <p>{item.description || counts}</p>
        </div>
        <div className="studio-list-meta">
          {flags}
          <span className="muted small" title={updatedStamp}>
            {counts} · {updatedLabel}
          </span>
        </div>
        {actions}
      </article>
    )
  }

  return (
    <article className={`catalog-card ${item.status === 'published' ? 'enabled' : ''}`}>
      <div className="catalog-card-head">
        <div>
          <Link className="card-title-link" to={`/analytics/${item.id}/view`}>
            {title}
          </Link>
          <div className="object-name">{counts}</div>
        </div>
        {flags}
      </div>
      {item.description && <p className="muted">{item.description}</p>}
      <div className="card-meta muted" title={updatedStamp}>
        {updatedLabel}
      </div>
      {actions}
    </article>
  )
}
