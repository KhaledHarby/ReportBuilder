import type { Dataset, DatasetColumn, LayoutColumn, ReportLayout } from '../api/types'
import { isLookupField } from '../lib/lookupColumns'
import { Field } from './AdminFields'
import { FilterLayoutSettings } from './FilterLayoutSettings'
import { FilterLabelButton } from './FilterLabelButton'
import { FilterOptionsButton } from './FilterOptionsButton'
import { FilterValueField } from './FilterValueField'
import {
  clearDescendantFilterValues,
  FILTER_OPERATORS,
  needsFilterValue,
  operatorMessageKey,
} from '../lib/filters'
import {
  CHART_TYPES,
  CHART_TYPE_KEYS,
  chartCategory,
  chartCategoryLabel,
  chartMeasures,
  chartSeriesField,
  chartSeriesLabel,
  isNumericSqlType,
  suggestedAggregate,
  withChartConfig,
  withChartPicks,
} from '../lib/chart'
import { lookupControlField } from '../lib/lookupColumns'
import { columnLabel } from '../i18n/localized'
import { DatasetPicker } from './DatasetPicker'
import { IconTrash } from './Icons'
import { useLocale } from '../i18n/LocaleProvider'
import type { Lang } from '../i18n/locale'

type Props = {
  datasetId: string
  datasets: Dataset[]
  palette: DatasetColumn[]
  layout: ReportLayout
  categoryError?: string
  measureError?: string
  onDatasetChange: (datasetId: string) => void
  onChange: (layout: ReportLayout) => void
}

export function ChartDesignerCanvas({
  datasetId,
  datasets,
  palette,
  layout,
  categoryError,
  measureError,
  onDatasetChange,
  onChange,
}: Props) {
  const { lang, t } = useLocale()
  const category = chartCategory(layout)
  const categoryLabel = chartCategoryLabel(layout)
  const series = chartSeriesField(layout)
  const seriesLabel = chartSeriesLabel(layout)
  const measures = chartMeasures(layout)
  const chart = layout.chart
  const pie = chart?.type === 'pie' || chart?.type === 'doughnut'
  const sourcePalette = palette.filter((column) => !isLookupField(column.columnName))
  const groupable = sourcePalette.filter((column) => column.isGroupable)
  const categoryOptions = groupable.length ? groupable : sourcePalette
  const filterable = palette.filter((column) => column.isFilterable)
  const filterOptions = filterable.length ? filterable : sourcePalette
  const measureFields = new Set(measures.map((item) => item.field))

  // Columns mapped to a lookup get a second entry that groups by the code but shows the lookup text.
  const lookupFor = (column: DatasetColumn) => {
    const field = lookupControlField(column.id)
    return palette.some((item) => item.columnName === field) ? field : ''
  }
  const groupOptions = (exclude: string) =>
    categoryOptions
      .filter((column) => column.columnName !== exclude)
      .flatMap((column) => {
        const label = columnLabel(lang, column)
        const lookup = lookupFor(column)
        return [
          { value: column.columnName, label },
          ...(lookup ? [{ value: lookup, label: `${label} · ${t('lookupColumn')}` }] : []),
        ]
      })
  // A picked value is either a physical column or its lookup entry; the layout always groups by
  // the physical column and carries the lookup column alongside it.
  const resolveGroupPick = (value: string) => {
    if (!isLookupField(value)) {
      return { field: value, label: '' }
    }
    const columnId = value.split(':')[1]
    const fact = palette.find((column) => column.id === columnId)
    return fact ? { field: fact.columnName, label: value } : { field: '', label: '' }
  }

  const updatePicks = (next: {
    category?: string
    categoryLabel?: string
    series?: string
    seriesLabel?: string
    measures?: LayoutColumn[]
  }) => {
    onChange(
      withChartPicks(layout, {
        category: next.category ?? category,
        categoryLabel: next.categoryLabel ?? categoryLabel,
        series: next.series ?? series,
        seriesLabel: next.seriesLabel ?? seriesLabel,
        measures: next.measures ?? measures,
        chart: chart ?? undefined,
      }),
    )
  }

  return (
    <aside className="panel chart-designer">
      <DatasetPicker
        value={datasetId}
        datasets={datasets}
        onChange={onDatasetChange}
      />

      <div className="chart-type-block">
        <span className="muted small">{t('chartType')}</span>
        <div className="chart-types" role="group" aria-label={t('chartType')}>
          {CHART_TYPES.map((type) => (
            <button
              key={type}
              type="button"
              className={chart?.type === type ? 'active' : ''}
              aria-pressed={chart?.type === type}
              onClick={() => onChange(withChartConfig(layout, { type }))}
            >
              {t(CHART_TYPE_KEYS[type])}
            </button>
          ))}
        </div>
      </div>

      <div className={`chart-fields${pie ? ' single' : ''}`}>
        <Field label={t('chartCategory')} error={categoryError}>
          <select
            value={categoryLabel || category}
            aria-invalid={Boolean(categoryError)}
            onChange={(event) => {
              const pick = resolveGroupPick(event.target.value)
              const clearSeries = pick.field === series
              updatePicks({
                category: pick.field,
                categoryLabel: pick.label,
                series: clearSeries ? '' : series,
                seriesLabel: clearSeries ? '' : seriesLabel,
              })
            }}
          >
            <option value="">{t('chartPickField')}</option>
            {groupOptions('').map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </Field>

        {!pie && (
          <Field label={t('chartSeries')}>
            <select
              value={seriesLabel || series}
              onChange={(event) => {
                const pick = resolveGroupPick(event.target.value)
                updatePicks({ series: pick.field, seriesLabel: pick.label })
              }}
            >
              <option value="">{t('chartSeriesNone')}</option>
              {groupOptions(category).map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </Field>
        )}
      </div>

      <div className={`chart-measures${measureError ? ' has-error' : ''}`}>
        <div className="chart-measures-head">
          <strong>{t('chartMeasures')}</strong>
          <p className="muted small">{t('chartMeasuresHint')}</p>
        </div>
        {measureError ? (
          <p className="field-error" role="alert">
            {measureError}
          </p>
        ) : null}
        <div className="filter-list">
          {measures.map((measure, index) => {
            const meta = palette.find((column) => column.columnName === measure.field)
            return (
              <div key={`${measure.field}-${index}`} className="chart-measure-row">
                <strong>{meta ? columnLabel(lang, meta) : measure.field}</strong>
                <select
                  value={measure.aggregate ?? 'COUNT'}
                  onChange={(event) => {
                    const next = measures.slice()
                    next[index] = { ...measure, aggregate: event.target.value }
                    updatePicks({ measures: next })
                  }}
                >
                  <option value="COUNT">COUNT</option>
                  {(!meta || isNumericSqlType(meta.sqlType)) && (
                    <>
                      <option value="SUM">SUM</option>
                      <option value="AVG">AVG</option>
                    </>
                  )}
                  <option value="MIN">MIN</option>
                  <option value="MAX">MAX</option>
                </select>
                <select
                  value={measure.locale === 'en' || measure.locale === 'ar' ? measure.locale : 'default'}
                  title={t('columnLocale')}
                  aria-label={t('columnLocale')}
                  onChange={(event) => {
                    const locale = event.target.value
                    const next = measures.slice()
                    next[index] = {
                      ...measure,
                      locale: locale === 'default' ? undefined : locale,
                    }
                    updatePicks({ measures: next })
                  }}
                >
                  <option value="default">{t('columnLocaleDefault')}</option>
                  <option value="en">{t('columnLocaleEnglish')}</option>
                  <option value="ar">{t('columnLocaleArabic')}</option>
                </select>
                <button
                  type="button"
                  className="link danger"
                  onClick={() =>
                    updatePicks({
                      measures: measures.filter((_, itemIndex) => itemIndex !== index),
                    })
                  }
                >
                  {t('remove')}
                </button>
              </div>
            )
          })}
        </div>
        {(!pie || measures.length === 0) && (
          <select
            value=""
            onChange={(event) => {
              const field = event.target.value
              const meta = palette.find((column) => column.columnName === field)
              if (!field || !meta || measureFields.has(field) || field === category || field === series) {
                return
              }
              updatePicks({
                measures: [...measures, { field, aggregate: suggestedAggregate(meta) }],
              })
            }}
          >
            <option value="">{t('chartAddMeasure')}</option>
            {sourcePalette
              .filter(
                (column) =>
                  !measureFields.has(column.columnName) &&
                  column.columnName !== category &&
                  column.columnName !== series,
              )
              .map((column) => (
                <option key={column.id} value={column.columnName}>
                  {columnLabel(lang, column)}
                </option>
              ))}
          </select>
        )}
      </div>

      <div className="chart-options">
        {!pie && (
          <label className="filter-lock">
            <input
              type="checkbox"
              checked={Boolean(chart?.stacked)}
              onChange={(event) =>
                onChange(withChartConfig(layout, { stacked: event.target.checked }))
              }
            />
            {t('chartStacked')}
          </label>
        )}
        <label className="filter-lock">
          <input
            type="checkbox"
            checked={chart?.showLegend !== false}
            onChange={(event) =>
              onChange(withChartConfig(layout, { showLegend: event.target.checked }))
            }
          />
          {t('chartLegend')}
        </label>
        <label className="filter-lock">
          <input
            type="checkbox"
            checked={Boolean(chart?.showDataLabels)}
            onChange={(event) =>
              onChange(withChartConfig(layout, { showDataLabels: event.target.checked }))
            }
          />
          {t('chartLabels')}
        </label>
      </div>

      <div className="chart-filters">
        <div className="chart-measures-head">
          <div className="zone-title">
            <strong>{t('filters')}</strong>
            <FilterLayoutSettings
              value={layout.filtersPerRow}
              onChange={(filtersPerRow) => onChange({ ...layout, filtersPerRow })}
            />
          </div>
          <p className="muted small">{t('filtersHint')}</p>
        </div>
        <div className="filter-list">
          {layout.filters.map((filter, index) => (
            <ChartFilterRow
              key={`${filter.field}-${index}`}
              lang={lang}
              datasetId={datasetId}
              palette={palette}
              filter={filter}
              allFilters={layout.filters}
              onChange={(nextFilter) => {
                const previous = layout.filters[index]
                const next = layout.filters.slice()
                next[index] = nextFilter
                onChange({
                  ...layout,
                  filters: clearDescendantFilterValues(next, index, previous),
                })
              }}
              onRemove={() => {
                const removed = layout.filters[index]?.field.toLowerCase()
                onChange({
                  ...layout,
                  filters: layout.filters
                    .filter((_, itemIndex) => itemIndex !== index)
                    .map((item) =>
                      item.dependsOn?.toLowerCase() === removed
                        ? { ...item, dependsOn: null }
                        : item,
                    ),
                })
              }}
            />
          ))}
        </div>
        <select
          value=""
          onChange={(event) => {
            const field = event.target.value
            if (!field) {
              return
            }
            onChange({
              ...layout,
              filters: [...layout.filters, { field, operator: 'eq', value: '' }],
            })
          }}
        >
          <option value="">{t('chartAddFilter')}</option>
          {filterOptions.map((column) => (
            <option key={column.id} value={column.columnName}>
              {columnLabel(lang, column)}
            </option>
          ))}
        </select>
      </div>
    </aside>
  )
}

function ChartFilterRow({
  lang,
  datasetId,
  palette,
  filter,
  allFilters,
  onChange,
  onRemove,
}: {
  lang: Lang
  datasetId: string
  palette: DatasetColumn[]
  filter: ReportLayout['filters'][number]
  allFilters: ReportLayout['filters']
  onChange: (filter: ReportLayout['filters'][number]) => void
  onRemove: () => void
}) {
  const { t } = useLocale()
  const meta = palette.find((item) => item.columnName === filter.field)
  const fieldName = meta ? columnLabel(lang, meta) : filter.field
  return (
    <div className={`chart-filter-row${filter.isFixed ? ' is-fixed' : ''}${filter.isHidden ? ' is-hidden' : ''}${filter.isRequired ? ' is-required' : ''}`}>
      <strong title={filter.field}>{fieldName}</strong>
      <FilterLabelButton
        fieldLabel={fieldName}
        alias={filter.alias}
        aliasArabic={filter.aliasArabic}
        onChange={(next) => onChange({ ...filter, ...next })}
      />
      <select
        value={filter.operator}
        onChange={(event) => onChange({ ...filter, operator: event.target.value })}
      >
        {FILTER_OPERATORS.map((op) => (
          <option key={op.value} value={op.value}>
            {t(operatorMessageKey(op.value))}
          </option>
        ))}
      </select>
      {needsFilterValue(filter.operator) ? (
        <FilterValueField
          datasetId={datasetId}
          column={meta}
          operator={filter.operator}
          value={filter.value ?? ''}
          contextFilters={allFilters}
          onChange={(value) => onChange({ ...filter, value })}
        />
      ) : (
        <span className="muted small">{t('noValueNeeded')}</span>
      )}
      <FilterOptionsButton
        filter={filter}
        allFilters={allFilters}
        lang={lang}
        palette={palette}
        onChange={onChange}
      />
      <button
        type="button"
        className="filter-remove"
        title={t('remove')}
        aria-label={t('removeNamed', { name: fieldName })}
        onClick={onRemove}
      >
        <IconTrash />
      </button>
    </div>
  )
}
