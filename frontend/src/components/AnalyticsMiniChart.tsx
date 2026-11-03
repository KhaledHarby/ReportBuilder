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
import { registerChartCapture } from '../lib/analyticsCapture'
import { ANALYTICS_NULL } from '../lib/analyticsFilters'
import { useLocale } from '../i18n/LocaleProvider'
import { localeFor } from '../i18n/locale'

useEcharts([BarChart, LineChart, PieChart, GridComponent, TooltipComponent, CanvasRenderer])

type Props = {
  chart: AnalyticsChartResult
  nullLabel: string
  onSelect: (value: string) => void
}

export function AnalyticsMiniChart({ chart, nullLabel, onSelect }: Props) {
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
    const categories = chart.points.map((point) =>
      point.value === ANALYTICS_NULL ? nullLabel : point.value,
    )
    const values = chart.points.map((point) => point.metric)
    const palette = [accent, teal, gold, '#7c5cbf', '#c1443c']

    if (chart.type === 'pie') {
      return {
        color: palette,
        tooltip: { trigger: 'item' },
        series: [
          {
            type: 'pie',
            radius: ['42%', '68%'],
            data: chart.points.map((point, index) => ({
              name: categories[index],
              value: point.metric,
              raw: point.value,
            })),
            label: { color: text, fontSize: 11 },
          },
        ],
      }
    }

    return {
      color: [accent],
      grid: { left: 8, right: 8, top: 12, bottom: 8, containLabel: true },
      tooltip: { trigger: 'axis' },
      xAxis: {
        type: 'category',
        data: categories,
        axisLabel: { color: muted, hideOverlap: true, fontSize: 10 },
        axisLine: { lineStyle: { color: line } },
      },
      yAxis: {
        type: 'value',
        axisLabel: {
          color: muted,
          fontSize: 10,
          formatter: (value: number) => value.toLocaleString(locale),
        },
        splitLine: { lineStyle: { color: line, opacity: 0.6 } },
      },
      series: [
        {
          type: chart.type === 'line' ? 'line' : 'bar',
          data: values,
          smooth: chart.type === 'line',
          barMaxWidth: 28,
          areaStyle: chart.type === 'line' ? { opacity: 0.12 } : undefined,
        },
      ],
    }
  }, [chart, locale, nullLabel])

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

  return (
    <article className="analytics-chart-card">
      <header>
        <h3>{chart.title}</h3>
        {chart.state === 'compare' && <span className="analytics-state-pill">B</span>}
      </header>
      <div ref={hostRef} className="analytics-chart-host" />
    </article>
  )
}
