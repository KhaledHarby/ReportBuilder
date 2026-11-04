import { useMutation, useQuery } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api, isSessionExpired } from '../api/client'
import type {
  AnalyticsChartResult,
  AnalyticsChartType,
  AnalyticsDashboardDetail,
  AnalyticsDimensionResult,
  AnalyticsDimensionValue,
  AnalyticsMeasureResult,
  AnalyticsDataResult,
  AnalyticsSnapshotResult,
  AnalyticsSelectionState,
} from '../api/types'
import { AnalyticsDrilldown } from '../components/AnalyticsDrilldown'
import { AnalyticsExportMenu, type AnalyticsExportKind } from '../components/AnalyticsExportMenu'
import { AnalyticsMapView } from '../components/AnalyticsMapView'
import { AnalyticsMiniChart } from '../components/AnalyticsMiniChart'
import { AnalyticsSmartSearch } from '../components/AnalyticsSmartSearch'
import { IconBookmark, IconDrill, IconEdit, IconFilter, IconLock, IconUnlock } from '../components/Icons'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'
import { usePageTitle } from '../lib/pageTitle'
import { cellLinkProps } from '../lib/reportLinks'
import { useOnceInView } from '../lib/useOnceInView'
import {
  createBookmark,
  deleteBookmark,
  loadBookmarks,
  type AnalyticsBookmark,
} from '../lib/analyticsBookmarks'
import {
  ANALYTICS_NULL,
  applyRowToSelections,
  buildShareUrl,
  clearDimension,
  clearSelections,
  decodeViewState,
  encodeViewState,
  interpolateUrl,
  maxDimensionCount,
  moveDimensionState,
  normalizeViewState,
  resolveDimState,
  selectValues,
  selectionEntries,
  toggleLocked,
  toggleSelection,
  type AnalyticsViewState,
} from '../lib/analyticsFilters'
import {
  DRILL_CHART_ID,
  drillAtLeaf,
  drillInto,
  drillToLevel,
  nextBreakdownDimension,
  reconcileDrill,
  resolveDrillSeries,
} from '../lib/analyticsDrill'
import { exportAnalyticsPdf, exportAnalyticsPptx } from '../lib/analyticsExport'

const PAGE_SIZE = 50

// Selection states are re-visited constantly while filtering; treating a recent result as fresh
// avoids refetching all three panes when the page remounts.
const QUERY_STALE_MS = 30_000

export function AnalyticsDashboardPage() {
  const { id } = useParams()
  const { lang, t } = useLocale()
  const [searchParams, setSearchParams] = useSearchParams()
  const [view, setView] = useState<AnalyticsViewState>(() =>
    reconcileDrill(decodeViewState(searchParams.get('sel'))),
  )
  const [drillChrome, setDrillChrome] = useState<{
    dashboardId: string | undefined
    breakdownOverride: string | null
    measureOverride: string | null
    chartType: AnalyticsChartType
  }>(() => ({
    dashboardId: id,
    breakdownOverride: null,
    measureOverride: null,
    chartType: 'bar',
  }))
  const chrome =
    drillChrome.dashboardId === id
      ? drillChrome
      : {
          dashboardId: id,
          breakdownOverride: null,
          measureOverride: null,
          chartType: 'bar' as const,
        }
  const breakdownOverride = chrome.breakdownOverride
  const measureOverride = chrome.measureOverride
  const drillChartType = chrome.chartType
  const [skip, setSkip] = useState(0)
  const [menu, setMenu] = useState<string | null>(null)
  const [copyFlash, setCopyFlash] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)
  const [screenExporting, setScreenExporting] = useState(false)
  const [bookmarkName, setBookmarkName] = useState('')
  const [bookmarks, setBookmarks] = useState<AnalyticsBookmark[]>(() =>
    id ? loadBookmarks(id) : [],
  )

  const dashboard = useQuery({
    queryKey: ['analytics', id],
    queryFn: () => api.getAnalyticsDashboard(id!),
    enabled: Boolean(id),
  })
  usePageTitle(
    dashboard.data ? localizedText(lang, dashboard.data.name, dashboard.data.nameArabic) : null,
  )

  const hasCompare =
    Object.keys(view.compare).length > 0 ||
    Object.values(view.dimStates).some((item) => item === 'compare') ||
    (dashboard.data?.config.charts ?? []).some((chart) => chart.state === 'compare')

  const dimensionIds = useMemo(
    () => dashboard.data?.config.dimensions.map((dimension) => dimension.id) ?? [],
    [dashboard.data],
  )
  const measureIds = useMemo(
    () => dashboard.data?.config.measures.map((measure) => measure.id) ?? [],
    [dashboard.data],
  )
  const suggestedBreakdown = nextBreakdownDimension(dimensionIds, view.drill ?? [])
  const breakdownId =
    breakdownOverride && dimensionIds.includes(breakdownOverride)
      ? breakdownOverride
      : suggestedBreakdown
  const measureId =
    measureOverride && measureIds.includes(measureOverride)
      ? measureOverride
      : (measureIds[0] ?? null)
  const drillCovered = (dashboard.data?.config.charts ?? []).some(
    (chart) =>
      chart.dimensionId === breakdownId &&
      chart.measureId === measureId &&
      chart.state !== 'compare',
  )
  const serverDrill = Boolean(breakdownId && measureId && !drillCovered)

  const query = useQuery({
    queryKey: [
      'analytics-query',
      id,
      view.default,
      serverDrill ? breakdownId : '',
      serverDrill ? measureId : '',
    ],
    queryFn: ({ signal }) =>
      api.queryAnalyticsDashboard(
        id!,
        {
          selections: view.default,
          ...(serverDrill
            ? { drillDimensionId: breakdownId ?? undefined, drillMeasureId: measureId ?? undefined }
            : {}),
        },
        signal,
      ),
    enabled: Boolean(id) && Boolean(dashboard.data),
    placeholderData: (previous) => previous,
    staleTime: QUERY_STALE_MS,
  })

  // The grid and the map sit below the filters and KPIs. Their requests wait until the pane has
  // scrolled near the viewport, so a click on a filter costs one snapshot request, not three.
  const gridPane = useOnceInView<HTMLDivElement>()
  const mapPane = useOnceInView<HTMLDivElement>()

  const dataQuery = useQuery({
    queryKey: ['analytics-data', id, view.default, skip],
    queryFn: ({ signal }) =>
      api.queryAnalyticsData(
        id!,
        {
          selections: view.default,
          skip,
          take: PAGE_SIZE,
        },
        signal,
      ),
    enabled: Boolean(id) && Boolean(dashboard.data) && gridPane.inView,
    placeholderData: (previous) => previous,
    staleTime: QUERY_STALE_MS,
  })

  const mapEnabled = Boolean(dashboard.data?.config.map?.enabled)
  const mapQuery = useQuery({
    queryKey: ['analytics-map', id, view.default],
    queryFn: ({ signal }) =>
      api.queryAnalyticsMap(
        id!,
        {
          selections: view.default,
        },
        signal,
      ),
    enabled: Boolean(id) && Boolean(dashboard.data) && mapEnabled && mapPane.inView,
    placeholderData: (previous) => previous,
    staleTime: QUERY_STALE_MS,
  })

  const compareQuery = useQuery({
    queryKey: ['analytics-query-compare', id, view.compare],
    queryFn: ({ signal }) =>
      api.queryAnalyticsDashboard(
        id!,
        {
          selections: view.compare,
          track: false,
        },
        signal,
      ),
    enabled: Boolean(id) && Boolean(dashboard.data) && hasCompare,
    placeholderData: (previous) => previous,
    staleTime: QUERY_STALE_MS,
  })

  useEffect(() => {
    if (id) {
      setBookmarks(loadBookmarks(id))
    }
  }, [id])

  useEffect(() => {
    if (menu !== 'bookmarks') {
      return
    }
    const onPointer = (event: PointerEvent) => {
      const node = event.target as HTMLElement | null
      if (!node?.closest('.ax-menu')) {
        setMenu(null)
      }
    }
    document.addEventListener('pointerdown', onPointer)
    return () => document.removeEventListener('pointerdown', onPointer)
  }, [menu])

  useEffect(() => {
    const encoded = encodeViewState(view)
    const current = searchParams.get('sel') ?? ''
    if (encoded === current) {
      return
    }
    const next = new URLSearchParams(searchParams)
    if (encoded) {
      next.set('sel', encoded)
    } else {
      next.delete('sel')
    }
    setSearchParams(next, { replace: true })
  }, [view, searchParams, setSearchParams])

  const labels = useMemo(() => {
    const map: Record<string, string> = {}
    for (const dimension of dashboard.data?.config.dimensions ?? []) {
      map[dimension.id] = dimension.label || dimension.field
    }
    return map
  }, [dashboard.data])

  const crumbs = [
    ...selectionEntries(view.default, labels, 'default'),
    ...selectionEntries(view.compare, labels, 'compare'),
  ]
  const clearAllFilters = () =>
    patchView((current) => ({
      ...current,
      default: clearSelections(current.locked, current.default),
      compare: clearSelections(current.locked, current.compare),
    }))

  const exportCsv = useMutation({
    mutationFn: () =>
      api.exportAnalyticsDashboard(
        id!,
        {
          selections: view.default,
          skip: 0,
          take: 200,
        },
        dashboard.data
          ? localizedText(lang, dashboard.data.name, dashboard.data.nameArabic)
          : undefined,
      ),
    onSuccess: () => setExportError(null),
    onError: (error) => setExportError((error as Error).message),
  })

  async function copyLink() {
    const path = buildShareUrl(
      `${window.location.pathname}${window.location.hash}`,
      view,
    )
    const full = `${window.location.origin}${path}`
    try {
      await navigator.clipboard.writeText(full)
      setCopyFlash(true)
      window.setTimeout(() => setCopyFlash(false), 1600)
    } catch {
      setExportError(t('analyticsCopyFailed'))
    }
  }

  function patchChrome(patch: {
    breakdownOverride?: string | null
    measureOverride?: string | null
    chartType?: AnalyticsChartType
  }) {
    setDrillChrome({ ...chrome, ...patch, dashboardId: id })
  }

  function patchView(updater: (current: AnalyticsViewState) => AnalyticsViewState) {
    setSkip(0)
    setView((current) => reconcileDrill(updater(current)))
  }

  function focusDrill(dimensionId: string, value: string) {
    patchChrome({ breakdownOverride: null })
    patchView((current) => drillInto(current, dimensionId, value))
  }

  function toggleValue(dimensionId: string, value: string) {
    if (view.locked.includes(dimensionId)) {
      return
    }
    const state = resolveDimState(view.dimStates, dimensionId)
    patchView((current) => ({
      ...current,
      [state]: toggleSelection(current[state], dimensionId, value),
    }))
  }

  const searchValues = useCallback(
    (text: string, signal: AbortSignal) =>
      api
        .searchAnalyticsValues(
          id!,
          {
            selections: view.default,
            query: text,
          },
          signal,
        )
        .then((result) => result.hits),
    [id, view.default],
  )

  if (
    isSessionExpired(dashboard.error) ||
    isSessionExpired(query.error) ||
    isSessionExpired(dataQuery.error) ||
    isSessionExpired(mapQuery.error) ||
    isSessionExpired(compareQuery.error)
  ) {
    return <Spinner fullscreen label={t('loadingApp')} />
  }

  if (dashboard.isLoading) {
    return <Spinner fullscreen label={t('loading')} />
  }

  if (dashboard.isError || !dashboard.data) {
    return (
      <section className="page">
        <div className="banner error">
          {dashboard.error?.message || t('analyticsNotFound')}
        </div>
        <Link to="/analytics">{t('back')}</Link>
      </section>
    )
  }

  const item = dashboard.data
  const title = localizedText(lang, item.name, item.nameArabic)
  const description = localizedText(lang, item.description, item.descriptionArabic)
  const result = query.data
  const grid = dataQuery.data
  const compareResult = compareQuery.data
  const isDraft = item.status === 'draft'
  const charts = mergeCharts(item, result, compareResult)
  const mapPoints = mapQuery.data?.mapPoints ?? []
  const firstLoad = query.isLoading && !result
  // Also true while the pane is still waiting to scroll into view, so it shows a placeholder
  // rather than an empty "0 rows" state before its request has started.
  const gridFirstLoad = !grid
  const mapFirstLoad = mapEnabled && !mapQuery.data
  const configuredMeasures = item.config.measures.length
  const configuredCharts = item.config.charts?.length ?? 0

  async function exportDashboard(kind: AnalyticsExportKind) {
    if (kind === 'csv') {
      exportCsv.mutate()
      return
    }

    setScreenExporting(true)
    setExportError(null)
    try {
      const snapshot = {
        title,
        description: description || undefined,
        selections: crumbs.map((crumb) => `${crumb.label}: ${crumb.display}`),
        kpis: [
          ...(result?.measures ?? []).map((measure) => ({
            label: measure.label,
            value: measure.formattedValue ?? '—',
          })),
          ...(compareResult?.measures ?? []).map((measure) => ({
            label: `${measure.label} (B)`,
            value: measure.formattedValue ?? '—',
          })),
        ],
        labels: {
          selections: t('analyticsStatSelections'),
          allData: t('analyticsBreadcrumbAll'),
          kpis: t('analyticsMeasuresTitle'),
          map: t('analyticsMapTitle'),
        },
      }
      if (kind === 'pdf') {
        await exportAnalyticsPdf(snapshot)
      } else {
        await exportAnalyticsPptx(snapshot)
      }
      await api.recordAnalyticsExport(item.id, kind)
    } catch (error) {
      setExportError((error as Error).message || t('analyticsExportFailed'))
    } finally {
      setScreenExporting(false)
    }
  }

  const sourceLabel =
    result?.source === 'mongo'
      ? t('analyticsSourceMongo')
      : result?.source === 'sqlserver'
        ? t('analyticsSourceSqlServer')
        : result?.source === 'live'
          ? t('analyticsSourceLive')
          : null

  return (
    <section className={`analytics-stage ax-view${query.isFetching ? ' is-refreshing' : ''}`}>
      <header className="ax-top">
        <div className="ax-top-title">
          <Link className="ax-back" to="/analytics">
            {t('back')}
          </Link>
          <div>
            <h1>{title}</h1>
            {description && <p>{description}</p>}
          </div>
        </div>
        <div className="ax-top-actions">
          {sourceLabel && (
            <span
              className={`analytics-source-badge analytics-source-${result?.source === 'live' ? 'live' : 'cache'}`}
              title={
                result?.elapsedMs !== undefined
                  ? t('analyticsSourceElapsed', { ms: result.elapsedMs.toLocaleString() })
                  : undefined
              }
              data-testid="analytics-source-badge"
            >
              <span className="analytics-source-dot" aria-hidden="true" />
              {sourceLabel}
            </span>
          )}
          {isDraft && <span className="badge draft">{t('analyticsDraftPreview')}</span>}
          <div className="ax-menu">
            <button
              type="button"
              className={`analytics-btn ghost${menu === 'bookmarks' ? ' is-on' : ''}`}
              aria-expanded={menu === 'bookmarks'}
              onClick={() => setMenu((current) => (current === 'bookmarks' ? null : 'bookmarks'))}
            >
              <IconBookmark />
              {t('analyticsBookmarks')}
              {bookmarks.length > 0 ? ` ${bookmarks.length}` : ''}
            </button>
            {menu === 'bookmarks' && (
              <div className="ax-menu-panel">
                <BookmarkBar
                  bookmarks={bookmarks}
                  name={bookmarkName}
                  onName={setBookmarkName}
                  saveLabel={t('analyticsSaveBookmark')}
                  emptyLabel={t('analyticsNoBookmarks')}
                  heading={t('analyticsBookmarks')}
                  nameLabel={t('analyticsBookmarkName')}
                  onSave={() => {
                    if (!id || !bookmarkName.trim()) {
                      return
                    }
                    setBookmarks(createBookmark(id, bookmarkName, view))
                    setBookmarkName('')
                  }}
                  onApply={(bookmark) => {
                    setSkip(0)
                    patchChrome({ breakdownOverride: null })
                    setView(reconcileDrill(normalizeViewState(bookmark.view)))
                    setMenu(null)
                  }}
                  onDelete={(bookmarkId) => {
                    if (!id) return
                    setBookmarks(deleteBookmark(id, bookmarkId))
                  }}
                />
              </div>
            )}
          </div>
          <button type="button" className="analytics-btn ghost" onClick={() => void copyLink()}>
            {copyFlash ? t('analyticsLinkCopied') : t('analyticsCopyLink')}
          </button>
          <AnalyticsExportMenu
            pending={exportCsv.isPending || screenExporting}
            onExport={(kind) => {
              void exportDashboard(kind)
            }}
          />
          {item.canEdit && (
            <Link className="analytics-btn ghost" to={`/analytics/${item.id}`}>
              <IconEdit />
              {t('edit')}
            </Link>
          )}
        </div>
      </header>

      {isDraft && <div className="banner analytics-draft-banner">{t('analyticsDraftBanner')}</div>}

      <div className="ax-filters">
        <AnalyticsSmartSearch
          dimensions={result?.dimensions ?? []}
          nullLabel={t('analyticsNullValue')}
          onSelect={toggleValue}
          onSearch={id ? searchValues : undefined}
        />
        <div className="ax-slicers">
          {item.config.dimensions.map((dimension) => {
            const state = resolveDimState(view.dimStates, dimension.id)
            const source = state === 'compare' ? compareResult : result
            const selected = (state === 'compare' ? view.compare : view.default)[dimension.id] ?? []
            const data = source?.dimensions.find((item) => item.dimensionId === dimension.id) ?? {
              dimensionId: dimension.id,
              label: dimension.label,
              selectedValues: selected,
              values: [],
            }
            const locked = view.locked.includes(dimension.id)
            const summary =
              selected.length === 0
                ? t('analyticsAny')
                : selected.length === 1
                  ? selected[0] === ANALYTICS_NULL
                    ? t('analyticsNullValue')
                    : selected[0]
                  : t('analyticsSelectedCount', { n: selected.length })
            return (
              <Slicer
                key={dimension.id}
                label={dimension.label || dimension.field}
                summary={summary}
                active={selected.length > 0}
                locked={locked}
                open={menu === dimension.id}
                onOpen={() => setMenu(dimension.id)}
                onClose={() => setMenu((current) => (current === dimension.id ? null : current))}
              >
                <DimensionWidget
                  dimension={data}
                  search=""
                  state={state}
                  locked={locked}
                  onToggle={(value) => toggleValue(dimension.id, value)}
                  onDrill={(value) => focusDrill(dimension.id, value)}
                  onSelectMatching={(values) => {
                    if (locked) return
                    patchView((current) => ({
                      ...current,
                      [state]: selectValues(current[state], dimension.id, values),
                    }))
                  }}
                  onClear={() => {
                    if (locked) return
                    patchView((current) => ({
                      ...current,
                      [state]: clearDimension(current[state], dimension.id),
                    }))
                  }}
                  onLock={() =>
                    setView((current) => ({
                      ...current,
                      locked: toggleLocked(current.locked, dimension.id),
                    }))
                  }
                  onState={(next) => patchView((current) => moveDimensionState(current, dimension.id, next))}
                  emptyLabel={t('analyticsNoDimensionValues')}
                  nullLabel={t('analyticsNullValue')}
                  selectMatchingLabel={t('analyticsSelectMatching')}
                  clearLabel={t('analyticsClearDim')}
                  lockLabel={locked ? t('analyticsUnlockDim') : t('analyticsLockDim')}
                  searchLabel={t('analyticsFilterDim')}
                  drillLabel={t('analyticsDrillInto', { label: dimension.label || dimension.field })}
                  defaultStateLabel="A"
                  compareStateLabel="B"
                />
              </Slicer>
            )
          })}
        </div>
        <button
          type="button"
          className="analytics-btn ghost"
          disabled={crumbs.length === 0}
          onClick={clearAllFilters}
        >
          {t('analyticsClearSelections')}
        </button>
      </div>

      {crumbs.length > 0 && (
      <FilterBreadcrumbs
        crumbs={crumbs}
        emptyLabel={t('analyticsNoSelections')}
        locked={view.locked}
        onRemove={(dimensionId, value) => {
          if (view.locked.includes(dimensionId)) {
            return
          }
          toggleValue(dimensionId, value)
        }}
        onClearGroup={(dimensionId, state) => {
          if (view.locked.includes(dimensionId)) {
            return
          }
          patchView((current) => ({
            ...current,
            [state]: clearDimension(current[state], dimensionId),
          }))
        }}
        onClearAll={clearAllFilters}
      />
      )}

      {(query.isError || dataQuery.isError || mapQuery.isError || compareQuery.isError || exportError) && (
        <div className="banner error">
          {exportError ||
            query.error?.message ||
            dataQuery.error?.message ||
            mapQuery.error?.message ||
            compareQuery.error?.message}
        </div>
      )}

      <div className="analytics-canvas ax-board" aria-busy={query.isFetching}>
          {configuredMeasures === 0 && (result?.measures.length ?? 0) === 0 ? (
            <div className="analytics-empty-block">{t('analyticsNoMeasures')}</div>
          ) : (
            <div className="analytics-kpi-strip">
              {firstLoad
                ? Array.from({ length: configuredMeasures || 3 }, (_, index) => (
                    <KpiCard key={`skeleton-${index}`} skeleton />
                  ))
                : result!.measures.map((measure, index) => (
                    <KpiCard
                      key={measure.measureId}
                      measure={measure}
                      style={{ animationDelay: `${index * 60}ms` }}
                    />
                  ))}
            </div>
          )}

          {hasCompare && (compareResult?.measures.length ?? 0) > 0 && (
            <div>
              <h2 className="analytics-section-title">{t('analyticsCompareKpis')}</h2>
              <div className="analytics-kpi-strip">
                {compareResult!.measures.map((measure) => (
                  <KpiCard key={`c-${measure.measureId}`} measure={measure} compare />
                ))}
              </div>
            </div>
          )}

          {item.config.dimensions.length > 0 && (
            <DrillCanvas
              dimensions={item.config.dimensions.map((dimension) => ({
                id: dimension.id,
                label: dimension.label || dimension.field,
              }))}
              measures={item.config.measures.map((measure) => ({
                id: measure.id,
                label: measure.label,
              }))}
              drill={view.drill ?? []}
              breakdownId={breakdownId}
              measureId={measureId}
              chartType={drillChartType}
              result={result}
              firstLoad={firstLoad}
              failed={query.isError}
              serverDrill={serverDrill}
              locked={Boolean(breakdownId && view.locked.includes(breakdownId))}
              atLeaf={drillAtLeaf(dimensionIds, view.drill ?? [])}
              onBreakdown={(dimensionId) => patchChrome({ breakdownOverride: dimensionId })}
              onMeasure={(nextMeasure) => patchChrome({ measureOverride: nextMeasure })}
              onChartType={(type) => patchChrome({ chartType: type })}
              onDrill={(value) => {
                if (breakdownId) {
                  focusDrill(breakdownId, value)
                }
              }}
              onDrillTo={(depth) => {
                patchChrome({ breakdownOverride: null })
                patchView((current) => drillToLevel(current, depth))
              }}
            />
          )}

          {firstLoad && configuredCharts > 0 && (
            <div className="analytics-chart-grid">
              {Array.from({ length: configuredCharts }, (_, index) => (
                <article key={`chart-skeleton-${index}`} className="analytics-chart-card" aria-hidden="true">
                  <span className="analytics-skeleton analytics-skeleton-sm" />
                  <span className="analytics-skeleton analytics-skeleton-chart" />
                </article>
              ))}
            </div>
          )}

          {!firstLoad &&
            charts.some(
              (chart) =>
                chart.dimensionId !== breakdownId ||
                chart.measureId !== measureId ||
                chart.state === 'compare',
            ) && (
            <div className="analytics-chart-grid">
              {charts.map((chart) =>
                (chart.dimensionId === breakdownId &&
                  chart.measureId === measureId &&
                  chart.state !== 'compare') ? null : chart.points.length === 0 ? (
                  <article key={chart.chartId} className="analytics-chart-card">
                    <header>
                      <h3>{chart.title}</h3>
                    </header>
                    <div className="analytics-empty-block">{t('analyticsChartEmpty')}</div>
                  </article>
                ) : (
                  <AnalyticsMiniChart
                    key={chart.chartId}
                    chart={chart}
                    nullLabel={t('analyticsNullValue')}
                    onSelect={(value) => toggleValue(chart.dimensionId, value)}
                  />
                ),
              )}
            </div>
          )}

          {item.config.map?.enabled && (
            <div ref={mapPane.ref}>
              <AnalyticsMapView
                points={mapPoints}
                loading={mapFirstLoad}
                emptyLabel={t('analyticsMapEmpty')}
                title={t('analyticsMapTitle')}
                onSelect={(label) => {
                  const match = result?.dimensions.find((dimension) =>
                    dimension.values.some(
                      (value) => value.value.toLowerCase() === label.toLowerCase(),
                    ),
                  )
                  const value = match?.values.find(
                    (item) => item.value.toLowerCase() === label.toLowerCase(),
                  )?.value
                  if (match && value) {
                    toggleValue(match.dimensionId, value)
                  }
                }}
              />
            </div>
          )}

          <div ref={gridPane.ref}>
            <DetailDataGrid
              result={grid}
              loading={dataQuery.isFetching}
              firstLoad={gridFirstLoad}
              skip={skip}
              pageSize={PAGE_SIZE}
              onPrev={() => setSkip((value) => Math.max(0, value - PAGE_SIZE))}
              onNext={() => setSkip((value) => value + PAGE_SIZE)}
              onUseRow={(row) => {
                patchView((current) => ({
                  ...current,
                  default: applyRowToSelections(
                    current.default,
                    row,
                    (grid?.columns ?? []).map((column) => ({
                      id: column.id,
                      field: item.config.detailColumns.find((detail) => detail.id === column.id)?.field
                        ?? item.config.dimensions.find((dimension) => dimension.id === column.id)?.field,
                      label: column.label,
                    })),
                    item.config.dimensions,
                  ),
                }))
              }}
              title={t('analyticsDetailGrid')}
              emptyLabel={t('analyticsNoRows')}
              nullLabel={t('analyticsNullValue')}
              prevLabel={t('analyticsPrev')}
              nextLabel={t('analyticsNextPage')}
              rowFilterLabel={t('analyticsRowFilter')}
              scope={(view.drill ?? [])
                .map((step) => {
                  const label = labels[step.dimensionId] ?? step.dimensionId
                  const value = step.value === ANALYTICS_NULL ? t('analyticsNullValue') : step.value
                  return `${label}: ${value}`
                })
                .join(' · ')}
            />
          </div>
      </div>
    </section>
  )
}

function mergeCharts(
  dashboard: AnalyticsDashboardDetail,
  result: AnalyticsSnapshotResult | undefined,
  compareResult: AnalyticsSnapshotResult | undefined,
): AnalyticsChartResult[] {
  const configured = dashboard.config.charts ?? []
  if (configured.length > 0) {
    return configured
      .map((chart) => {
        const source = chart.state === 'compare' ? compareResult : result
        return source?.charts?.find((item) => item.chartId === chart.id)
      })
      .filter((item): item is AnalyticsChartResult => Boolean(item))
  }

  const first = result?.dimensions[0]
  const measure = result?.measures[0]
  if (!first || !measure) {
    return []
  }
  return [
    {
      chartId: 'auto',
      title: first.label,
      type: 'bar',
      dimensionId: first.dimensionId,
      measureId: measure.measureId,
      state: 'default',
      points: first.values
        .filter((item) => item.state !== 'excluded')
        .slice(0, 20)
        .map((item) => ({ value: item.value, metric: item.count })),
    },
  ]
}

function Slicer({
  label,
  summary,
  active,
  locked,
  open,
  onOpen,
  onClose,
  children,
}: {
  label: string
  summary: string
  active: boolean
  locked: boolean
  open: boolean
  onOpen: () => void
  onClose: () => void
  children: ReactNode
}) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) {
      return
    }
    const onPointer = (event: PointerEvent) => {
      if (!ref.current?.contains(event.target as Node)) {
        onClose()
      }
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    document.addEventListener('pointerdown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('pointerdown', onPointer)
      document.removeEventListener('keydown', onKey)
    }
  }, [open, onClose])

  return (
    <div className={`ax-slicer${open ? ' is-open' : ''}${active ? ' is-active' : ''}${locked ? ' is-locked' : ''}`} ref={ref}>
      <button
        type="button"
        className="ax-slicer-btn"
        aria-expanded={open}
        onClick={() => (open ? onClose() : onOpen())}
      >
        <small>{label}</small>
        <strong>{summary}</strong>
      </button>
      {open && <div className="ax-slicer-panel">{children}</div>}
    </div>
  )
}

function BookmarkBar({
  bookmarks,
  name,
  onName,
  onSave,
  onApply,
  onDelete,
  heading,
  nameLabel,
  saveLabel,
  emptyLabel,
}: {
  bookmarks: AnalyticsBookmark[]
  name: string
  onName: (value: string) => void
  onSave: () => void
  onApply: (bookmark: AnalyticsBookmark) => void
  onDelete: (id: string) => void
  heading: string
  nameLabel: string
  saveLabel: string
  emptyLabel: string
}) {
  return (
    <div className="analytics-bookmarks">
      <strong>{heading}</strong>
      <input
        value={name}
        onChange={(event) => onName(event.target.value)}
        placeholder={nameLabel}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            onSave()
          }
        }}
      />
      <button type="button" className="analytics-btn ghost" disabled={!name.trim()} onClick={onSave}>
        {saveLabel}
      </button>
      <div className="analytics-bookmark-list">
        {bookmarks.length === 0 ? (
          <span className="muted">{emptyLabel}</span>
        ) : (
          bookmarks.map((bookmark) => (
            <span key={bookmark.id} className="analytics-bookmark-chip">
              <button type="button" onClick={() => onApply(bookmark)}>
                {bookmark.name}
              </button>
              <button type="button" className="is-remove" onClick={() => onDelete(bookmark.id)} aria-label="remove">
                ×
              </button>
            </span>
          ))
        )}
      </div>
    </div>
  )
}

const BREADCRUMB_VISIBLE = 3

function groupBreadcrumbs(
  crumbs: { dimensionId: string; label: string; value: string; display: string; state: AnalyticsSelectionState }[],
) {
  const groups: {
    key: string
    dimensionId: string
    label: string
    state: AnalyticsSelectionState
    values: { value: string; display: string }[]
  }[] = []
  const index = new Map<string, (typeof groups)[number]>()
  for (const crumb of crumbs) {
    const key = `${crumb.state}:${crumb.dimensionId}`
    let group = index.get(key)
    if (!group) {
      group = {
        key,
        dimensionId: crumb.dimensionId,
        label: crumb.label,
        state: crumb.state,
        values: [],
      }
      index.set(key, group)
      groups.push(group)
    }
    group.values.push({ value: crumb.value, display: crumb.display })
  }
  return groups
}

function FilterBreadcrumbs({
  crumbs,
  emptyLabel,
  locked,
  onRemove,
  onClearGroup,
  onClearAll,
}: {
  crumbs: { dimensionId: string; label: string; value: string; display: string; state: AnalyticsSelectionState }[]
  emptyLabel: string
  locked: string[]
  onRemove: (dimensionId: string, value: string) => void
  onClearGroup: (dimensionId: string, state: AnalyticsSelectionState) => void
  onClearAll: () => void
}) {
  const { t } = useLocale()
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})
  const groups = useMemo(() => groupBreadcrumbs(crumbs), [crumbs])
  const canClear = crumbs.some((crumb) => !locked.includes(crumb.dimensionId))

  return (
    <nav className={`analytics-breadcrumbs${crumbs.length === 0 ? ' is-empty' : ''}`} aria-label={t('analyticsBreadcrumbTrail')}>
      <button
        type="button"
        className={`analytics-crumb-root${crumbs.length === 0 ? ' is-current' : ''}`}
        disabled={!canClear}
        onClick={onClearAll}
        title={t('analyticsClearSelections')}
      >
        {t('analyticsBreadcrumbAll')}
      </button>
      {groups.map((group) => {
        const isLocked = locked.includes(group.dimensionId)
        const open = expanded[group.key]
        const hidden = open ? 0 : Math.max(0, group.values.length - BREADCRUMB_VISIBLE)
        const shown = hidden > 0 ? group.values.slice(0, BREADCRUMB_VISIBLE) : group.values
        return (
          <div key={group.key} className="analytics-crumb-step">
            <span className="analytics-crumb-sep" aria-hidden="true">
              ›
            </span>
            <div
              className={`analytics-crumb-group${group.state === 'compare' ? ' is-compare' : ''}${isLocked ? ' is-locked' : ''}`}
              role="list"
            >
              <span className="analytics-crumb-field">
                {group.state === 'compare' && (
                  <i className="analytics-crumb-badge" title={t('analyticsBreadcrumbCompare')}>
                    B
                  </i>
                )}
                {group.label}
                {isLocked && (
                  <span className="analytics-crumb-lock-wrap" title={t('analyticsBreadcrumbLocked')}>
                    <IconLock className="analytics-crumb-lock" />
                  </span>
                )}
              </span>
              {shown.map((item) => (
                <button
                  key={item.value}
                  type="button"
                  className="analytics-crumb-chip"
                  role="listitem"
                  disabled={isLocked}
                  onClick={() => onRemove(group.dimensionId, item.value)}
                  title={`${group.label}: ${item.display}`}
                >
                  <span>{item.display}</span>
                  {!isLocked && <em aria-hidden="true">×</em>}
                </button>
              ))}
              {hidden > 0 && (
                <button
                  type="button"
                  className="analytics-crumb-more"
                  onClick={() => setExpanded((current) => ({ ...current, [group.key]: true }))}
                >
                  {t('analyticsBreadcrumbMore', { n: hidden })}
                </button>
              )}
              {!isLocked && group.values.length > 1 && (
                <button
                  type="button"
                  className="analytics-crumb-clear-group"
                  title={t('analyticsBreadcrumbClearGroup', { label: group.label })}
                  aria-label={t('analyticsBreadcrumbClearGroup', { label: group.label })}
                  onClick={() => onClearGroup(group.dimensionId, group.state)}
                >
                  ×
                </button>
              )}
            </div>
          </div>
        )
      })}
      {crumbs.length === 0 && <span className="analytics-crumb-empty">{emptyLabel}</span>}
    </nav>
  )
}

function DimensionWidget({
  dimension,
  search,
  state,
  locked,
  onToggle,
  onDrill,
  onSelectMatching,
  onClear,
  onLock,
  onState,
  emptyLabel,
  nullLabel,
  selectMatchingLabel,
  clearLabel,
  lockLabel,
  searchLabel,
  drillLabel,
  defaultStateLabel,
  compareStateLabel,
  style,
}: {
  dimension: AnalyticsDimensionResult
  search: string
  state: AnalyticsSelectionState
  locked: boolean
  onToggle: (value: string) => void
  onDrill: (value: string) => void
  onSelectMatching: (values: string[]) => void
  onClear: () => void
  onLock: () => void
  onState: (state: AnalyticsSelectionState) => void
  emptyLabel: string
  nullLabel: string
  selectMatchingLabel: string
  clearLabel: string
  lockLabel: string
  searchLabel: string
  drillLabel: string
  defaultStateLabel: string
  compareStateLabel: string
  style?: CSSProperties
}) {
  const [localSearch, setLocalSearch] = useState('')
  const max = maxDimensionCount(dimension.values)
  const terms = `${search} ${localSearch}`.toLowerCase().split(/\s+/).filter(Boolean)
  const values = terms.length
    ? dimension.values.filter((item) => {
        const label = (item.value === ANALYTICS_NULL ? nullLabel : item.value).toLowerCase()
        return terms.every((term) => label.includes(term))
      })
    : dimension.values
  const selectedCount = dimension.selectedValues.length
  const isFiltered = values.length !== dimension.values.length
  const matching = values.filter((item) => item.state !== 'excluded').map((item) => item.value)

  return (
    <article className={`analytics-dim${locked ? ' is-locked' : ''}${state === 'compare' ? ' is-compare' : ''}`} style={style}>
      <header className="analytics-dim-head">
        <h3>{dimension.label}</h3>
        <div className="analytics-dim-head-meta">
          <span className="analytics-state-toggle" role="group" aria-label="state">
            <button
              type="button"
              className={state === 'default' ? 'is-active' : ''}
              onClick={() => onState('default')}
            >
              {defaultStateLabel}
            </button>
            <button
              type="button"
              className={state === 'compare' ? 'is-active' : ''}
              onClick={() => onState('compare')}
            >
              {compareStateLabel}
            </button>
          </span>
          <button
            type="button"
            className={`analytics-icon-btn${locked ? ' is-active' : ''}`}
            title={lockLabel}
            aria-label={lockLabel}
            onClick={onLock}
          >
            {locked ? <IconLock /> : <IconUnlock />}
          </button>
          <span className="analytics-dim-tally">
            {selectedCount > 0 && <b>{selectedCount}</b>}
            {isFiltered
              ? `${values.length}/${dimension.values.length}`
              : dimension.values.length.toLocaleString()}
          </span>
        </div>
      </header>
      <div className="analytics-dim-tools">
        <input
          value={localSearch}
          onChange={(event) => setLocalSearch(event.target.value)}
          placeholder={searchLabel}
          aria-label={searchLabel}
        />
        <button
          type="button"
          className="analytics-icon-btn"
          disabled={locked || matching.length === 0}
          title={selectMatchingLabel}
          aria-label={selectMatchingLabel}
          onClick={() => onSelectMatching(matching)}
        >
          +
        </button>
        <button
          type="button"
          className="analytics-icon-btn"
          disabled={locked || selectedCount === 0}
          title={clearLabel}
          aria-label={clearLabel}
          onClick={onClear}
        >
          ×
        </button>
      </div>
      {values.length === 0 ? (
        <div className="analytics-dim-empty">{emptyLabel}</div>
      ) : (
        <ul className="analytics-dim-list">
          {values.map((item) => (
            <DimensionRow
              key={item.value}
              item={item}
              max={max}
              nullLabel={nullLabel}
              disabled={locked}
              drillLabel={drillLabel}
              onToggle={onToggle}
              onDrill={onDrill}
            />
          ))}
        </ul>
      )}
    </article>
  )
}

function DimensionRow({
  item,
  max,
  nullLabel,
  disabled,
  drillLabel,
  onToggle,
  onDrill,
}: {
  item: AnalyticsDimensionValue
  max: number
  nullLabel: string
  disabled: boolean
  drillLabel: string
  onToggle: (value: string) => void
  onDrill: (value: string) => void
}) {
  const width = max <= 0 ? 0 : Math.max(6, Math.round((item.count / max) * 100))
  const label = item.value === ANALYTICS_NULL ? nullLabel : item.value
  const canDrill = !disabled && item.state !== 'excluded'
  return (
    <li className={`analytics-dim-row ${item.state}`}>
      <button type="button" className="analytics-dim-hit" disabled={disabled} onClick={() => onToggle(item.value)}>
        <span className="analytics-dim-label" title={label}>
          {label}
        </span>
        <span className="analytics-dim-meter" aria-hidden="true">
          <span style={{ width: `${width}%` }} />
        </span>
        <span className="analytics-dim-count">{item.count.toLocaleString()}</span>
      </button>
      <button
        type="button"
        className="analytics-dim-drill"
        disabled={!canDrill}
        title={drillLabel}
        aria-label={drillLabel}
        onClick={() => onDrill(item.value)}
      >
        <IconDrill />
      </button>
    </li>
  )
}

function DrillCanvas({
  dimensions,
  measures,
  drill,
  breakdownId,
  measureId,
  chartType,
  result,
  firstLoad,
  failed,
  serverDrill,
  locked,
  atLeaf,
  onBreakdown,
  onMeasure,
  onChartType,
  onDrill,
  onDrillTo,
}: {
  dimensions: { id: string; label: string }[]
  measures: { id: string; label: string }[]
  drill: AnalyticsViewState['drill']
  breakdownId: string | null
  measureId: string | null
  chartType: AnalyticsChartType
  result: AnalyticsSnapshotResult | undefined
  firstLoad: boolean
  failed: boolean
  serverDrill: boolean
  locked: boolean
  atLeaf: boolean
  onBreakdown: (dimensionId: string) => void
  onMeasure: (measureId: string) => void
  onChartType: (type: AnalyticsChartType) => void
  onDrill: (value: string) => void
  onDrillTo: (depth: number) => void
}) {
  const drillReady =
    !serverDrill ||
    (result?.charts ?? []).some(
      (chart) =>
        chart.chartId === DRILL_CHART_ID &&
        chart.dimensionId === breakdownId &&
        chart.measureId === measureId,
    )
  const series = breakdownId
    ? resolveDrillSeries({
        dimensionId: breakdownId,
        measureId,
        dimensions: result?.dimensions ?? [],
        charts: result?.charts,
        serverDrill,
      })
    : { points: [], usesCount: true }
  const selectedValues =
    result?.dimensions.find((dimension) => dimension.dimensionId === breakdownId)?.selectedValues ?? []

  return (
    <AnalyticsDrilldown
      dimensions={dimensions}
      measures={measures}
      drill={drill}
      breakdownId={breakdownId}
      measureId={measureId}
      chartType={chartType}
      points={series.points}
      usesCount={series.usesCount}
      loading={Boolean(breakdownId) && !failed && (firstLoad || (serverDrill && !drillReady))}
      locked={locked}
      atLeaf={atLeaf}
      selectedValues={selectedValues}
      onBreakdown={onBreakdown}
      onMeasure={onMeasure}
      onChartType={onChartType}
      onDrill={onDrill}
      onDrillTo={onDrillTo}
    />
  )
}

function KpiCard({
  measure,
  style,
  compare,
  skeleton,
}: {
  measure?: AnalyticsMeasureResult
  style?: CSSProperties
  compare?: boolean
  skeleton?: boolean
}) {
  if (skeleton || !measure) {
    return (
      <article className="analytics-kpi is-skeleton" aria-hidden="true">
        <span className="analytics-skeleton analytics-skeleton-sm" />
        <span className="analytics-skeleton analytics-skeleton-lg" />
      </article>
    )
  }

  return (
    <article className={`analytics-kpi${compare ? ' is-compare' : ''}`} style={style}>
      <div className="analytics-kpi-label">{measure.label}</div>
      <div className="analytics-kpi-value">{measure.formattedValue ?? '—'}</div>
      <div className="analytics-kpi-sheen" aria-hidden="true" />
    </article>
  )
}

function DetailDataGrid({
  result,
  loading,
  firstLoad,
  skip,
  pageSize,
  onPrev,
  onNext,
  onUseRow,
  title,
  emptyLabel,
  nullLabel,
  prevLabel,
  nextLabel,
  rowFilterLabel,
  scope,
}: {
  result: AnalyticsDataResult | undefined
  loading: boolean
  firstLoad: boolean
  skip: number
  pageSize: number
  onPrev: () => void
  onNext: () => void
  onUseRow: (row: Record<string, unknown>) => void
  title: string
  emptyLabel: string
  nullLabel: string
  prevLabel: string
  nextLabel: string
  rowFilterLabel: string
  scope?: string
}) {
  const columns = result?.columns ?? []
  const rows = result?.rows ?? []
  const total = result?.totalCount ?? 0
  const from = total === 0 ? 0 : skip + 1
  const to = Math.min(skip + rows.length, total)

  return (
    <section className={`analytics-grid-shell${loading ? ' is-loading' : ''}`} aria-busy={loading}>
      <div className="analytics-grid-head">
        <div>
          <h2>{title}</h2>
          <p>
            {firstLoad ? (
              '—'
            ) : (
              <>
                {from}–{to} <span>/</span> {total.toLocaleString()}
                {scope ? <> · {scope}</> : null}
              </>
            )}
          </p>
        </div>
        <div className="analytics-pager">
          <button type="button" className="analytics-btn ghost" disabled={skip <= 0} onClick={onPrev}>
            {prevLabel}
          </button>
          <button
            type="button"
            className="analytics-btn ghost"
            disabled={skip + pageSize >= total}
            onClick={onNext}
          >
            {nextLabel}
          </button>
        </div>
      </div>
      <div className="analytics-grid-scroll">
        <table>
          <thead>
            <tr>
              <th className="analytics-row-actions-col" />
              {(columns.length > 0 ? columns : [{ id: 'empty', label: '—' }]).map((column) => (
                <th key={column.id}>{column.label}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {firstLoad ? (
              Array.from({ length: 6 }, (_, rowIndex) => (
                <tr key={`skeleton-${rowIndex}`} aria-hidden="true">
                  {Array.from({ length: Math.max(columns.length, 4) + 1 }, (_, cellIndex) => (
                    <td key={cellIndex}>
                      <span className="analytics-skeleton analytics-skeleton-cell" />
                    </td>
                  ))}
                </tr>
              ))
            ) : rows.length === 0 ? (
              <tr>
                <td colSpan={Math.max(columns.length, 1) + 1}>
                  <div className="analytics-empty-block">{emptyLabel}</div>
                </td>
              </tr>
            ) : (
              rows.map((row, index) => (
                <tr key={index} style={{ animationDelay: `${Math.min(index, 12) * 18}ms` }}>
                  <td className="analytics-row-actions-col">
                    <button
                      type="button"
                      className="analytics-icon-btn"
                      title={rowFilterLabel}
                      aria-label={rowFilterLabel}
                      onClick={() => onUseRow(row)}
                    >
                      <IconFilter />
                    </button>
                  </td>
                  {columns.map((column) => {
                    const raw = row[column.id]
                    const text = raw == null || raw === '' ? nullLabel : String(raw)
                    const href = column.urlTemplate
                      ? interpolateUrl(column.urlTemplate, row, columns, column.id)
                      : null
                    return (
                      <td key={column.id} className={href ? 'has-cell-link' : undefined}>
                        {href ? (
                          <a className="analytics-cell-link" {...cellLinkProps(href)}>
                            {text}
                          </a>
                        ) : (
                          text
                        )}
                      </td>
                    )
                  })}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}
