import type { DatasetColumn, LayoutFilter, ReportLayout } from '../api/types'
import { isFilterLocked, needsFilterValue } from './filters'

const RESERVED_QUERY_KEYS = new Set(['lang', 'skip', 'take', 'format'])

export function appBasePath() {
  const base = import.meta.env.BASE_URL.replace(/\/+$/, '')
  return base === '/' ? '' : base
}

/** Prefix the Vite/IIS app path so /reports/... works under /ReportBuilder. */
export function resolveAppHref(href: string) {
  const trimmed = href.trim()
  if (!trimmed.startsWith('/') || trimmed.startsWith('//')) {
    return trimmed
  }

  const base = appBasePath()
  if (!base) {
    return trimmed
  }
  if (trimmed === base || trimmed.startsWith(`${base}/`)) {
    return trimmed
  }
  return `${base}${trimmed}`
}

export function cellLinkProps(href: string) {
  return { href, target: '_blank' as const, rel: 'noreferrer' }
}

export function buildReportDrillthroughUrl(
  reportId: string,
  sourceField: string,
  targetField?: string,
) {
  const target = (targetField ?? sourceField).trim()
  if (!target) {
    return `/reports/${reportId}/view`
  }
  const token = `{${sourceField}}`
  const query = `${encodeURIComponent(target)}=${token}`
  return `/reports/${reportId}/view?${query}`
}

export function reportIdFromUrlTemplate(template: string | null | undefined) {
  const match = template?.trim().match(/\/reports\/([^/?#]+)\/view(?:\?|$|#)/i)
  return match?.[1] ?? ''
}

export function drillthroughTargetField(template: string | null | undefined) {
  const match = template?.trim().match(/\/view\?([^#]*)/i)
  if (!match) {
    return ''
  }
  const params = new URLSearchParams(match[1])
  for (const [key] of params.entries()) {
    if (!RESERVED_QUERY_KEYS.has(key.toLowerCase()) && key.trim()) {
      return key.trim()
    }
  }
  return ''
}

export function allowedDrillthroughFields(
  layout?: ReportLayout | null,
  columns?: Array<Pick<DatasetColumn, 'columnName' | 'isHidden'>> | null,
) {
  const fields = new Set<string>()
  const add = (field?: string | null) => {
    const name = field?.trim()
    if (name) {
      fields.add(name)
    }
  }
  for (const filter of layout?.filters ?? []) {
    add(filter.field)
  }
  for (const column of layout?.columns ?? []) {
    add(column.field)
  }
  for (const field of layout?.groupBy ?? []) {
    add(field)
  }
  for (const column of columns ?? []) {
    if (!column.isHidden) {
      add(column.columnName)
    }
  }
  return fields
}

export function resolveDrillthroughField(allowed: Set<string>, key: string) {
  return canonicalField(allowed, key)
}

export function unusedDrillthroughKeys(params: URLSearchParams, allowed: Set<string>) {
  const unused: string[] = []
  const seen = new Set<string>()
  for (const [key] of params.entries()) {
    if (RESERVED_QUERY_KEYS.has(key.toLowerCase()) || !key.trim()) {
      continue
    }
    const lower = key.trim().toLowerCase()
    if (seen.has(lower)) {
      continue
    }
    seen.add(lower)
    if (!canonicalField(allowed, key)) {
      unused.push(key.trim())
    }
  }
  return unused
}

function canonicalField(allowed: Set<string>, key: string) {
  const lower = key.trim().toLowerCase()
  if (!lower) {
    return null
  }
  for (const field of allowed) {
    if (field.toLowerCase() === lower) {
      return field
    }
  }
  return null
}

function queryValue(params: URLSearchParams, field: string) {
  const values = fieldValues(params, field).filter(Boolean)
  if (values.length === 0) {
    return null
  }
  return values.join(',')
}

function fieldValues(params: URLSearchParams, field: string) {
  const lower = field.toLowerCase()
  const values: string[] = []
  for (const [key, value] of params.entries()) {
    if (RESERVED_QUERY_KEYS.has(key.toLowerCase())) {
      continue
    }
    if (key.toLowerCase() === lower) {
      values.push(value.trim())
    }
  }
  return values
}

function filterText(filter: LayoutFilter | undefined) {
  return (filter?.value ?? '').trim()
}

/** Keep the address in sync with viewer choices so the link can be shared. */
export function filtersToSearchParams(
  current: URLSearchParams,
  saved: LayoutFilter[],
  active: LayoutFilter[],
) {
  const next = new URLSearchParams(current)
  const editable = new Map<string, { name: string; saved: LayoutFilter; active: LayoutFilter }[]>()

  saved.forEach((savedFilter, index) => {
    if (isFilterLocked(savedFilter) || !needsFilterValue(savedFilter.operator)) {
      return
    }
    const name = savedFilter.field.trim()
    if (!name) {
      return
    }
    const key = name.toLowerCase()
    const rows = editable.get(key) ?? []
    rows.push({ name, saved: savedFilter, active: active[index] ?? savedFilter })
    editable.set(key, rows)
  })

  const deleteKeys: string[] = []
  for (const key of next.keys()) {
    if (RESERVED_QUERY_KEYS.has(key.toLowerCase())) {
      continue
    }
    if (editable.has(key.trim().toLowerCase())) {
      deleteKeys.push(key)
    }
  }
  for (const key of new Set(deleteKeys)) {
    next.delete(key)
  }

  for (const rows of editable.values()) {
    const changed = rows.some((row) => filterText(row.active) !== filterText(row.saved))
    if (!changed) {
      continue
    }
    for (const row of rows) {
      next.append(row.name, filterText(row.active))
    }
  }

  return next
}

export function applyQueryParamsToFilters(
  saved: LayoutFilter[],
  params: URLSearchParams,
  allowedFields: Set<string>,
): LayoutFilter[] | null {
  if ([...params.keys()].every((key) => RESERVED_QUERY_KEYS.has(key.toLowerCase()))) {
    return null
  }

  const queues = new Map<string, string[]>()
  for (const filter of saved) {
    const key = filter.field.trim().toLowerCase()
    if (!key || queues.has(key)) {
      continue
    }
    const values = fieldValues(params, filter.field)
    if (values.length > 0) {
      queues.set(key, values)
    }
  }

  const used = new Set<string>()
  const next = saved.map((filter) => {
    const key = filter.field.trim().toLowerCase()
    const queue = queues.get(key)
    if (!queue || queue.length === 0) {
      return { ...filter }
    }
    const value = queue.shift() ?? ''
    used.add(key)
    const operator = needsFilterValue(filter.operator) ? filter.operator : 'eq'
    return { ...filter, operator, value }
  })

  for (const [key] of params.entries()) {
    if (RESERVED_QUERY_KEYS.has(key.toLowerCase())) {
      continue
    }
    const field = canonicalField(allowedFields, key)
    if (!field || used.has(field.toLowerCase())) {
      continue
    }
    const value = queryValue(params, field)
    if (!value) {
      continue
    }
    used.add(field.toLowerCase())
    next.push({
      field,
      operator: 'eq',
      value,
      isHidden: true,
    })
  }

  const changed =
    next.length !== saved.length ||
    next.some(
      (filter, index) =>
        filter.value !== saved[index]?.value || filter.operator !== saved[index]?.operator,
    )
  return changed ? next : null
}
