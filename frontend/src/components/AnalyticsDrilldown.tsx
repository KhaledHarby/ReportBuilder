import type { AnalyticsChartPoint, AnalyticsChartType } from '../api/types'
import { AnalyticsMiniChart } from './AnalyticsMiniChart'
import { useLocale } from '../i18n/LocaleProvider'
import { ANALYTICS_NULL, type AnalyticsDrillStep } from '../lib/analyticsFilters'

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
  const labelFor = (dimensionId: string) =>
    dimensions.find((item) => item.id === dimensionId)?.label ?? dimensionId
  const display = (value: string) => (value === ANALYTICS_NULL ? t('analyticsNullValue') : value)
  const metricLabel = usesCount ? t('analyticsDrillCount') : measure?.label ?? t('analyticsDrillMeasure')

  return (
    <section className="analytics-drill" id="analytics-drill" aria-label={t('analyticsDrillPath')}>
      <header className="analytics-drill-head">
        <div className="analytics-drill-copy">
          <p className="analytics-drill-kicker">{t('analyticsDrillTitle')}</p>
          <h2>{breakdown?.label ?? t('analyticsDrillBy')}</h2>
          <p className="analytics-drill-hint">
            {locked ? t('analyticsBreadcrumbLocked') : atLeaf ? t('analyticsDrillLeaf') : t('analyticsDrillHint')}
          </p>
        </div>
        <div className="analytics-drill-controls">
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
            <label className="analytics-drill-measure">
              <span>{t('analyticsDrillMeasure')}</span>
              <select
                value={measureId ?? ''}
                onChange={(event) => onMeasure(event.target.value)}
              >
                {measures.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.label}
                  </option>
                ))}
              </select>
            </label>
          )}
        </div>
      </header>

      <div className="analytics-drill-path" role="navigation" aria-label={t('analyticsDrillPath')}>
        <button
          type="button"
          className={`analytics-drill-step${drill.length === 0 ? ' is-current' : ''}`}
          onClick={() => onDrillTo(0)}
        >
          <strong>{t('analyticsBreadcrumbAll')}</strong>
        </button>
        {drill.map((step, index) => (
          <span key={`${step.dimensionId}:${step.value}`} className="analytics-drill-hop">
            <i className="analytics-drill-sep" aria-hidden="true" />
            <button
              type="button"
              className={`analytics-drill-step${index === drill.length - 1 ? ' is-current' : ''}`}
              onClick={() => onDrillTo(index + 1)}
            >
              <small>{labelFor(step.dimensionId)}</small>
              <strong>{display(step.value)}</strong>
            </button>
          </span>
        ))}
        {drill.length > 0 && (
          <button type="button" className="analytics-btn ghost analytics-drill-up" onClick={() => onDrillTo(drill.length - 1)}>
            {t('analyticsDrillUp')}
          </button>
        )}
      </div>

      {dimensions.length > 0 && (
        <div className="analytics-drill-dims" role="tablist" aria-label={t('analyticsDrillBy')}>
          {dimensions.map((dimension) => {
            const onPath = drill.some((step) => step.dimensionId === dimension.id)
            const active = dimension.id === breakdownId
            return (
              <button
                key={dimension.id}
                type="button"
                role="tab"
                aria-selected={active}
                className={`analytics-drill-dim${active ? ' is-active' : ''}${onPath ? ' is-on-path' : ''}`}
                onClick={() => onBreakdown(dimension.id)}
              >
                {dimension.label}
              </button>
            )
          })}
        </div>
      )}

      <div className="analytics-drill-stage">
        <div className="analytics-drill-metric">
          <span>{metricLabel}</span>
          {!loading && <em>{points.length.toLocaleString()}</em>}
        </div>
        {loading ? (
          <div className="analytics-drill-chart is-loading" aria-hidden="true">
            <span className="analytics-skeleton analytics-skeleton-chart" />
          </div>
        ) : points.length === 0 || !breakdown ? (
          <div className="analytics-empty-block">{t('analyticsDrillEmpty')}</div>
        ) : (
          <AnalyticsMiniChart
            bare
            hostClassName="analytics-drill-chart"
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
      </div>
    </section>
  )
}
