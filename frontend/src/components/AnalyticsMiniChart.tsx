import { useEffect, useMemo, useRef } from 'react'
import {
  BarChart,
  LineChart,
  PieChart,
} from 'echarts/charts'
import {
  GridComponent,
  TooltipComponent,
} from 'echarts/components'
import { init, use as useEcharts, type ECharts } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsOption } from 'echarts'
import type { AnalyticsChartResult } from '../api/types'
import { IconDrill } from './Icons'
import { registerChartCapture } from '../lib/analyticsCapture'
import { ANALYTICS_NULL } from '../lib/analyticsFilters'
import { useLocale } from '../i18n/LocaleProvider'
import { localeFor } from '../i18n/locale'

useEcharts([BarChart, LineChart, PieChart, GridComponent, TooltipComponent, CanvasRenderer])

type Props = {
  chart: AnalyticsChartResult
  nullLabel: string
  onSelect: (value: string) => void
  selectedValues?: string[]
  bare?: boolean
  hostClassName?: string
  onExplore?: () => void
  exploreLabel?: string
}

export function AnalyticsMiniChart({
  chart,
  nullLabel,
  onSelect,
  selectedValues,
  bare,
  hostClassName,
  onExplore,
  exploreLabel,
}: Props) {
  const { lang } = useLocale()
  const hostRef = useRef<HTMLDivElement>(null)
  const chartRef = useRef<ECharts | null>(null)
  const locale = localeFor(lang)

  const option = useMemo<EChartsOption>(() => {
    const css = hostRef.current ? getComputedStyle(hostRef.current) : null
    const text = css?.getPropertyValue('--text').trim() || '#17262d'
    const muted = css?.getPropertyValue('--muted').trim() || '#5c7079'
    const line = css?.getPropertyValue('--line').trim() || '#e3e8ec'
    const accent = css?.getPropertyValue('--accent').trim() || '#005470'
    const teal = css?.getPropertyValue('--teal').trim() || '#0f8a90'
    const gold = css?.getPropertyValue('--gold').trim() || '#c9a15f'
    const picked = css?.getPropertyValue('--ax-selected').trim() || '#0f8a70'
    const selected = new Set((selectedValues ?? []).map((value) => value.toLowerCase()))
    const categories = chart.points.map((point) =>
      point.value === ANALYTICS_NULL ? nullLabel : point.value,
    )
    const palette = [accent, teal, gold, '#7c5cbf', '#c1443c']
    const mark = (value: string) =>
      selected.has(value.toLowerCase()) ? { color: picked } : undefined

    if (chart.type === 'pie') {
      return {
        color: palette,
        tooltip: { trigger: 'item' },
        series: [
          {
            type: 'pie',
            radius: ['46%', '72%'],
            cursor: 'pointer',
            itemStyle: { borderRadius: 6, borderColor: 'transparent', borderWidth: 2 },
            data: chart.points.map((point, index) => ({
              name: categories[index],
              value: point.metric,
              raw: point.value,
              itemStyle: mark(point.value),
            })),
            label: { color: text, fontSize: 11 },
          },
        ],
      }
    }

    return {
      color: [accent],
      grid: { left: 8, right: 12, top: 18, bottom: 8, containLabel: true },
      tooltip: { trigger: 'axis' },
      xAxis: {
        type: 'category',
        data: categories,
        axisLabel: { color: muted, hideOverlap: true, fontSize: 11 },
        axisLine: { lineStyle: { color: line } },
        axisTick: { show: false },
      },
      yAxis: {
        type: 'value',
        axisLabel: {
          color: muted,
          fontSize: 10,
          formatter: (value: number) => value.toLocaleString(locale),
        },
        splitLine: { lineStyle: { color: line, opacity: 0.55 } },
      },
      series: [
        {
          type: chart.type === 'line' ? 'line' : 'bar',
          cursor: 'pointer',
          data: chart.points.map((point) => ({
            value: point.metric,
            itemStyle: mark(point.value),
          })),
          smooth: chart.type === 'line',
          symbolSize: chart.type === 'line' ? 8 : undefined,
          barMaxWidth: bare ? 42 : 28,
          itemStyle: chart.type === 'bar' ? { borderRadius: [7, 7, 2, 2] } : undefined,
          areaStyle: chart.type === 'line' ? { opacity: 0.14 } : undefined,
        },
      ],
    }
  }, [bare, chart, locale, nullLabel, selectedValues])

  useEffect(() => {
    const host = hostRef.current
    if (!host) {
      return
    }
    const instance = chartRef.current ?? init(host)
    chartRef.current = instance
    instance.setOption(option, true)
    instance.off('click')
    instance.on('click', (params) => {
      const data = params.data
      if (data && typeof data === 'object' && 'raw' in data && typeof data.raw === 'string') {
        onSelect(data.raw)
        return
      }
      if (typeof params.dataIndex === 'number') {
        const point = chart.points[params.dataIndex]
        if (point) {
          onSelect(point.value)
        }
      }
    })
    const observer = new ResizeObserver(() => instance.resize())
    observer.observe(host)
    return () => {
      observer.disconnect()
    }
  }, [chart.points, onSelect, option])

  useEffect(
    () =>
      registerChartCapture({
        id: chart.chartId,
        title: chart.title,
        dataUrl: () => chartRef.current?.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#ffffff' }),
      }),
    [chart.chartId, chart.title],
  )

  useEffect(
    () => () => {
      chartRef.current?.dispose()
      chartRef.current = null
    },
    [],
  )

  const host = <div ref={hostRef} className={hostClassName ?? 'analytics-chart-host'} />
  if (bare) {
    return host
  }

  return (
    <article className="analytics-chart-card">
      <header>
        <h3>{chart.title}</h3>
        {chart.state === 'compare' && <span className="analytics-state-pill">B</span>}
        {onExplore && (
          <button
            type="button"
            className="analytics-chart-explore"
            onClick={onExplore}
            title={exploreLabel}
          >
            <IconDrill />
            {exploreLabel}
          </button>
        )}
      </header>
      {host}
    </article>
  )
}
