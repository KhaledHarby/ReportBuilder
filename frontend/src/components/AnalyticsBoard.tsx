import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { AnalyticsChartPoint, AnalyticsChartResult } from '../api/types'
import { AnalyticsMiniChart } from './AnalyticsMiniChart'
import { IconChevron } from './Icons'
import { ThemePicker } from './ThemePicker'
import { useLocale } from '../i18n/LocaleProvider'
import { localeFor } from '../i18n/locale'
import {
  deriveBoard,
  formatShare,
  formatSignedPercent,
  type BoardSort,
} from '../lib/analyticsBoard'
import type { AnalyticsDrillStep } from '../lib/analyticsFilters'

type MeasureOption = { id: string; label: string }

type PeriodControl = {
  label: string
  values: { value: string; label: string }[]
  selected: string | null
  allActive: boolean
  locked: boolean
}

type Props = {
  measures: MeasureOption[]
  measureId: string | null
  measureLabel: string
  measureFormat: string | null
  onMeasure: (measureId: string) => void
  period: PeriodControl | null
  onPeriod: (value: string | null) => void
  points: AnalyticsChartPoint[]
  comparePoints: AnalyticsChartPoint[] | null
  totalFormatted: string
  totalNumeric: number | null
  compareNumeric: number | null
  selectedValues: string[]
  totalCaption: string
  shareScope: string
  breakdownName: string
  breakdownTitle: string
  breakdownLocked: boolean
  atLeaf: boolean
  figuresLoading: boolean
  seriesLoading: boolean
  drill: AnalyticsDrillStep[]
  pathLabel: (step: AnalyticsDrillStep) => string
  onDrill: (value: string) => void
  onDrillTo: (depth: number) => void
  trend: {
    title: string
    chart: AnalyticsChartResult
    comparePoints: AnalyticsChartPoint[] | null
    selectedValues: string[]
    onSelect: (value: string) => void
  } | null
}

export function AnalyticsBoard({
  measures,
  measureId,
  measureLabel,
  measureFormat,
  onMeasure,
  period,
  onPeriod,
  points,
  comparePoints,
  totalFormatted,
  totalNumeric,
  compareNumeric,
  selectedValues,
  totalCaption,
  shareScope,
  breakdownName,
  breakdownTitle,
  breakdownLocked,
  atLeaf,
  figuresLoading,
  seriesLoading,
  drill,
  pathLabel,
  onDrill,
  onDrillTo,
  trend,
}: Props) {
  const { lang, t } = useLocale()
  const locale = localeFor(lang)
  const [sort, setSort] = useState<BoardSort>('name')
  const model = deriveBoard({
    points: seriesLoading ? [] : points,
    comparePoints: seriesLoading ? null : comparePoints,
    totalNumeric,
    compareNumeric,
    selectedValues,
    sort,
    locale,
    format: measureFormat,
    nullLabel: t('analyticsNullValue'),
  })
  const showDelta = model.rows.some((row) => row.delta !== null)
  const secondIsDelta = model.totalDelta !== null
  const shareValue =
    model.rows.length === 0
      ? '—'
      : formatShare(model.singledShare ?? 100, locale, 1)
  const shareCaption = t('analyticsBoardOfAll', { label: shareScope })

  return (
    <div className="ax-sheet">
      <div className="ax-controls">
        {measures.length > 0 && (
          <div className="ax-control">
            <span>{t('analyticsBoardMetric')}</span>
            <div className="ax-pills" role="group" aria-label={t('analyticsBoardMetric')}>
              {measures.map((measure) => (
                <button
                  key={measure.id}
                  type="button"
                  className={`ax-pill${measure.id === measureId ? ' is-on' : ''}`}
                  aria-pressed={measure.id === measureId}
                  onClick={() => onMeasure(measure.id)}
                >
                  {measure.label}
                </button>
              ))}
            </div>
          </div>
        )}
        {period && period.values.length > 0 && (
          <div className="ax-control">
            <span>{period.label}</span>
            <div className="ax-pills" role="group" aria-label={period.label}>
              <button
                type="button"
                className={`ax-pill${period.allActive ? ' is-on' : ''}`}
                aria-pressed={period.allActive}
                disabled={period.locked}
                onClick={() => onPeriod(null)}
              >
                {t('analyticsBoardAll')}
              </button>
              {period.values.map((item) => (
                <button
                  key={item.value}
                  type="button"
                  className={`ax-pill${period.selected !== null && period.selected.toLowerCase() === item.value.toLowerCase() ? ' is-on' : ''}`}
                  aria-pressed={period.selected !== null && period.selected.toLowerCase() === item.value.toLowerCase()}
                  disabled={period.locked}
                  onClick={() => onPeriod(item.value)}
                >
                  {item.label}
                </button>
              ))}
            </div>
          </div>
        )}
        <label className="ax-control ax-sort">
          <span>{t('analyticsBoardSort')}</span>
          <select
            value={sort}
            aria-label={t('analyticsBoardSort')}
            onChange={(event) => setSort(event.target.value === 'value' ? 'value' : 'name')}
          >
            <option value="name">{t('analyticsBoardSortName')}</option>
            <option value="value">{t('analyticsBoardSortValue')}</option>
          </select>
        </label>
      </div>

      <div className={`ax-figures${figuresLoading ? ' is-loading' : ''}`} aria-busy={figuresLoading}>
        {figuresLoading ? (
          Array.from({ length: 4 }, (_, index) => (
            <div key={index} className="ax-figure" aria-hidden="true">
              <span className="analytics-skeleton analytics-skeleton-sm" />
              <span className="analytics-skeleton analytics-skeleton-lg" />
            </div>
          ))
        ) : (
          <>
            <figure className="ax-figure">
              <figcaption>{measureLabel}</figcaption>
              <strong>{totalFormatted}</strong>
              <em>{totalCaption}</em>
            </figure>
            <figure className="ax-figure">
              <figcaption>
                {secondIsDelta ? t('analyticsBoardVersus') : t('analyticsBoardLargest')}
              </figcaption>
              <strong className={secondIsDelta ? toneClass(model.totalDelta) : undefined}>
                {secondIsDelta ? formatSignedPercent(model.totalDelta ?? 0, locale) : model.top ? formatShare(model.top.share, locale, 1) : '—'}
              </strong>
              <em>{secondIsDelta ? t('analyticsBoardVersusHint') : t('analyticsBoardLargestHint')}</em>
            </figure>
            <figure className="ax-figure">
              <figcaption>{t('analyticsBoardShare')}</figcaption>
              <strong>{shareValue}</strong>
              <em>{shareCaption}</em>
            </figure>
            <figure className="ax-figure">
              <figcaption>{t('analyticsBoardTop', { label: breakdownName })}</figcaption>
              <strong className="is-text">{model.top?.label ?? '—'}</strong>
              <em>
                {model.top
                  ? `${model.top.metricText} · ${formatShare(model.top.share, locale, 0)}`
                  : '—'}
              </em>
            </figure>
          </>
        )}
      </div>

      {!seriesLoading && model.insight && (
        <p className="ax-insight">
          {model.insight.kind === 'growth' ? (
            <>
              {t('analyticsBoardFastest')}: <b>{model.insight.highLabel}</b>{' '}
              <span className={toneClass(model.insight.highDelta)}>{formatSignedPercent(model.insight.highDelta, locale)}</span>
              {' · '}
              {t('analyticsBoardSlowest')}: <b>{model.insight.lowLabel}</b>{' '}
              <span className={toneClass(model.insight.lowDelta)}>{formatSignedPercent(model.insight.lowDelta, locale)}</span>
            </>
          ) : (
            <>
              {t('analyticsBoardLargest')}: <b>{model.insight.highLabel}</b>{' '}
              {formatShare(model.insight.highShare, locale, 1)}
              {' · '}
              {t('analyticsBoardSmallest')}: <b>{model.insight.lowLabel}</b>{' '}
              {formatShare(model.insight.lowShare, locale, 1)}
            </>
          )}
        </p>
      )}

      <div className="ax-split">
        <section className="ax-panel" aria-label={breakdownTitle}>
          <header className="ax-panel-head">
            <div>
              <h2>{breakdownTitle}</h2>
              <p>
                {totalCaption}
                {atLeaf ? ` · ${t('analyticsDrillLeaf')}` : ''}
                {breakdownLocked ? ` · ${t('analyticsBreadcrumbLocked')}` : ''}
              </p>
            </div>
            {drill.length > 0 && (
              <button type="button" className="ax-text-btn" onClick={() => onDrillTo(drill.length - 1)}>
                {t('analyticsDrillUp')}
              </button>
            )}
          </header>
          {drill.length > 0 && (
            <ol className="ax-path">
              <li>
                <button type="button" onClick={() => onDrillTo(0)}>
                  {t('analyticsBreadcrumbAll')}
                </button>
              </li>
              {drill.map((step, index) => (
                <li key={`${step.dimensionId}:${step.value}:${index}`}>
                  <button type="button" onClick={() => onDrillTo(index + 1)}>
                    {pathLabel(step)}
                  </button>
                </li>
              ))}
            </ol>
          )}
          {seriesLoading ? (
            <div className="ax-rows" aria-hidden="true">
              {Array.from({ length: 4 }, (_, index) => (
                <span key={index} className="analytics-skeleton analytics-skeleton-row" />
              ))}
            </div>
          ) : model.rows.length === 0 ? (
            <div className="analytics-empty-block">{t('analyticsDrillEmpty')}</div>
          ) : (
            <div className="ax-rows" role="list">
              {model.rows.map((row) => {
                const selected = selectedValues.some((value) => value.toLowerCase() === row.value.toLowerCase())
                return (
                  <button
                    key={row.value}
                    type="button"
                    role="listitem"
                    className={`ax-row${selected ? ' is-on' : ''}${showDelta ? '' : ' no-delta'}`}
                    disabled={breakdownLocked}
                    onClick={() => onDrill(row.value)}
                  >
                    <span className="ax-row-name" title={row.label}>
                      {row.label}
                    </span>
                    <span className="ax-row-track" aria-hidden="true">
                      <span style={{ width: `${row.width}%` }} />
                    </span>
                    <span className="ax-row-metric">{row.metricText}</span>
                    <span className="ax-row-share">{formatShare(row.share, locale, 0)}</span>
                    {showDelta && (
                      <span className={`ax-row-delta ${row.delta === null ? '' : toneClass(row.delta)}`}>
                        {row.delta === null ? '' : formatSignedPercent(row.delta, locale)}
                      </span>
                    )}
                    <IconChevron className="ax-row-chevron" />
                  </button>
                )
              })}
            </div>
          )}
          <p className="ax-panel-foot">{t('analyticsBoardDrillHint')}</p>
        </section>

        <section className="ax-panel" aria-label={trend?.title ?? t('analyticsBoardTrend')}>
          <header className="ax-panel-head">
            <div>
              <h2>{trend?.title ?? t('analyticsBoardTrendOf', { measure: measureLabel })}</h2>
              <p>{totalCaption}</p>
            </div>
          </header>
          <p className="ax-hover-hint">{t('analyticsBoardHover')}</p>
          {seriesLoading ? (
            <div className="ax-trend is-loading" aria-hidden="true">
              <span className="analytics-skeleton analytics-skeleton-chart" />
            </div>
          ) : trend && trend.chart.points.length > 0 ? (
            <AnalyticsMiniChart
              chart={trend.chart}
              nullLabel={t('analyticsNullValue')}
              onSelect={trend.onSelect}
              selectedValues={trend.selectedValues}
              bare
              hostClassName="ax-trend"
              tone="selected"
              legend
              seriesName={measureLabel}
              compareSeries={
                trend.comparePoints && trend.comparePoints.length > 0
                  ? { name: t('analyticsBoardCompare'), points: trend.comparePoints }
                  : null
              }
              axisFormat={measureFormat}
            />
          ) : (
            <div className="analytics-empty-block">{t('analyticsChartEmpty')}</div>
          )}
        </section>
      </div>
    </div>
  )
}

function toneClass(value: number | null) {
  if (value === null || value === 0) {
    return 'is-flat'
  }
  return value > 0 ? 'is-up' : 'is-down'
}

export function AnalyticsThemeButton() {
  const { t } = useLocale()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) {
      return
    }
    const onPointer = (event: PointerEvent) => {
      if (!ref.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        event.stopPropagation()
        setOpen(false)
      }
    }
    document.addEventListener('pointerdown', onPointer)
    document.addEventListener('keydown', onKey, true)
    return () => {
      document.removeEventListener('pointerdown', onPointer)
      document.removeEventListener('keydown', onKey, true)
    }
  }, [open])

  return (
    <div className={`ax-pop${open ? ' is-open' : ''}`} ref={ref}>
      <button
        type="button"
        className="ax-outline-btn"
        aria-expanded={open}
        aria-haspopup="dialog"
        onClick={() => setOpen((value) => !value)}
      >
        {t('analyticsBoardTheme')}
      </button>
      {open && (
        <div className="ax-pop-panel ax-theme-panel" role="dialog" aria-label={t('analyticsBoardTheme')}>
          <ThemePicker compact />
        </div>
      )}
    </div>
  )
}

export function AnalyticsScopeButton({
  label,
  open,
  onToggle,
  onClose,
  children,
}: {
  label: string
  open: boolean
  onToggle: () => void
  onClose: () => void
  children: ReactNode
}) {
  const ref = useRef<HTMLDivElement>(null)
  const { t } = useLocale()

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
        event.preventDefault()
        event.stopPropagation()
        onClose()
      }
    }
    document.addEventListener('pointerdown', onPointer)
    document.addEventListener('keydown', onKey, true)
    return () => {
      document.removeEventListener('pointerdown', onPointer)
      document.removeEventListener('keydown', onKey, true)
    }
  }, [open, onClose])

  return (
    <div className={`ax-pop${open ? ' is-open' : ''}`} ref={ref}>
      <button
        type="button"
        className="ax-scope-pill"
        aria-expanded={open}
        aria-haspopup="dialog"
        onClick={onToggle}
      >
        {label}
      </button>
      {open && (
        <div className="ax-pop-panel ax-scope-panel" role="dialog" aria-label={t('analyticsBoardFilters')}>
          {children}
        </div>
      )}
    </div>
  )
}
