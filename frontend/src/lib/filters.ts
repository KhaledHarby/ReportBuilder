import type { DatasetColumn, LayoutFilter } from '../api/types'
import type { Lang } from '../i18n/locale'
import { columnLabel, localizedText } from '../i18n/localized'
import type { MessageKey } from '../i18n/messages'

export const MIN_FILTERS_PER_ROW = 1
export const MAX_FILTERS_PER_ROW = 6
export const DEFAULT_FILTERS_PER_ROW = 2

export function filtersPerRow(value: number | null | undefined) {
  const count = Number(value)
  if (!Number.isFinite(count) || count < 1) {
    return DEFAULT_FILTERS_PER_ROW
  }
  return Math.min(MAX_FILTERS_PER_ROW, Math.max(MIN_FILTERS_PER_ROW, Math.round(count)))
}

export const FILTER_OPERATORS = [
  { value: 'eq', label: 'equals' },
  { value: 'in', label: 'is one of' },
  { value: 'neq', label: 'not equals' },
  { value: 'gt', label: 'greater than' },
  { value: 'gte', label: 'greater or equal' },
  { value: 'lt', label: 'less than' },
  { value: 'lte', label: 'less or equal' },
  { value: 'contains', label: 'contains' },
  { value: 'isNull', label: 'is empty' },
  { value: 'isNotNull', label: 'is not empty' },
] as const

export function operatorMessageKey(operator: string): MessageKey {
  switch (operator) {
    case 'eq':
      return 'opEq'
    case 'in':
      return 'opIn'
    case 'neq':
      return 'opNeq'
    case 'gt':
      return 'opGt'
    case 'gte':
      return 'opGte'
    case 'lt':
      return 'opLt'
    case 'lte':
      return 'opLte'
    case 'contains':
      return 'opContains'
    case 'isNull':
      return 'opIsNull'
    case 'isNotNull':
      return 'opIsNotNull'
    default:
      return 'opEq'
  }
}

export function needsFilterValue(operator: string) {
  return operator !== 'isNull' && operator !== 'isNotNull'
}

export function isFilterFixed(filter: LayoutFilter | undefined | null) {
  return Boolean(filter?.isFixed)
}

export function isFilterHidden(filter: LayoutFilter | undefined | null) {
  return Boolean(filter?.isHidden)
}

export function isFilterRequired(filter: LayoutFilter | undefined | null) {
  return Boolean(filter?.isRequired)
}

export function filterHasValue(filter: LayoutFilter) {
  if (!needsFilterValue(filter.operator)) {
    return true
  }
  const value = filter.value ?? ''
  if (filter.operator === 'in' || filter.operator === 'eq') {
    return value.split(/[,;\n]/).some((part) => part.trim().length > 0)
  }
  return value.trim().length > 0
}

export function missingRequiredFilters(filters: LayoutFilter[]) {
  return filters.filter((filter) => isFilterRequired(filter) && !filterHasValue(filter))
}

export function isFilterLocked(filter: LayoutFilter | undefined | null) {
  return isFilterFixed(filter) || isFilterHidden(filter)
}

export function displayFilterValue(
  filter: LayoutFilter,
  labels?: { empty: string; notEmpty: string },
) {
  if (filter.operator === 'isNull') {
    return labels?.empty ?? 'empty'
  }
  if (filter.operator === 'isNotNull') {
    return labels?.notEmpty ?? 'not empty'
  }
  const value = filter.value?.trim()
  return value ? value : '—'
}

export function lockFixedFilters(saved: LayoutFilter[], next: LayoutFilter[]) {
  return next.map((filter, index) => {
    const original = saved[index]
    return isFilterLocked(original) ? { ...original } : filter
  })
}

export function filterDisplayLabel(
  lang: Lang,
  filter: Pick<LayoutFilter, 'field' | 'alias' | 'aliasArabic'>,
  column?: DatasetColumn | null,
) {
  const english = filter.alias?.trim() ?? ''
  const arabic = filter.aliasArabic?.trim() ?? ''
  if (english || arabic) {
    return localizedText(lang, english, arabic)
  }
  if (column) {
    return columnLabel(lang, column)
  }
  return filter.field
}

function fieldKey(field: string | null | undefined) {
  return (field ?? '').trim().toLowerCase()
}

export function hasCascadeCycle(filters: LayoutFilter[]) {
  for (const filter of filters) {
    const seen = new Set<string>()
    let current = filter.dependsOn?.trim()
    while (current) {
      const key = fieldKey(current)
      if (key === fieldKey(filter.field) || seen.has(key)) {
        return true
      }
      seen.add(key)
      current =
        filters.find((item) => fieldKey(item.field) === key)?.dependsOn?.trim() ||
        undefined
    }
  }
  return false
}

export function canDependOn(filters: LayoutFilter[], field: string, parentField: string) {
  if (!fieldKey(parentField) || fieldKey(field) === fieldKey(parentField)) {
    return false
  }
  const trial = filters.map((item) =>
    fieldKey(item.field) === fieldKey(field)
      ? { ...item, dependsOn: parentField }
      : item,
  )
  return !hasCascadeCycle(trial)
}

export function dependableParents(filters: LayoutFilter[], field: string) {
  const seen = new Set<string>()
  const parents: LayoutFilter[] = []
  for (const item of filters) {
    const key = fieldKey(item.field)
    if (!key || seen.has(key) || !canDependOn(filters, field, item.field)) {
      continue
    }
    seen.add(key)
    parents.push(item)
  }
  return parents
}

export function cascadeConstraintKey(field: string | undefined, filters?: LayoutFilter[]) {
  if (!field || !filters?.length) {
    return ''
  }
  const parts: string[] = []
  const seen = new Set<string>()
  let current = filters.find((item) => fieldKey(item.field) === fieldKey(field))?.dependsOn?.trim()
  while (current) {
    const key = fieldKey(current)
    if (!seen.add(key)) {
      break
    }
    const parent = filters.find((item) => fieldKey(item.field) === key)
    parts.push(`${key}\0${parent?.operator ?? ''}\0${parent?.value ?? ''}`)
    current = parent?.dependsOn?.trim()
  }
  return parts.join('\n')
}

function dependsOnChainHits(
  filters: LayoutFilter[],
  start: LayoutFilter,
  ancestorField: string,
) {
  const target = fieldKey(ancestorField)
  const seen = new Set<string>()
  let current = start.dependsOn?.trim()
  while (current) {
    const key = fieldKey(current)
    if (key === target) {
      return true
    }
    if (!seen.add(key)) {
      return false
    }
    current =
      filters.find((item) => fieldKey(item.field) === key)?.dependsOn?.trim() ||
      undefined
  }
  return false
}

export function clearDescendantFilterValues(
  filters: LayoutFilter[],
  changedIndex: number,
  previous?: LayoutFilter,
) {
  const changed = filters[changedIndex]
  if (!changed) {
    return filters
  }
  const prior = previous ?? changed
  const sameConstraint =
    (prior.value ?? '') === (changed.value ?? '') &&
    prior.operator === changed.operator &&
    fieldKey(prior.field) === fieldKey(changed.field)
  if (sameConstraint) {
    return filters
  }

  return filters.map((filter, index) => {
    if (index === changedIndex) {
      return filter
    }
    if (!dependsOnChainHits(filters, filter, changed.field)) {
      return filter
    }
    if (!needsFilterValue(filter.operator)) {
      return filter
    }
    return { ...filter, value: '' }
  })
}
