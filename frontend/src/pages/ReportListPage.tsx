import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import type { ExportFormat, ReportCategory, ReportStatus, ReportSummary } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'
import { bilingualExportName } from '../lib/download'
import { AiAssistPanel } from '../components/AiAssistPanel'
import { DeleteReportDialog } from '../components/DeleteReportDialog'
import { ScheduleReportDialog } from '../components/ScheduleReportDialog'
import { ReportCategoryManager } from '../components/ReportCategoryManager'
import { StudioCard, type StudioView } from '../components/StudioCard'
import { IconChart, IconFilter, IconGrid, IconList, IconPlus } from '../components/Icons'
import { Spinner } from '../components/Spinner'

const VIEW_KEY = 'rb.reportsView'
const LEGACY_VIEW_KEY = 'rb.adminReportsView'

function readReportsView(): StudioView {
  if (typeof localStorage === 'undefined') {
    return 'card'
  }
  const stored = localStorage.getItem(VIEW_KEY) ?? localStorage.getItem(LEGACY_VIEW_KEY)
  return stored === 'list' ? 'list' : 'card'
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

export function ReportListPage() {
  const { can } = useAuth()
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const reports = useQuery({ queryKey: ['reports'], queryFn: api.listReports })
  const categories = useQuery({
    queryKey: ['report-categories'],
    queryFn: api.listReportCategories,
  })
  const datasets = useQuery({
    queryKey: ['datasets', 'enabled'],
    queryFn: () => api.listDatasets(true),
    enabled: can('datasets.view'),
  })
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState<'all' | ReportStatus>('all')
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [downloadingId, setDownloadingId] = useState<string | null>(null)
  const [duplicatingId, setDuplicatingId] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ReportSummary | null>(null)
  const [scheduleTarget, setScheduleTarget] = useState<ReportSummary | null>(null)
  const [reportsView, setReportsView] = useState<StudioView>(readReportsView)
  const [managingCategories, setManagingCategories] = useState(false)

  useEffect(() => {
    localStorage.setItem(VIEW_KEY, reportsView)
  }, [reportsView])
  const remove = useMutation({
    mutationFn: api.deleteReport,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['reports'] })
      setDeleteTarget(null)
    },
  })
  const duplicate = useMutation({
    mutationFn: (id: string) => api.duplicateReport(id),
    onSuccess: (report) => {
      void queryClient.invalidateQueries({ queryKey: ['reports'] })
      navigate(`/reports/${report.id}`)
    },
    onError: (error) => setDownloadError((error as Error).message),
    onSettled: () => setDuplicatingId(null),
  })
  const setStatus = useMutation({
    mutationFn: ({
      id,
      status,
    }: {
      id: string
      status: ReportStatus
    }) =>
      status === 'published' ? api.publishReport(id) : api.unpublishReport(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['reports'] }),
  })

  const publishedCount =
    reports.data?.filter((report) => report.status === 'published').length ?? 0
  const draftCount =
    reports.data?.filter((report) => report.status === 'draft').length ?? 0

  const filtered = useMemo(() => {
    const list = reports.data ?? []
    const q = query.trim().toLowerCase()
    return list.filter((report) => {
      if (statusFilter !== 'all' && report.status !== statusFilter) {
        return false
      }
      if (!q) {
        return true
      }
      return (
        report.name.toLowerCase().includes(q) ||
        (report.nameArabic ?? '').toLowerCase().includes(q) ||
        report.datasetName.toLowerCase().includes(q) ||
        (report.datasetNameArabic ?? '').toLowerCase().includes(q) ||
        (report.createdByName ?? '').toLowerCase().includes(q) ||
        (report.categoryName ?? '').toLowerCase().includes(q) ||
        (report.categoryNameArabic ?? '').toLowerCase().includes(q)
      )
    })
  }, [reports.data, query, statusFilter])

  const grouped = useMemo(() => {
    const catalog = categories.data ?? []
    const known = new Map(catalog.map((category) => [category.id, category]))
    const buckets = new Map<string, ReportSummary[]>()
    for (const report of filtered) {
      const key = report.categoryId && known.has(report.categoryId) ? report.categoryId : ''
      const list = buckets.get(key) ?? []
      list.push(report)
      buckets.set(key, list)
    }

    const sections: {
      id: string | null
      category: ReportCategory | null
      reports: ReportSummary[]
    }[] = []
    for (const category of catalog) {
      const items = buckets.get(category.id)
      if (items?.length) {
        sections.push({ id: category.id, category, reports: items })
      }
    }
    const uncategorized = buckets.get('')
    if (uncategorized?.length) {
      sections.push({ id: null, category: null, reports: uncategorized })
    }
    return sections
  }, [filtered, categories.data])

  const renderReports = (items: ReportSummary[]) => (
    <div className={reportsView === 'list' ? 'studio-list' : 'studio-grid'}>
      {items.map((report) => (
        <StudioCard
          key={report.id}
          variant={reportsView}
          report={report}
          downloading={downloadingId === report.id}
          canExport={can('reports.export')}
          onExport={async (format: ExportFormat, file?: FileSystemFileHandle) => {
            setDownloadError(null)
            setDownloadingId(report.id)
            try {
              await api.exportReport(
                report.id,
                format,
                lang,
                file,
                bilingualExportName(report.name, report.nameArabic),
              )
            } catch (error) {
              setDownloadError((error as Error).message)
            } finally {
              setDownloadingId(null)
            }
          }}
          onDelete={() => setDeleteTarget(report)}
          duplicating={duplicatingId === report.id}
          onDuplicate={
            can('reports.create')
              ? () => {
                  setDuplicatingId(report.id)
                  duplicate.mutate(report.id)
                }
              : undefined
          }
          onPublish={() =>
            setStatus.mutate({ id: report.id, status: 'published' })
          }
          onUnpublish={() =>
            setStatus.mutate({ id: report.id, status: 'draft' })
          }
          onSchedule={
            report.canSchedule && report.kind !== 'chart'
              ? () => setScheduleTarget(report)
              : undefined
          }
        />
      ))}
    </div>
  )

  return (
    <section className="page studio">
      <header className="page-header">
        <div>
          <div className="kicker">{t('libraryKicker')}</div>
          <h1>{t('libraryTitle')}</h1>
          <p className="lede">{t('libraryLede')}</p>
        </div>
        {(can('reports.create') || can('settings.update')) && (
          <div className="header-actions">
            {can('settings.update') && (
              <button
                type="button"
                className="btn"
                aria-pressed={managingCategories}
                onClick={() => setManagingCategories((open) => !open)}
              >
                {t('manageCategories')}
              </button>
            )}
            {can('reports.create') && (
              <>
                <Link className="btn" to="/reports/new?kind=chart">
                  <IconChart />
                  {t('newChart')}
                </Link>
                <Link className="btn primary" to="/reports/new">
                  <IconPlus />
                  {t('newReport')}
                </Link>
              </>
            )}
          </div>
        )}
      </header>

      {can('ai.use') && (
        <AiAssistPanel
          onApply={(suggestion) =>
            navigate(suggestion.layout.kind === 'chart' ? '/reports/new?kind=chart' : '/reports/new', {
              state: { aiDraft: suggestion },
            })
          }
        />
      )}

      {managingCategories && can('settings.update') && <ReportCategoryManager />}

      <div className="stats">
        <div className="stat">
          <span className="muted small">{t('published')}</span>
          <b>{publishedCount}</b>
        </div>
        <div className="stat">
          <span className="muted small">{t('drafts')}</span>
          <b>{draftCount}</b>
        </div>
        <div className="stat">
          <span className="muted small">{t('enabledSources')}</span>
          <b>{datasets.data?.length ?? 0}</b>
        </div>
      </div>

      {reports.isError && (
        <div className="banner error">
          {(reports.error as Error).message}. {t('apiHint')}
        </div>
      )}
      {setStatus.isError && (
        <div className="banner error">{(setStatus.error as Error).message}</div>
      )}
      {remove.isError && !deleteTarget && (
        <div className="banner error">{(remove.error as Error).message}</div>
      )}
      {downloadError && <div className="banner error">{downloadError}</div>}

      <ScheduleReportDialog report={scheduleTarget} onClose={() => setScheduleTarget(null)} />

      <DeleteReportDialog
        report={deleteTarget}
        pending={remove.isPending}
        error={remove.isError ? (remove.error as Error).message : null}
        onClose={() => {
          if (!remove.isPending) {
            remove.reset()
            setDeleteTarget(null)
          }
        }}
        onConfirm={() => {
          if (deleteTarget) {
            remove.mutate(deleteTarget.id)
          }
        }}
      />

      <div className="studio-head">
        <div>
          <h2>{t('yourStudio')}</h2>
          <p className="muted small">
            {t('studioCounts', {
              published: publishedCount,
              drafts: draftCount,
              draftLabel: draftCount === 1 ? t('draftSingular') : t('draftPlural'),
            })}
          </p>
        </div>
        <div className="studio-head-tools">
          <div className="status-tabs" role="tablist" aria-label={t('reportStatus')}>
            {(
              [
                ['all', 'tabAll'],
                ['published', 'tabPublished'],
                ['draft', 'tabDrafts'],
              ] as const
            ).map(([id, label]) => (
              <button
                key={id}
                type="button"
                role="tab"
                aria-selected={statusFilter === id}
                className={statusFilter === id ? 'active' : ''}
                onClick={() => setStatusFilter(id)}
              >
                {t(label)}
              </button>
            ))}
          </div>
          <input
            className="search"
            placeholder={t('searchReports')}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
          <ViewToggle
            view={reportsView}
            onChange={setReportsView}
            label={t('reportsView')}
            cardsLabel={t('viewCards')}
            listLabel={t('viewList')}
          />
        </div>
      </div>

      {reports.isLoading ? (
        <Spinner page label={t('loadingApp')} />
      ) : grouped.length ? (
        grouped.map((section) => (
          <section key={section.id ?? 'uncategorized'} className="studio-section">
            <div className="studio-section-head">
              <div>
                <h3>
                  {section.category
                    ? localizedText(lang, section.category.name, section.category.nameArabic)
                    : t('uncategorizedReports')}
                </h3>
                <p className="muted small">
                  {section.category
                    ? t('reportsInCategory', { count: section.reports.length })
                    : t('uncategorizedHint')}
                </p>
              </div>
            </div>
            {renderReports(section.reports)}
          </section>
        ))
      ) : reports.data?.length ? (
        <div className="empty">
          <IconFilter />
          <div className="kicker">{t('noMatchKicker')}</div>
          <h2>{t('noMatchTitle')}</h2>
          <p>{t('noMatchHint')}</p>
        </div>
      ) : (
        <div className="empty">
          <IconPlus />
          <div className="kicker">{t('blankKicker')}</div>
          <h2>{t('blankTitle')}</h2>
          <p>{t('blankHint')}</p>
          <p>
            {can('reports.create') && (
              <>
                <Link className="btn" to="/reports/new?kind=chart">
                  <IconChart />
                  {t('newChart')}
                </Link>
                <Link className="btn primary" to="/reports/new">
                  <IconPlus />
                  {t('startDesigning')}
                </Link>
              </>
            )}
          </p>
        </div>
      )}
    </section>
  )
}
