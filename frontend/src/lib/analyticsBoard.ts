import type { AnalyticsChartPoint, AnalyticsChartResult } from '../api/types'
import { DRILL_CHART_ID } from './analyticsDrill'
import { ANALYTICS_NULL } from './analyticsFilters'

export type BoardSort = 'name' | 'value'

const PERIOD_MIN = 2
const PERIOD_MAX = 8

export function pickPeriodDimension(
  dimensionIds: string[],
  valueCounts: Record<string, number>,
  breakdownId: string | null,
): string | null {
  for (const id of dimensionIds) {
    if (id === breakdownId) {
      continue
    }
    const count = valueCounts[id] ?? 0
    if (count >= PERIOD_MIN && count <= PERIOD_MAX) {
      return id
    }
  }
  return null
}

export function pickScopeDimension(
  dimensionIds: string[],
  breakdownId: string | null,
  periodId: string | null,
): string | null {
  return dimensionIds.find((id) => id !== breakdownId && id !== periodId) ?? null
}

export function seriesPoints(
  charts: AnalyticsChartResult[] | undefined,
  dimensionId: string | null,
  measureId: string | null,
): AnalyticsChartPoint[] | null {
  if (!dimensionId || !measureId) {
    return null
  }
  const list = charts ?? []
  const drill = list.find(
    (chart) =>
      chart.chartId === DRILL_CHART_ID &&
      chart.dimensionId === dimensionId &&
      chart.measureId === measureId,
  )
  if (drill) {
    return drill.points
  }
  const match = list.find(
    (chart) =>
      chart.chartId !== DRILL_CHART_ID &&
      chart.dimensionId === dimensionId &&
      chart.measureId === measureId,
  )
  return match?.points ?? null
}

/** A line on some other dimension, else any other saved chart drawn as a line. */
export function pickTrendChart(
  charts: AnalyticsChartResult[] | undefined,
  breakdownId: string | null,
): AnalyticsChartResult | null {
  const list = (charts ?? []).filter(
    (chart) => chart.chartId !== DRILL_CHART_ID && chart.state !== 'compare' && chart.points.length > 0,
  )
  const line = list.find((chart) => chart.type === 'line' && chart.dimensionId !== breakdownId)
  if (line) {
    return line
  }
  const other = list.find((chart) => chart.dimensionId !== breakdownId)
  if (other) {
    return { ...other, type: 'line' }
  }
  return null
}

export function compactMetric(value: number, locale: string): string {
  if (!Number.isFinite(value)) {
    return '—'
  }
  const abs = Math.abs(value)
  const sign = value < 0 ? '-' : ''
  const units = [
    { size: 1e12, suffix: 'T' },
    { size: 1e9, suffix: 'B' },
    { size: 1e6, suffix: 'M' },
    { size: 1e3, suffix: 'K' },
  ]
  for (const unit of units) {
    if (abs >= unit.size) {
      const scaled = abs / unit.size
      const digits = scaled >= 100 ? 0 : 1
      const text = scaled.toLocaleString(locale, {
        minimumFractionDigits: digits,
        maximumFractionDigits: digits,
      })
      return `${sign}${text}${unit.suffix}`
    }
  }
  return value.toLocaleString(locale, {
    maximumFractionDigits: abs >= 100 ? 0 : 1,
  })
}

export function formatPointMetric(value: number, format: string | null | undefined, locale: string): string {
  if (format === 'P1') {
    return `${value.toLocaleString(locale, { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`
  }
  const compact = compactMetric(value, locale)
  if (format === 'aed') {
    return `AED ${compact}`
  }
  return compact
}

export function formatShare(value: number, locale: string, digits: number): string {
  return `${value.toLocaleString(locale, {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  })}%`
}

export function formatSignedPercent(value: number, locale: string): string {
  const text = Math.abs(value).toLocaleString(locale, {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  })
  if (value === 0) {
    return `${text}%`
  }
  return `${value < 0 ? '▼' : '▲'} ${text}%`
}

function sameValue(left: string, right: string) {
  return left.toLowerCase() === right.toLowerCase()
}

function percentChange(current: number, baseline: number): number | null {
  if (!Number.isFinite(current) || !Number.isFinite(baseline) || baseline === 0) {
    return null
  }
  return ((current - baseline) / Math.abs(baseline)) * 100
}

export type BoardRowModel = {
  value: string
  label: string
  metric: number
  width: number
  metricText: string
  share: number
  delta: number | null
}

export type BoardInsightModel =
  | { kind: 'growth'; highLabel: string; highDelta: number; lowLabel: string; lowDelta: number }
  | { kind: 'share'; highLabel: string; highShare: number; lowLabel: string; lowShare: number }

export type BoardModel = {
  rows: BoardRowModel[]
  sum: number
  top: { label: string; metricText: string; share: number } | null
  /** Share of the one singled-out breakdown value, when the list is filtered to it. */
  singledShare: number | null
  totalDelta: number | null
  insight: BoardInsightModel | null
}

export function deriveBoard(options: {
  points: AnalyticsChartPoint[]
  comparePoints: AnalyticsChartPoint[] | null
  totalNumeric: number | null
  compareNumeric: number | null
  selectedValues: string[]
  sort: BoardSort
  locale: string
  format: string | null | undefined
  nullLabel: string
}): BoardModel {
  const compare = new Map(
    (options.comparePoints ?? []).map((point) => [point.value.toLowerCase(), point.metric]),
  )
  const sum = options.points.reduce((total, point) => total + (Number.isFinite(point.metric) ? point.metric : 0), 0)
  const max = options.points.reduce((peak, point) => Math.max(peak, point.metric), 0)
  const shareOf = (metric: number) => (sum > 0 ? (metric / sum) * 100 : 0)

    const rows = options.points.map((point) => {
    const baseline = compare.get(point.value.toLowerCase())
    return {
      value: point.value,
      label: point.value === ANALYTICS_NULL ? options.nullLabel : point.value,
      metric: point.metric,
      width: max > 0 ? Math.max(0, (point.metric / max) * 100) : 0,
      metricText: formatPointMetric(point.metric, options.format, options.locale),
      share: shareOf(point.metric),
      delta: baseline === undefined ? null : percentChange(point.metric, baseline),
    }
  })

  const singled =
    options.selectedValues.length === 1
      ? rows.find((row) => sameValue(row.value, options.selectedValues[0]))
      : undefined

  rows.sort((left, right) => {
    if (options.sort === 'value') {
      return right.metric - left.metric || left.label.localeCompare(right.label, options.locale, { numeric: true })
    }
    return left.label.localeCompare(right.label, options.locale, { numeric: true, sensitivity: 'base' })
  })

  const ranked = [...rows].sort((left, right) => right.share - left.share || left.label.localeCompare(right.label))
  const top = ranked[0]
    ? { label: ranked[0].label, metricText: ranked[0].metricText, share: ranked[0].share }
    : null

  const withDelta = ranked.filter((row) => row.delta !== null)
  let insight: BoardInsightModel | null = null
  if (withDelta.length >= 2) {
    const ordered = [...withDelta].sort((left, right) => (right.delta ?? 0) - (left.delta ?? 0))
    const high = ordered[0]
    const low = ordered[ordered.length - 1]
    insight = {
      kind: 'growth',
      highLabel: high.label,
      highDelta: high.delta ?? 0,
      lowLabel: low.label,
      lowDelta: low.delta ?? 0,
    }
  } else if (ranked.length >= 2 && top) {
    const low = ranked[ranked.length - 1]
    insight = {
      kind: 'share',
      highLabel: top.label,
      highShare: top.share,
      lowLabel: low.label,
      lowShare: low.share,
    }
  }

  return {
    rows,
    sum,
    top,
    singledShare: singled ? singled.share : null,
    totalDelta:
      options.totalNumeric !== null && options.compareNumeric !== null
        ? percentChange(options.totalNumeric, options.compareNumeric)
        : null,
    insight,
  }
}
