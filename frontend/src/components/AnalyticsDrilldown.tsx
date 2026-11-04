import type { AnalyticsChartPoint, AnalyticsChartType } from '../api/types'
import { AnalyticsMiniChart } from './AnalyticsMiniChart'
import { useLocale } from '../i18n/LocaleProvider'
import type { AnalyticsDrillStep } from '../lib/analyticsFilters'

type DimensionChip = {
  id: string
  label: string
}

type MeasureOption = {
  id: string
  label: string
}

type Props = {
  dimensions: DimensionChip[]
  measures: MeasureOption[]
  drill: AnalyticsDrillStep[]
  breakdownId: string | null
  measureId: string | null
  chartType: AnalyticsChartType
  points: AnalyticsChartPoint[]
  usesCount: boolean
  loading: boolean
  locked: boolean
  atLeaf: boolean
  selectedValues: string[]
  onBreakdown: (dimensionId: string) => void
  onMeasure: (measureId: string) => void
  onChartType: (type: AnalyticsChartType) => void
  onDrill: (value: string) => void
  onDrillTo: (depth: number) => void
}

const CHART_TYPES: AnalyticsChartType[] = ['bar', 'line', 'pie']

export function AnalyticsDrilldown({
  dimensions,
  measures,
  drill,
  breakdownId,
  measureId,
  chartType,
  points,
  usesCount,
  loading,
  locked,
  atLeaf,
  selectedValues,
  onBreakdown,
  onMeasure,
  onChartType,
  onDrill,
  onDrillTo,
}: Props) {
  const { t } = useLocale()
  const breakdown = dimensions.find((item) => item.id === breakdownId) ?? null
  const measure = measures.find((item) => item.id === measureId) ?? null
  const metricLabel = usesCount ? t('analyticsDrillCount') : measure?.label ?? t('analyticsDrillMeasure')

  return (
    <section className="ax-chart" id="analytics-drill" aria-label={t('analyticsDrillPath')}>
      <header className="ax-chart-head">
        <div>
          <h2>{breakdown?.label ?? t('analyticsDrillBy')}</h2>
          <p>
            {metricLabel}
            {!loading && points.length > 0 ? ` · ${points.length.toLocaleString()}` : ''}
            {atLeaf ? ` · ${t('analyticsDrillLeaf')}` : locked ? ` · ${t('analyticsBreadcrumbLocked')}` : ''}
          </p>
        </div>
        <div className="ax-chart-tools">
          {drill.length > 0 && (
            <button type="button" className="analytics-btn ghost" onClick={() => onDrillTo(drill.length - 1)}>
              {t('analyticsDrillUp')}
            </button>
          )}
          <div className="analytics-seg" role="group" aria-label={t('analyticsChartType')}>
            {CHART_TYPES.map((type) => (
              <button
                key={type}
                type="button"
                className={chartType === type ? 'is-active' : ''}
                aria-pressed={chartType === type}
                onClick={() => onChartType(type)}
              >
                {type === 'bar'
                  ? t('analyticsChartBar')
                  : type === 'line'
                    ? t('analyticsChartLine')
                    : t('analyticsChartPie')}
              </button>
            ))}
          </div>
          {measures.length > 0 && (
            <select
              aria-label={t('analyticsDrillMeasure')}
              value={measureId ?? ''}
              onChange={(event) => onMeasure(event.target.value)}
            >
              {measures.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.label}
                </option>
              ))}
            </select>
          )}
        </div>
      </header>

      {dimensions.length > 1 && (
        <div className="ax-chart-tabs" role="tablist" aria-label={t('analyticsDrillBy')}>
          {dimensions.map((dimension) => {
            const active = dimension.id === breakdownId
            const onPath = drill.some((step) => step.dimensionId === dimension.id)
            return (
              <button
                key={dimension.id}
                type="button"
                role="tab"
                aria-selected={active}
                className={`ax-chart-tab${active ? ' is-active' : ''}${onPath ? ' is-on-path' : ''}`}
                onClick={() => onBreakdown(dimension.id)}
              >
                {dimension.label}
              </button>
            )
          })}
        </div>
      )}

      {loading ? (
        <div className="ax-chart-plot is-loading" aria-hidden="true">
          <span className="analytics-skeleton analytics-skeleton-chart" />
        </div>
      ) : points.length === 0 || !breakdown ? (
        <div className="analytics-empty-block">{t('analyticsDrillEmpty')}</div>
      ) : (
        <AnalyticsMiniChart
          bare
          hostClassName="ax-chart-plot"
          chart={{
            chartId: 'drill-focus',
            title: measure ? `${breakdown.label} · ${measure.label}` : breakdown.label,
            type: chartType,
            dimensionId: breakdown.id,
            measureId: measure?.id ?? '',
            state: 'default',
            points,
          }}
          nullLabel={t('analyticsNullValue')}
          selectedValues={selectedValues}
          onSelect={locked ? () => undefined : onDrill}
        />
      )}
    </section>
  )
}
