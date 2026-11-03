import type {
  ChartConfig,
  ChartType,
  DatasetColumn,
  LayoutColumn,
  ReportLayout,
  ReportResult,
} from '../api/types'
import { emptyLayout } from '../api/types'
import { isLookupField } from './lookupColumns'

export const CHART_TYPES: ChartType[] = ['bar', 'line', 'area', 'pie', 'doughnut']

export const CARTESIAN_CHART_TYPES = ['bar', 'line', 'area'] as const

export type CartesianChartType = (typeof CARTESIAN_CHART_TYPES)[number]

export function isCartesianChartType(type: string | null | undefined): type is CartesianChartType {
  const value = type?.trim().toLowerCase()
  return value === 'bar' || value === 'line' || value === 'area'
}

export function normalizeChartType(type: string | null | undefined): ChartType | null {
  const value = type?.trim().toLowerCase()
  return CHART_TYPES.find((item) => item === value) ?? null
}

export function chartViewTypeChoices(layout: ReportLayout): ChartType[] {
  if (normalizeChartType(layout.chart?.type) == null) {
    return []
  }
  if ((layout.groupBy?.[1] ?? '').trim()) {
    return [...CARTESIAN_CHART_TYPES]
  }
  return [...CHART_TYPES]
}

export const CHART_TYPE_KEYS: Record<ChartType, 'chartBar' | 'chartLine' | 'chartArea' | 'chartPie' | 'chartDoughnut'> =
  {
    bar: 'chartBar',
    line: 'chartLine',
    area: 'chartArea',
    pie: 'chartPie',
    doughnut: 'chartDoughnut',
  }

const NUMERIC_TYPES = new Set([
  'int',
  'bigint',
  'smallint',
  'tinyint',
  'decimal',
  'numeric',
  'money',
  'smallmoney',
  'float',
  'real',
])

export function isNumericSqlType(sqlType: string) {
  return NUMERIC_TYPES.has(sqlType.toLowerCase())
}

export function isChartLayout(layout: ReportLayout | null | undefined) {
  return layout?.kind === 'chart'
}

export function defaultChartConfig(type: ChartType = 'bar'): ChartConfig {
  return {
    type,
    stacked: false,
    showLegend: true,
    showDataLabels: false,
  }
}

export function emptyChartLayout(type: ChartType = 'bar'): ReportLayout {
  return {
    kind: 'chart',
    columns: [],
    groupBy: [],
    filters: [],
    orderBy: [],
    chart: defaultChartConfig(type),
  }
}

export function normalizeLayout(layout: ReportLayout): ReportLayout {
  const kind = layout.kind === 'chart' ? 'chart' : 'table'
  const base = emptyLayout()
  return {
    ...base,
    ...layout,
    kind,
    columns: layout.columns ?? [],
    groupBy: layout.groupBy ?? [],
    filters: layout.filters ?? [],
    orderBy: layout.orderBy ?? [],
    chart: kind === 'chart' ? { ...defaultChartConfig(), ...layout.chart } : layout.chart ?? null,
    userScope: layout.userScope ?? null,
  }
}

export function chartCategory(layout: ReportLayout) {
  return layout.groupBy[0] ?? ''
}

export function chartSeriesField(layout: ReportLayout) {
  return layout.groupBy[1] ?? ''
}

export function chartMeasures(layout: ReportLayout) {
  return layout.columns.filter((column) => Boolean(column.aggregate))
}

/**
 * Lookup label shown for a grouped field. The engine can only group by the physical column, so a
 * chart that wants the lookup text stores the lookup column directly after that field in
 * `columns`; the backend fills it per row and the view uses it for axis and legend text.
 */
export function chartLabelField(layout: ReportLayout, field: string) {
  if (!field) {
    return ''
  }
  const index = layout.columns.findIndex(
    (column) => !column.aggregate && column.field.toLowerCase() === field.toLowerCase(),
  )
  const next = index >= 0 ? layout.columns[index + 1] : undefined
  return next && !next.aggregate && isLookupField(next.field) ? next.field : ''
}

export function chartCategoryLabel(layout: ReportLayout) {
  return chartLabelField(layout, chartCategory(layout))
}

export function chartSeriesLabel(layout: ReportLayout) {
  return chartLabelField(layout, chartSeriesField(layout))
}

export function withChartConfig(layout: ReportLayout, chart: Partial<ChartConfig>): ReportLayout {
  const type = chart.type ?? layout.chart?.type ?? 'bar'
  const next: ChartConfig = {
    ...defaultChartConfig(type),
    ...layout.chart,
    ...chart,
    type,
  }
  const series = type === 'pie' || type === 'doughnut' ? '' : chartSeriesField(layout)
  return withChartPicks(layout, {
    category: chartCategory(layout),
    categoryLabel: chartCategoryLabel(layout),
    series,
    seriesLabel: series ? chartSeriesLabel(layout) : '',
    measures: chartMeasures(layout),
    chart: next,
  })
}

export function withChartPicks(
  layout: ReportLayout,
  picks: {
    category: string
    /** Lookup column (`lookup:<columnId>:control`) whose text labels the category values. */
    categoryLabel?: string
    series?: string
    seriesLabel?: string
    measures: LayoutColumn[]
    chart?: ChartConfig
  },
): ReportLayout {
  const chart = { ...defaultChartConfig(), ...layout.chart, ...picks.chart }
  const pie = chart.type === 'pie' || chart.type === 'doughnut'
  const category = picks.category
  const series = pie ? '' : picks.series && picks.series !== category ? picks.series : ''
  const groupBy = [category, series].filter(Boolean)
  const groupColumns: LayoutColumn[] = []
  if (category) {
    groupColumns.push({ field: category })
    if (picks.categoryLabel) {
      groupColumns.push({ field: picks.categoryLabel })
    }
  }
  if (series) {
    groupColumns.push({ field: series })
    if (picks.seriesLabel) {
      groupColumns.push({ field: picks.seriesLabel })
    }
  }
  const grouped = new Set(groupColumns.map((column) => column.field))
  const measures = picks.measures.filter(
    (measure) => measure.field && measure.aggregate && !grouped.has(measure.field),
  )
  const columns: LayoutColumn[] = [
    ...groupColumns,
    ...measures.map((measure) => ({
      field: measure.field,
      alias: measure.alias,
      aggregate: measure.aggregate,
      format: measure.format,
      locale: measure.locale,
      align: measure.align,
      headerBand: measure.headerBand,
      headerBandArabic: measure.headerBandArabic,
      urlTemplate: measure.urlTemplate,
      sticky: measure.sticky,
      wrap: measure.wrap,
      conditionalFormat: measure.conditionalFormat,
    })),
  ]
  return {
    ...layout,
    kind: 'chart',
    groupBy,
    columns,
    chart,
  }
}

export function suggestedAggregate(column: DatasetColumn) {
  return isNumericSqlType(column.sqlType) ? 'SUM' : 'COUNT'
}

export function resultColumnName(result: ReportResult, field: string, alias?: string | null) {
  const wanted = (alias?.trim() || field).toLowerCase()
  const match = result.columns.find(
    (column) =>
      column.name.toLowerCase() === wanted || column.label.toLowerCase() === wanted,
  )
  return match?.name ?? alias?.trim() ?? field
}

export function cellNumber(value: unknown) {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value
  }
  if (typeof value === 'string' && value.trim()) {
    const parsed = Number(value)
    return Number.isFinite(parsed) ? parsed : 0
  }
  return 0
}

export function cellLabel(value: unknown, emptyLabel: string) {
  if (value == null || value === '') {
    return emptyLabel
  }
  if (value instanceof Date) {
    return value.toISOString().slice(0, 10)
  }
  return String(value)
}
