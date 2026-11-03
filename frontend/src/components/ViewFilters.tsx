import type { DatasetColumn, LayoutFilter } from '../api/types'
import {
  clearDescendantFilterValues,
  displayFilterValue,
  filterDisplayLabel,
  filtersPerRow,
  filterHasValue,
  isFilterFixed,
  isFilterHidden,
  isFilterLocked,
  isFilterRequired,
  needsFilterValue,
} from '../lib/filters'
import { FilterValueField } from './FilterValueField'
import { IconFilter } from './Icons'
import { useLocale } from '../i18n/LocaleProvider'

type Props = {
  datasetId: string
  reportId?: string
  columns: DatasetColumn[]
  saved: LayoutFilter[]
  filters: LayoutFilter[]
  columnsPerRow?: number | null
  onChange: (filters: LayoutFilter[]) => void
  onReset: () => void
}

function sameFilter(a: LayoutFilter, b: LayoutFilter) {
  return (
    a.field === b.field &&
    a.operator === b.operator &&
    (a.value ?? '') === (b.value ?? '')
  )
}

function columnFor(columns: DatasetColumn[], field: string) {
  const lower = field.toLowerCase()
  return columns.find((column) => column.columnName.toLowerCase() === lower)
}

export function ViewFilters({
  datasetId,
  reportId,
  columns,
  saved,
  filters,
  columnsPerRow,
  onChange,
  onReset,
}: Props) {
  const { lang, t } = useLocale()
  if (saved.every((filter) => isFilterHidden(filter))) {
    return null
  }

  const dirty = filters.length !== saved.length || filters.some((filter, index) => !sameFilter(filter, saved[index] ?? filter))
  const canEditAny = saved.some((filter) => !isFilterLocked(filter))

  const setValue = (index: number, value: string) => {
    if (isFilterLocked(saved[index])) {
      return
    }
    const previous = filters[index]
    const next = filters.slice()
    next[index] = { ...next[index], value }
    onChange(clearDescendantFilterValues(next, index, previous))
  }

  const clearValue = (index: number) => {
    setValue(index, '')
  }

  const restoreOne = (index: number) => {
    if (isFilterLocked(saved[index])) {
      return
    }
    const next = filters.slice()
    next[index] = { ...saved[index] }
    onChange(next)
  }

  return (
    <section className="view-filters">
      <div className="view-filters-head">
        <div>
          <h3>
            <IconFilter />
            {t('filters')}
          </h3>
          <p className="muted small">
            {canEditAny ? t('viewFiltersHint') : t('viewFiltersLocked')}
          </p>
        </div>
        {canEditAny && (
          <button type="button" className="btn" disabled={!dirty} onClick={onReset}>
            {t('resetToSaved')}
          </button>
        )}
      </div>
      <div
        className="filter-list"
        style={{ ['--filter-columns' as string]: String(filtersPerRow(columnsPerRow)) }}
      >
        {filters.map((filter, index) => {
          if (isFilterHidden(saved[index] ?? filter)) {
            return null
          }
          const previousVisible = filters
            .slice(0, index)
            .filter((_, earlier) => !isFilterHidden(saved[earlier] ?? filters[earlier]))
            .at(-1)
          const orJoin =
            previousVisible &&
            previousVisible.field.toLowerCase() === filter.field.toLowerCase() &&
            (previousVisible.operator === 'eq' || previousVisible.operator === 'in') &&
            (filter.operator === 'eq' || filter.operator === 'in')
          const column = columnFor(columns, filter.field)
          const label = filterDisplayLabel(lang, filter, column)
          const locked = isFilterFixed(saved[index] ?? filter)
          const required = isFilterRequired(saved[index] ?? filter)
          const needsValue = required && !filterHasValue(filter)
          const changed = !locked && !sameFilter(filter, saved[index] ?? filter)
          const canClear = !locked && needsFilterValue(filter.operator)

          return (
            <div key={`${filter.field}-${filter.operator}-${index}`}>
              {orJoin && <div className="filter-join">{t('or')}</div>}
              <div className={`view-filter-row ${changed ? 'changed' : ''} ${locked ? 'locked' : ''} ${needsValue ? 'needs-value' : ''}`}>
                <strong className="view-filter-label" title={filter.field}>
                  {label}
                  {required && (
                    <span className="filter-required-mark" title={t('filterRequiredHint')}>
                      *
                    </span>
                  )}
                </strong>
                <div className="view-filter-control">
                  {locked ? (
                    <span className="view-filter-value">
                      {displayFilterValue(filter, {
                        empty: t('empty'),
                        notEmpty: t('notEmpty'),
                      })}
                      <span className="badge">{t('fixed')}</span>
                    </span>
                  ) : canClear ? (
                    <FilterValueField
                      datasetId={datasetId}
                      reportId={reportId}
                      column={column}
                      operator={filter.operator}
                      value={filter.value ?? ''}
                      contextFilters={filters}
                      onChange={(value) => setValue(index, value)}
                    />
                  ) : (
                    <span className="view-filter-fixed muted">{t('noValueNeeded')}</span>
                  )}
                  <div className="view-filter-actions">
                    {canClear && filter.value && (
                      <button type="button" className="link" onClick={() => clearValue(index)}>
                        {t('clear')}
                      </button>
                    )}
                    {changed && (
                      <button type="button" className="link" onClick={() => restoreOne(index)}>
                        {t('reset')}
                      </button>
                    )}
                  </div>
                </div>
              </div>
            </div>
          )
        })}
      </div>
    </section>
  )
}
