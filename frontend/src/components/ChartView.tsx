import { useEffect, useMemo, useRef, type ReactNode } from 'react'
import {
  BarChart,
  LineChart,
  PieChart,
} from 'echarts/charts'
import {
  GridComponent,
  LegendComponent,
  TitleComponent,
  TooltipComponent,
} from 'echarts/components'
import { init, use as useEcharts, type ECharts } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsOption, SeriesOption } from 'echarts'
import type { ReportLayout, ReportResult } from '../api/types'
import { useLocale } from '../i18n/LocaleProvider'
import { localeFor } from '../i18n/locale'
import { useTheme } from '../theme/ThemeProvider'
import { Spinner } from './Spinner'
import {
  cellLabel,
  cellNumber,
  chartCategory,
  chartCategoryLabel,
  chartMeasures,
  chartSeriesField,
  chartSeriesLabel,
  isChartLayout,
  resultColumnName,
} from '../lib/chart'

useEcharts([
  BarChart,
  LineChart,
  PieChart,
  GridComponent,
  LegendComponent,
  TitleComponent,
  TooltipComponent,
  CanvasRenderer,
])

type Props = {
  result: ReportResult | null
  layout: ReportLayout
  loading?: boolean
  error?: string | null
  emptyHint?: string
  height?: number
}

type CssTokens = {
  text: string
  muted: string
  line: string
  surface: string
  accent: string
  teal: string
  gold: string
  danger: string
  font: string
}

function readTokens(el: HTMLElement): CssTokens {
  const css = getComputedStyle(el)
  const read = (name: string, fallback: string) =>
    css.getPropertyValue(name).trim() || fallback
  return {
    text: read('--text', '#17262d'),
    muted: read('--muted', '#5c7079'),
    line: read('--line', '#e3e8ec'),
    surface: read('--surface', '#ffffff'),
    accent: read('--accent', '#005470'),
    teal: read('--teal', '#0f8a90'),
    gold: read('--gold', '#c9a15f'),
    danger: read('--danger', '#c1443c'),
    font: read('--sans', 'Segoe UI, sans-serif'),
  }
}

function formatNumber(value: number, locale: string) {
  return Number.isInteger(value)
    ? value.toLocaleString(locale)
    : value.toLocaleString(locale, { maximumFractionDigits: 2 })
}

function cartesianType(type: string): 'bar' | 'line' {
  return type === 'line' || type === 'area' ? 'line' : 'bar'
}

export function ChartView({
  result,
  layout,
  loading,
  error,
  emptyHint,
  height,
}: Props) {
  const { lang, dir, t } = useLocale()
  const { theme } = useTheme()
  const hostRef = useRef<HTMLDivElement>(null)
  const chartRef = useRef<ECharts | null>(null)
  const locale = localeFor(lang)
  const emptyLabel = t('chartEmptyCategory')
  const chart = layout.chart
  const categoryField = chartCategory(layout)
  const categoryLabelField = chartCategoryLabel(layout)
  const seriesField = chartSeriesField(layout)
  const seriesLabelField = chartSeriesLabel(layout)
  const measures = chartMeasures(layout)
  const ready =
    isChartLayout(layout) &&
    Boolean(result) &&
    Boolean(categoryField) &&
    measures.length > 0 &&
    (result?.rows.length ?? 0) > 0

  const option = useMemo<EChartsOption | null>(() => {
    if (!ready || !result || !chart) {
      return null
    }
    const tokens = hostRef.current
      ? readTokens(hostRef.current)
      : readTokens(document.documentElement)
    const palette = [tokens.accent, tokens.teal, tokens.gold, tokens.danger, '#7c6a9a', '#2f6fed']
    const categoryKey = resultColumnName(result, categoryField)
    const seriesKey = seriesField ? resultColumnName(result, seriesField) : ''
    // Rows are grouped by the physical value; when a lookup column rides along, its text is what
    // the axis and legend show. Two codes sharing one label keep separate entries.
    const categoryText = labelReader(result, categoryKey, categoryLabelField, emptyLabel)
    const seriesText = labelReader(result, seriesKey, seriesLabelField, emptyLabel)
    const measureKeys = measures.map((measure) => ({
      ...measure,
      key: resultColumnName(result, measure.field, measure.alias),
      label:
        result.columns.find(
          (column) =>
            column.name.toLowerCase() ===
            resultColumnName(result, measure.field, measure.alias).toLowerCase(),
        )?.label ??
        measure.alias ??
        measure.field,
    }))
    const rtl = dir === 'rtl'
    const pie = chart.type === 'pie' || chart.type === 'doughnut'
    const stacked = Boolean(chart.stacked) && !pie
    const showLegend = chart.showLegend !== false
    const labels = Boolean(chart.showDataLabels)

    const axisLabel = {
      color: tokens.muted,
      fontFamily: tokens.font,
    }
    const tooltip: EChartsOption['tooltip'] = {
      trigger: pie ? 'item' : 'axis',
      backgroundColor: tokens.surface,
      borderColor: tokens.line,
      textStyle: { color: tokens.text, fontFamily: tokens.font },
    }

    if (pie) {
      const measure = measureKeys[0]
      const data = result.rows.map((row) => ({
        name: categoryText(row),
        value: cellNumber(row[measure.key]),
      }))
      return {
        color: palette,
        textStyle: { color: tokens.text, fontFamily: tokens.font },
        tooltip,
        legend: showLegend
          ? {
              type: 'scroll',
              bottom: 0,
              textStyle: { color: tokens.muted, fontFamily: tokens.font },
            }
          : undefined,
        series: [
          {
            type: 'pie',
            radius: chart.type === 'doughnut' ? ['46%', '72%'] : '72%',
            center: ['50%', '46%'],
            data,
            label: {
              show: labels,
              color: tokens.text,
              fontFamily: tokens.font,
              formatter: '{b}: {d}%',
            },
            itemStyle: { borderColor: tokens.surface, borderWidth: 2 },
          },
        ],
      }
    }

    const categories: string[] = []
    const seen = new Set<string>()
    for (const row of result.rows) {
      const label = categoryText(row)
      if (!seen.has(label)) {
        seen.add(label)
        categories.push(label)
      }
    }

    const series: SeriesOption[] = []
    if (seriesKey && measureKeys[0]) {
      const measure = measureKeys[0]
      const names: string[] = []
      const nameSet = new Set<string>()
      for (const row of result.rows) {
        const name = seriesText(row)
        if (!nameSet.has(name)) {
          nameSet.add(name)
          names.push(name)
        }
      }
      for (const name of names) {
        const values = categories.map((category) => {
          const row = result.rows.find(
            (item) => categoryText(item) === category && seriesText(item) === name,
          )
          return row ? cellNumber(row[measure.key]) : 0
        })
        series.push(seriesItem(chart.type, name, values, stacked, labels, tokens.font))
      }
    } else {
      for (const measure of measureKeys) {
        const values = categories.map((category) => {
          const row = result.rows.find((item) => categoryText(item) === category)
          return row ? cellNumber(row[measure.key]) : 0
        })
        series.push(
          seriesItem(chart.type, measure.label, values, stacked, labels, tokens.font),
        )
      }
    }

    return {
      color: palette,
      textStyle: { color: tokens.text, fontFamily: tokens.font },
      tooltip: {
        ...tooltip,
        valueFormatter: (value) => formatNumber(Number(value), locale),
      },
      legend: showLegend
        ? {
            type: 'scroll',
            top: 0,
            textStyle: { color: tokens.muted, fontFamily: tokens.font },
          }
        : undefined,
      grid: {
        containLabel: true,
        top: showLegend ? 36 : 16,
        left: rtl ? 12 : 8,
        right: rtl ? 8 : 12,
        bottom: 8,
      },
      xAxis: {
        type: 'category',
        data: categories,
        inverse: rtl,
        axisLabel,
        axisLine: { lineStyle: { color: tokens.line } },
        axisTick: { show: false },
      },
      yAxis: {
        type: 'value',
        axisLabel: {
          ...axisLabel,
          formatter: (value: number) => formatNumber(value, locale),
        },
        splitLine: { lineStyle: { color: tokens.line } },
      },
      series,
    }
  }, [
    ready,
    result,
    chart,
    categoryField,
    categoryLabelField,
    seriesField,
    seriesLabelField,
    measures,
    dir,
    locale,
    emptyLabel,
    theme,
  ])

  useEffect(() => {
    const host = hostRef.current
    if (!host) {
      return
    }
    const instance = init(host, undefined, { renderer: 'canvas' })
    chartRef.current = instance
    const observer = new ResizeObserver(() => instance.resize())
    observer.observe(host)
    return () => {
      observer.disconnect()
      instance.dispose()
      chartRef.current = null
    }
  }, [])

  useEffect(() => {
    const instance = chartRef.current
    if (!instance) {
      return
    }
    if (!option) {
      instance.clear()
      return
    }
    instance.setOption(option, true)
    instance.resize()
  }, [option, theme, dir, height])

  let overlay: ReactNode = null
  if (error) {
    overlay = <div className="banner error">{error}</div>
  } else if (loading && !result) {
    overlay = (
      <Spinner page label={t('loadingApp')} />
    )
  } else if (!categoryField || measures.length === 0) {
    overlay = (
      <div className="empty chart-empty">
        <p className="muted">{emptyHint ?? t('chartEmptyHint')}</p>
      </div>
    )
  } else if (!result?.rows.length) {
    overlay = (
      <div className="empty chart-empty">
        <p className="muted">{t('chartNoRows')}</p>
      </div>
    )
  }

  return (
    <div className="chart-view">
      {overlay}
      <div
        ref={hostRef}
        className="chart-canvas"
        style={{
          height: height ?? undefined,
          display: overlay ? 'none' : undefined,
        }}
      />
      {loading && !overlay ? (
        <div className="chart-loading">
          <Spinner size="sm" />
          {t('liveQuery')}
        </div>
      ) : null}
    </div>
  )
}

/**
 * Builds the display text for a grouped value. Without a lookup column the value itself is shown.
 * With one, the lookup text is shown; distinct codes that share a label get the code appended so
 * they stay separate on the axis and in the legend.
 */
function labelReader(
  result: ReportResult,
  valueKey: string,
  labelField: string,
  emptyLabel: string,
): (row: Record<string, unknown>) => string {
  if (!valueKey) {
    return () => emptyLabel
  }
  if (!labelField) {
    return (row) => cellLabel(row[valueKey], emptyLabel)
  }
  const labelKey = resultColumnName(result, labelField)
  const codesByLabel = new Map<string, Set<string>>()
  for (const row of result.rows) {
    const text = cellLabel(row[labelKey] ?? row[valueKey], emptyLabel)
    const code = cellLabel(row[valueKey], emptyLabel)
    const codes = codesByLabel.get(text) ?? new Set<string>()
    codes.add(code)
    codesByLabel.set(text, codes)
  }
  return (row) => {
    const text = cellLabel(row[labelKey] ?? row[valueKey], emptyLabel)
    const code = cellLabel(row[valueKey], emptyLabel)
    return (codesByLabel.get(text)?.size ?? 0) > 1 && text !== code ? `${text} (${code})` : text
  }
}

function seriesItem(
  type: string,
  name: string,
  data: number[],
  stacked: boolean,
  labels: boolean,
  font: string,
): SeriesOption {
  const cartesian = cartesianType(type)
  return {
    name,
    type: cartesian,
    data,
    stack: stacked ? 'total' : undefined,
    smooth: cartesian === 'line',
    areaStyle: type === 'area' ? { opacity: 0.18 } : undefined,
    emphasis: { focus: 'series' },
    label: labels
      ? { show: true, position: 'top', fontFamily: font, fontSize: 11 }
      : undefined,
  }
}
