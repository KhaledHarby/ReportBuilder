import { resolveAppHref } from './reportLinks'

export type AnalyticsSelections = Record<string, string[]>

export type AnalyticsSelectionState = 'default' | 'compare'

export type AnalyticsDrillStep = {
  dimensionId: string
  value: string
}

export type AnalyticsViewState = {
  default: AnalyticsSelections
  compare: AnalyticsSelections
  locked: string[]
  dimStates: Record<string, AnalyticsSelectionState>
  /** Ordered focus path. Each step is a single selected value the viewer drilled into. */
  drill: AnalyticsDrillStep[]
}

export const ANALYTICS_NULL = '__RB_NULL__'

export function emptySelections(): AnalyticsSelections {
  return {}
}

export function emptyViewState(): AnalyticsViewState {
  return { default: {}, compare: {}, locked: [], dimStates: {}, drill: [] }
}

export function normalizeViewState(
  view: Partial<AnalyticsViewState> | null | undefined,
): AnalyticsViewState {
  const drill = Array.isArray(view?.drill)
    ? view.drill.filter(
        (step): step is AnalyticsDrillStep =>
          Boolean(step) &&
          typeof step.dimensionId === 'string' &&
          typeof step.value === 'string' &&
          step.dimensionId.trim().length > 0 &&
          step.value.length > 0,
      )
    : []
  return {
    default: view?.default ?? {},
    compare: view?.compare ?? {},
    locked: Array.isArray(view?.locked) ? view.locked.filter((id) => typeof id === 'string') : [],
    dimStates: view?.dimStates ?? {},
    drill,
  }
}

export function cloneSelections(selections: AnalyticsSelections): AnalyticsSelections {
  const next: AnalyticsSelections = {}
  for (const [key, values] of Object.entries(selections)) {
    next[key] = [...values]
  }
  return next
}

export function toggleSelection(
  selections: AnalyticsSelections,
  dimensionId: string,
  value: string,
): AnalyticsSelections {
  const current = selections[dimensionId] ?? []
  const exists = current.some((item) => item.toLowerCase() === value.toLowerCase())
  const nextValues = exists
    ? current.filter((item) => item.toLowerCase() !== value.toLowerCase())
    : [...current, value]

  const next: AnalyticsSelections = { ...selections }
  if (nextValues.length === 0) {
    delete next[dimensionId]
  } else {
    next[dimensionId] = nextValues
  }
  return next
}

export function clearDimension(
  selections: AnalyticsSelections,
  dimensionId: string,
): AnalyticsSelections {
  const next = { ...selections }
  delete next[dimensionId]
  return next
}

export function selectValues(
  selections: AnalyticsSelections,
  dimensionId: string,
  values: string[],
): AnalyticsSelections {
  const current = selections[dimensionId] ?? []
  const merged = [...current]
  for (const value of values) {
    if (!merged.some((item) => item.toLowerCase() === value.toLowerCase())) {
      merged.push(value)
    }
  }
  const next: AnalyticsSelections = { ...selections }
  if (merged.length === 0) {
    delete next[dimensionId]
  } else {
    next[dimensionId] = merged
  }
  return next
}

export function clearSelections(locked: string[] = [], current?: AnalyticsSelections): AnalyticsSelections {
  if (!current || locked.length === 0) {
    return {}
  }
  const next: AnalyticsSelections = {}
  for (const id of locked) {
    if (current[id]?.length) {
      next[id] = [...current[id]]
    }
  }
  return next
}

export function toggleLocked(locked: string[], dimensionId: string): string[] {
  return locked.includes(dimensionId)
    ? locked.filter((id) => id !== dimensionId)
    : [...locked, dimensionId]
}

export function selectionEntries(
  selections: AnalyticsSelections,
  labels: Record<string, string>,
  state: AnalyticsSelectionState = 'default',
): { dimensionId: string; label: string; value: string; display: string; state: AnalyticsSelectionState }[] {
  const entries: {
    dimensionId: string
    label: string
    value: string
    display: string
    state: AnalyticsSelectionState
  }[] = []

  for (const [dimensionId, values] of Object.entries(selections)) {
    for (const value of values) {
      entries.push({
        dimensionId,
        label: labels[dimensionId] ?? dimensionId,
        value,
        display: value === ANALYTICS_NULL ? '(empty)' : value,
        state,
      })
    }
  }

  return entries
}

export function maxDimensionCount(values: { count: number }[]): number {
  return values.reduce((max, item) => Math.max(max, item.count), 0)
}

function compactSelections(selections: AnalyticsSelections): Record<string, string[]> {
  const compact: Record<string, string[]> = {}
  for (const [key, values] of Object.entries(selections)) {
    if (values.length > 0) {
      compact[key] = values
    }
  }
  return compact
}

function toBase64Url(json: string): string {
  return btoa(unescape(encodeURIComponent(json)))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '')
}

function fromBase64Url(raw: string): string {
  const padded = raw.replace(/-/g, '+').replace(/_/g, '/')
  const pad = padded.length % 4 === 0 ? '' : '='.repeat(4 - (padded.length % 4))
  return decodeURIComponent(escape(atob(padded + pad)))
}

function parseSelectionMap(value: unknown): AnalyticsSelections {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return {}
  }
  const result: AnalyticsSelections = {}
  for (const [key, item] of Object.entries(value as Record<string, unknown>)) {
    if (!Array.isArray(item)) continue
    const values = item
      .filter((entry): entry is string => typeof entry === 'string' && entry.trim().length > 0)
      .map((entry) => entry.trim())
    if (values.length > 0) {
      result[key] = values
    }
  }
  return result
}

function parseDrill(value: unknown): AnalyticsDrillStep[] {
  if (!Array.isArray(value)) {
    return []
  }
  const steps: AnalyticsDrillStep[] = []
  for (const item of value) {
    if (!Array.isArray(item) || item.length < 2) {
      continue
    }
    const dimensionId = item[0]
    const stepValue = item[1]
    if (typeof dimensionId !== 'string' || typeof stepValue !== 'string') {
      continue
    }
    const id = dimensionId.trim()
    if (!id || stepValue.length === 0) {
      continue
    }
    steps.push({ dimensionId: id, value: stepValue })
  }
  return steps
}

export function encodeViewState(view: AnalyticsViewState): string {
  const drill = (view.drill ?? []).filter((step) => step.dimensionId && step.value)
  const payload = {
    d: compactSelections(view.default),
    c: compactSelections(view.compare),
    l: view.locked.filter(Boolean),
    s: view.dimStates,
    p: drill.map((step) => [step.dimensionId, step.value]),
  }
  const hasCompare = Object.keys(payload.c).length > 0
  const hasLocked = payload.l.length > 0
  const hasStates = Object.values(payload.s).some((item) => item === 'compare')
  const hasDrill = payload.p.length > 0
  if (!hasCompare && !hasLocked && !hasStates && !hasDrill) {
    return encodeSelections(view.default)
  }
  return toBase64Url(JSON.stringify(payload))
}

export function encodeSelections(selections: AnalyticsSelections): string {
  const compact = compactSelections(selections)
  if (Object.keys(compact).length === 0) {
    return ''
  }
  return toBase64Url(JSON.stringify(compact))
}

export function decodeViewState(raw: string | null | undefined): AnalyticsViewState {
  if (!raw?.trim()) {
    return emptyViewState()
  }
  try {
    const parsed = JSON.parse(fromBase64Url(raw)) as Record<string, unknown>
    if (parsed && typeof parsed === 'object' && 'd' in parsed) {
      const dimStates: Record<string, AnalyticsSelectionState> = {}
      if (parsed.s && typeof parsed.s === 'object') {
        for (const [key, value] of Object.entries(parsed.s as Record<string, unknown>)) {
          dimStates[key] = value === 'compare' ? 'compare' : 'default'
        }
      }
      return {
        default: parseSelectionMap(parsed.d),
        compare: parseSelectionMap(parsed.c),
        locked: Array.isArray(parsed.l)
          ? parsed.l.filter((item): item is string => typeof item === 'string')
          : [],
        dimStates,
        drill: parseDrill(parsed.p),
      }
    }
    return {
      default: parseSelectionMap(parsed),
      compare: {},
      locked: [],
      dimStates: {},
      drill: [],
    }
  } catch {
    return emptyViewState()
  }
}

export function decodeSelections(raw: string | null | undefined): AnalyticsSelections {
  return decodeViewState(raw).default
}

export function buildShareUrl(pathname: string, view: AnalyticsViewState): string {
  const url = new URL(pathname, window.location.origin)
  const encoded = encodeViewState(view)
  if (encoded) {
    url.searchParams.set('sel', encoded)
  } else {
    url.searchParams.delete('sel')
  }
  return `${url.pathname}${url.search}${url.hash}`
}

export function resolveDimState(
  dimStates: Record<string, AnalyticsSelectionState>,
  dimensionId: string,
): AnalyticsSelectionState {
  return dimStates[dimensionId] === 'compare' ? 'compare' : 'default'
}

export function moveDimensionState(
  view: AnalyticsViewState,
  dimensionId: string,
  nextState: AnalyticsSelectionState,
): AnalyticsViewState {
  const current = resolveDimState(view.dimStates, dimensionId)
  if (current === nextState) {
    return view
  }
  const from = current === 'compare' ? view.compare : view.default
  const to = nextState === 'compare' ? view.compare : view.default
  const values = from[dimensionId] ?? []
  const nextFrom = clearDimension(from, dimensionId)
  const nextTo = values.length ? selectValues(to, dimensionId, values) : { ...to }
  const dimStates = { ...view.dimStates }
  if (nextState === 'default') {
    delete dimStates[dimensionId]
  } else {
    dimStates[dimensionId] = 'compare'
  }
  return {
    ...view,
    default: current === 'compare' ? nextFrom : nextTo,
    compare: current === 'compare' ? nextTo : nextFrom,
    dimStates,
  }
}

export function applyRowToSelections(
  selections: AnalyticsSelections,
  row: Record<string, unknown>,
  columns: { id: string; field?: string }[],
  dimensions: { id: string; field: string }[],
): AnalyticsSelections {
  let next = { ...selections }
  for (const dimension of dimensions) {
    const column = columns.find(
      (item) => item.id === dimension.id || item.field === dimension.field,
    )
    if (!column) {
      continue
    }
    const raw = row[column.id]
    if (raw == null || raw === '') {
      continue
    }
    next = selectValues(next, dimension.id, [String(raw)])
  }
  return next
}

const CURRENT_COLUMN_TOKENS = new Set(['field', 'value', 'this'])

function rowValue(row: Record<string, unknown>, key: string): unknown {
  if (Object.prototype.hasOwnProperty.call(row, key)) {
    return row[key]
  }
  const match = Object.keys(row).find((item) => item.toLowerCase() === key.toLowerCase())
  return match ? row[match] : undefined
}

type UrlTokenColumn = {
  id: string
  field?: string
  label: string
  displayName?: string
  displayNameArabic?: string
}

function matchColumn(columns: UrlTokenColumn[], key: string, current?: UrlTokenColumn) {
  const lower = key.trim().toLowerCase()
  if (CURRENT_COLUMN_TOKENS.has(lower) && current) {
    return current
  }
  return columns.find(
    (item) =>
      item.id.toLowerCase() === lower ||
      (item.field?.toLowerCase() ?? '') === lower ||
      item.label.toLowerCase() === lower ||
      (item.displayName?.toLowerCase() ?? '') === lower ||
      (item.displayNameArabic?.toLowerCase() ?? '') === lower,
  )
}

function isSafeHref(value: string) {
  return value.startsWith('/') || value.startsWith('//') || /^https?:\/\//i.test(value)
}

/** True when a saved template can become an http(s) or site-relative link. */
export function isClickableUrlTemplate(template: string | null | undefined) {
  const trimmed = template?.trim() ?? ''
  if (!trimmed || /^(javascript|data|vbscript):/i.test(trimmed)) {
    return false
  }
  const preview = trimmed.replace(/\{[^}]+\}/g, 'x')
  return isSafeHref(preview)
}

export function interpolateUrl(
  template: string,
  row: Record<string, unknown>,
  columns: UrlTokenColumn[],
  currentColumnId?: string,
): string | null {
  const trimmed = template.trim()
  if (!trimmed || /^(javascript|data|vbscript):/i.test(trimmed)) {
    return null
  }

  const current = currentColumnId
    ? matchColumn(columns, currentColumnId) ??
      columns.find((item) => item.id === currentColumnId)
    : undefined

  const read = (key: string) => {
    const column = matchColumn(columns, key, current)
    const raw = column ? rowValue(row, column.id) : rowValue(row, key)
    return raw == null || raw === '' ? '' : String(raw)
  }

  if (/^\{[^}]+\}$/.test(trimmed)) {
    const value = read(trimmed.slice(1, -1)).trim()
    if (!value || /^(javascript|data|vbscript):/i.test(value) || !isSafeHref(value)) {
      return null
    }
    const href = value.startsWith('//') ? `https:${value}` : value
    return resolveAppHref(href)
  }

  const replaced = trimmed.replace(/\{([^}]+)\}/g, (_, token: string) =>
    encodeURIComponent(read(token.trim())),
  )
  if (!isSafeHref(replaced) || /^(javascript|data|vbscript):/i.test(replaced)) {
    return null
  }
  const href = replaced.startsWith('//') ? `https:${replaced}` : replaced
  return resolveAppHref(href)
}
