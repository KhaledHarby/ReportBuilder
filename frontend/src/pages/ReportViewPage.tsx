import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import type { ChartType, ExportFormat, LayoutFilter, LayoutOrderBy, ReportLayout } from '../api/types'
import { ChartView } from '../components/ChartView'
import { DetailGrid } from '../components/DetailGrid'
import { EmbedUrlDialog } from '../components/EmbedUrlDialog'
import { ResultGrid } from '../components/ResultGrid'
import { ViewFilters } from '../components/ViewFilters'
import { ExportMenu } from '../components/ExportMenu'
import { formatStamp } from '../lib/format'
import { filterDisplayLabel, isFilterHidden, lockFixedFilters, missingRequiredFilters } from '../lib/filters'
import {
  allowedDrillthroughFields,
  applyQueryParamsToFilters,
  filtersToSearchParams,
  unusedDrillthroughKeys,
} from '../lib/reportLinks'
import {
  buildFormatMap,
  buildUrlMap,
  fieldToResultName,
  resultNameToField,
  urlTokensForResult,
} from '../lib/columnFormat'
import { buildConditionalFormatMap } from '../lib/conditionalFormat'
import { isUserScopeConfigured } from '../lib/userScope'
import { isDetailConfigured } from '../lib/detailLayout'
import {
  CHART_TYPE_KEYS,
  chartViewTypeChoices,
  isChartLayout,
  normalizeChartType,
} from '../lib/chart'
import { qualifyLookupLabels, withLookupColumns } from '../lib/lookupColumns'
import { usePageTitle } from '../lib/pageTitle'
import { useAuth } from '../auth/AuthProvider'
import { useLocale } from '../i18n/LocaleProvider'
import { columnPresentation, layoutForLang, localizeResultColumns, localizedText, resultForLang } from '../i18n/localized'
import { bilingualExportName } from '../lib/download'

const EMPTY_FILTERS: LayoutFilter[] = []

function withFilters(layout: ReportLayout, filters: LayoutFilter[]): ReportLayout {
  return { ...layout, filters }
}

function sameOrderBy(left: LayoutOrderBy[], right: LayoutOrderBy[]) {
  if (left.length !== right.length) {
    return false
  }
  return left.every((item, index) => {
    const other = right[index]
    return (
      item.field.toLowerCase() === other.field.toLowerCase() &&
      (item.direction ?? 'asc') === (other.direction ?? 'asc')
    )
  })
}

export function ReportViewPage({ embedded = false }: { embedded?: boolean }) {
  const { can, isAdmin } = useAuth()
  const { lang, t } = useLocale()
  const { id } = useParams()
  const [searchParams, setSearchParams] = useSearchParams()
  const [skip, setSkip] = useState(0)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [filters, setFilters] = useState<LayoutFilter[] | null>(null)
  const [orderByOverride, setOrderByOverride] = useState<LayoutOrderBy[] | null>(null)
  const [embedOpen, setEmbedOpen] = useState(false)
  const [chartType, setChartType] = useState<ChartType | null>(null)

  const report = useQuery({
    queryKey: ['report', id],
    queryFn: () => api.getReport(id!),
    enabled: Boolean(id),
  })
  usePageTitle(
    report.data ? localizedText(lang, report.data.name, report.data.nameArabic) : null,
  )

  const isChart = isChartLayout(report.data?.layout)
  const take = isChart ? 200 : 50

  const dataset = useQuery({
    queryKey: ['dataset', report.data?.datasetId],
    queryFn: () => api.getDataset(report.data!.datasetId),
    enabled: Boolean(report.data?.datasetId && can('datasets.view')),
  })

  const filterColumns = report.data?.filterColumns ?? []
  const gridColumns = dataset.data?.columns ?? filterColumns

  const savedFilters = report.data?.layout.filters ?? EMPTY_FILTERS
  const savedOrderBy = report.data?.layout.orderBy ?? []
  const queryKey = searchParams.toString()
  const drillthroughFields = useMemo(
    () => allowedDrillthroughFields(report.data?.layout, gridColumns),
    [report.data?.layout, gridColumns],
  )
  const seededFilters = useMemo(
    () =>
      applyQueryParamsToFilters(
        savedFilters,
        new URLSearchParams(queryKey),
        drillthroughFields,
      ),
    [savedFilters, queryKey, drillthroughFields],
  )
  const unusedLinkFields = useMemo(
    () => unusedDrillthroughKeys(new URLSearchParams(queryKey), drillthroughFields),
    [queryKey, drillthroughFields],
  )
  const baselineFilters = seededFilters ?? savedFilters
  const activeFilters = lockFixedFilters(baselineFilters, filters ?? baselineFilters)
  const activeOrderBy = orderByOverride ?? savedOrderBy
  const filterKey = JSON.stringify(activeFilters)
  const sortKey = JSON.stringify(activeOrderBy)
  const layout = report.data
    ? { ...withFilters(report.data.layout, activeFilters), orderBy: activeOrderBy }
    : null

  useEffect(() => {
    setFilters(null)
    setOrderByOverride(null)
    setChartType(null)
    setSkip(0)
  }, [id])

  useEffect(() => {
    setFilters(null)
  }, [queryKey])

  useEffect(() => {
    setSkip(0)
  }, [filterKey, sortKey])

  const requiredNames = useMemo(
    () =>
      missingRequiredFilters(activeFilters)
        .map((filter) => filterDisplayLabel(lang, filter, filterColumns.find(
          (column) => column.columnName.toLowerCase() === filter.field.toLowerCase(),
        )))
        .join(', '),
    [activeFilters, filterColumns, lang],
  )
  const filtersReady = requiredNames.length === 0

  const result = useQuery({
    queryKey: ['run', id, skip, take, filterKey, sortKey, lang],
    queryFn: () =>
      api.runReport(id!, {
        skip: isChart ? 0 : skip,
        take,
        layout: layout!,
        lang,
      }),
    enabled: Boolean(id && report.data && layout && filtersReady),
    placeholderData: keepPreviousData,
  })

  const download = useMutation({
    mutationFn: ({ format, file }: { format: ExportFormat; file?: FileSystemFileHandle }) => {
      if (!report.data || !layout || !id) {
        throw new Error(t('reportLoading'))
      }
      if (!filtersReady) {
        throw new Error(t('requiredFiltersBanner', { names: requiredNames }))
      }
      // Prefer report-scoped export so Insights attributes the download to this report.
      // When filters differ from the saved layout, fall back to preview export with the live layout.
      const filtersChanged = filterKey !== JSON.stringify(savedFilters)
      const sortChanged = !sameOrderBy(activeOrderBy, savedOrderBy)
      if (filtersChanged || sortChanged) {
        return api.exportPreview(
          {
            name: report.data.name,
            nameArabic: report.data.nameArabic ?? undefined,
            datasetId: report.data.datasetId,
            layout,
          },
          format,
          lang,
          file,
        )
      }
      return api.exportReport(
        id,
        format,
        lang,
        file,
        bilingualExportName(report.data.name, report.data.nameArabic),
      )
    },
    onMutate: () => setDownloadError(null),
    onError: (error) => setDownloadError((error as Error).message),
  })

  const appliedCount = useMemo(
    () =>
      activeFilters.filter(
        (filter, index) =>
          !isFilterHidden(savedFilters[index] ?? filter) &&
          (filter.operator === 'isNull' ||
            filter.operator === 'isNotNull' ||
            Boolean(filter.value?.trim())),
      ).length,
    [activeFilters, savedFilters],
  )
  const visibleSavedCount = savedFilters.filter((filter) => !isFilterHidden(filter)).length

  const publishFilters = (next: LayoutFilter[]) => {
    const params = filtersToSearchParams(searchParams, savedFilters, next)
    if (params.toString() !== searchParams.toString()) {
      setSearchParams(params, { replace: true })
    }
  }

  const onFiltersChange = (next: LayoutFilter[]) => {
    const locked = lockFixedFilters(baselineFilters, next)
    setFilters(locked)
    publishFilters(locked)
  }

  const onFiltersReset = () => {
    setFilters(null)
    publishFilters(savedFilters)
  }

  const labeledResult = useMemo(
    () =>
      result.data
        ? localizeResultColumns(
            lang,
            result.data,
            qualifyLookupLabels(withLookupColumns(gridColumns)),
            layout ?? undefined,
          )
        : null,
    [result.data, gridColumns, lang, layout],
  )
  const localizedResult = useMemo(
    () =>
      labeledResult && layout
        ? resultForLang(labeledResult, layout, lang)
        : labeledResult,
    [labeledResult, layout, lang],
  )
  const viewLayout = useMemo(
    () => (layout ? layoutForLang(layout, lang) : null),
    [layout, lang],
  )
  const chartTypeChoices = useMemo(
    () => (layout ? chartViewTypeChoices(layout) : []),
    [layout],
  )
  const savedChartType = normalizeChartType(layout?.chart?.type)
  const activeChartType =
    chartType && chartTypeChoices.includes(chartType) ? chartType : savedChartType
  const chartLayout = useMemo(() => {
    const source = viewLayout ?? layout
    if (!source?.chart || !activeChartType) {
      return source
    }
    if (normalizeChartType(source.chart.type) === activeChartType) {
      return source
    }
    return {
      ...source,
      chart: { ...source.chart, type: activeChartType },
    }
  }, [viewLayout, layout, activeChartType])
  const columnFormats = useMemo(
    () => buildFormatMap(layout ?? undefined, labeledResult),
    [layout, labeledResult],
  )
  const columnUrls = useMemo(
    () => buildUrlMap(layout ?? undefined, labeledResult),
    [layout, labeledResult],
  )
  const columnConditionalFormats = useMemo(
    () => buildConditionalFormatMap(layout ?? undefined, labeledResult),
    [layout, labeledResult],
  )
  const urlTokens = useMemo(
    () => urlTokensForResult(layout ?? undefined, labeledResult),
    [layout, labeledResult],
  )
  const presentation = useMemo(
    () => columnPresentation(layout, labeledResult, lang),
    [layout, labeledResult, lang],
  )
  const gridSort = useMemo(() => {
    const first = activeOrderBy[0]
    if (!first?.field || !layout) {
      return null
    }
    return {
      column: fieldToResultName(layout, labeledResult, first.field),
      dir: first.direction === 'desc' ? 'desc' : 'asc',
    } as const
  }, [activeOrderBy, layout, labeledResult])

  const onSortChange = (next: { column: string; dir: 'asc' | 'desc' } | null) => {
    if (!layout || !next) {
      setOrderByOverride(null)
      return
    }
    const override: LayoutOrderBy[] = [
      {
        field: resultNameToField(layout, labeledResult, next.column),
        direction: next.dir,
      },
    ]
    setOrderByOverride(sameOrderBy(override, savedOrderBy) ? null : override)
  }

  return (
    <section className={`page report-view${embedded ? ' is-embedded' : ''}`}>
      {!embedded && <header className="view-hero">
        <div>
          <div className="kicker">
            {report.data?.status === 'draft' ? t('draftView') : t('liveView')}
          </div>
          <h1>
            {report.data
              ? localizedText(lang, report.data.name, report.data.nameArabic)
              : t('report')}
            {isUserScopeConfigured(report.data?.layout.userScope) && (
              <span className="badge user-scope-badge">{t('userScopeActive')}</span>
            )}
          </h1>
          <p className="lede">
            {report.data?.datasetName
              ? t('streamingFrom', {
                  name: localizedText(
                    lang,
                    report.data.datasetName,
                    report.data.datasetNameArabic,
                  ),
                })
              : t('loadingTemplate')}
          </p>
          <div className="view-meta">
            {report.data && (
              <span
                className={`badge ${report.data.status === 'published' ? 'published' : 'draft'}`}
              >
                {report.data.status === 'published' ? t('published') : t('draft')}
              </span>
            )}
            {report.data?.isAdminCreated && (
              <span className="badge admin">{t('adminReport')}</span>
            )}
            {report.data?.accessMode === 'Roles' ? (
              <span className="badge">{t('selectedRole')}</span>
            ) : report.data?.isRestricted ? (
              <span className="badge">{t('selectedPeople')}</span>
            ) : null}
            {isChart && <span className="badge">{t('chartBadge')}</span>}
            {report.data?.createdByName && (
              <span className="badge">{t('byUser', { name: report.data.createdByName })}</span>
            )}
            <span className="live-pill">
              <i /> {t('live')}
            </span>
            {layout && (
              <>
                <span className="badge">{t('columnsCount', { n: layout.columns.length })}</span>
                <span className="badge">{t('groupsCount', { n: layout.groupBy.length })}</span>
                <span className="badge">
                  {t('filtersCount', {
                    applied: appliedCount,
                    saved: visibleSavedCount,
                  })}
                </span>
              </>
            )}
            {report.data && (
              <span className="badge">
                {t('savedAt', { when: formatStamp(report.data.updatedAt, lang) })}
              </span>
            )}
          </div>
        </div>
        <div className="header-actions">
          {isAdmin && id && (
            <button type="button" className="btn" onClick={() => setEmbedOpen(true)}>
              {t('embedUrl')}
            </button>
          )}
          {can('reports.export') && (
            <ExportMenu
              primary
              label={t('downloadAll')}
              fileName={
                report.data
                  ? bilingualExportName(report.data.name, report.data.nameArabic)
                  : undefined
              }
              disabled={!id || !filtersReady}
              pending={download.isPending}
              onExport={(format, file) => download.mutate({ format, file })}
            />
          )}
          {report.data?.canEdit && (
            <Link className="btn" to={`/reports/${id}`}>
              {t('editLayout')}
            </Link>
          )}
          <Link className="btn" to="/">
            {t('allReports')}
          </Link>
        </div>
      </header>}

      {embedOpen && id && (
        <EmbedUrlDialog reportId={id} filters={activeFilters} onClose={() => setEmbedOpen(false)} />
      )}

      {(report.isError || result.isError || downloadError) && (
        <div className="banner error">
          {downloadError ||
            (report.error as Error | undefined)?.message ||
            (result.error as Error | undefined)?.message}
        </div>
      )}

      {report.data && !filtersReady && (
        <div className="banner error">
          {t('requiredFiltersBanner', { names: requiredNames })}
        </div>
      )}

      {!embedded && report.data && unusedLinkFields.length > 0 && (
        <div className="banner">
          {t('drillthroughUnusedParams', { fields: unusedLinkFields.join(', ') })}
        </div>
      )}

      {report.data && savedFilters.length > 0 && (
        <ViewFilters
          datasetId={report.data.datasetId}
          reportId={report.data.id}
          columns={filterColumns}
          saved={baselineFilters}
          filters={activeFilters}
          columnsPerRow={report.data.layout.filtersPerRow}
          onChange={onFiltersChange}
          onReset={onFiltersReset}
        />
      )}

      <div className="card">
        {isChart && chartTypeChoices.length > 1 && (
          <div className="chart-type-switch" role="group" aria-label={t('chartType')}>
            {chartTypeChoices.map((type) => (
              <button
                key={type}
                type="button"
                className={activeChartType === type ? 'active' : ''}
                aria-pressed={activeChartType === type}
                onClick={() => setChartType(type)}
              >
                {t(CHART_TYPE_KEYS[type])}
              </button>
            ))}
          </div>
        )}
        {isChart && chartLayout ? (
          <ChartView
            result={filtersReady ? localizedResult : null}
            layout={chartLayout}
            loading={filtersReady && result.isFetching}
            error={filtersReady && result.isError ? (result.error as Error).message : null}
            emptyHint={t('chartEmptyHint')}
          />
        ) : (
          <ResultGrid
            result={filtersReady ? localizedResult : null}
            columnFormats={columnFormats}
            columnUrls={columnUrls}
            columnConditionalFormats={columnConditionalFormats}
            urlTokens={urlTokens}
            loading={filtersReady && result.isFetching}
            error={filtersReady && result.isError ? (result.error as Error).message : null}
            emptyHint={filtersReady ? t('emptyHintView') : t('requiredFiltersBanner', { names: requiredNames })}
            allowSort
            sort={gridSort}
            onSortChange={onSortChange}
            align="center"
            fillWidth
            autoFit
            stickyFields={(viewLayout ?? layout)?.columns
              ?.filter((column) => column.sticky)
              .map((column) => column.field)}
            columnAlign={presentation.columnAlign}
            columnBands={presentation.columnBands}
            columnWrap={presentation.columnWrap}
            onPageChange={setSkip}
            detailParentFields={
              layout && isDetailConfigured(layout.detail)
                ? [
                    fieldToResultName(layout, localizedResult, layout.detail.parentField ?? ''),
                    layout.detail.parentField ?? '',
                  ]
                : undefined
            }
            renderDetail={
              id && layout && isDetailConfigured(layout.detail)
                ? (parentValue) => (
                    <DetailGrid
                      reportId={id}
                      layout={layout}
                      parentValue={parentValue}
                      lang={lang}
                      sendLayout={false}
                    />
                  )
                : undefined
            }
          />
        )}
      </div>
    </section>
  )
}
