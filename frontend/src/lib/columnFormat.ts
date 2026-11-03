import type { LayoutColumn, ReportLayout, ReportResult } from '../api/types'

export type ColumnFormat = 'date' | 'datetime' | 'time' | 'integer' | 'decimal'

export type ColumnFormatOption = {
  value: ColumnFormat
  labelKey:
    | 'formatDate'
    | 'formatDateTime'
    | 'formatTime'
    | 'formatInteger'
    | 'formatDecimal'
}

const DATE_TYPES = new Set([
  'date',
  'datetime',
  'datetime2',
  'smalldatetime',
  'datetimeoffset',
])

const INTEGER_TYPES = new Set(['int', 'bigint', 'smallint', 'tinyint'])

const DECIMAL_TYPES = new Set([
  'decimal',
  'numeric',
  'money',
  'smallmoney',
  'float',
  'real',
])

export function formatOptionsForSqlType(sqlType: string): ColumnFormatOption[] {
  const type = sqlType.toLowerCase()
  if (DATE_TYPES.has(type)) {
    return [
      { value: 'date', labelKey: 'formatDate' },
      { value: 'datetime', labelKey: 'formatDateTime' },
      { value: 'time', labelKey: 'formatTime' },
    ]
  }
  if (type === 'time') {
    return [{ value: 'time', labelKey: 'formatTime' }]
  }
  if (INTEGER_TYPES.has(type)) {
    return [
      { value: 'integer', labelKey: 'formatInteger' },
      { value: 'decimal', labelKey: 'formatDecimal' },
    ]
  }
  if (DECIMAL_TYPES.has(type)) {
    return [
      { value: 'decimal', labelKey: 'formatDecimal' },
      { value: 'integer', labelKey: 'formatInteger' },
    ]
  }
  return []
}

export function hasFormatOptions(sqlType: string) {
  return formatOptionsForSqlType(sqlType).length > 0
}

export function formatColumnValue(
  value: unknown,
  format: string | null | undefined,
  locale: string,
  column?: { name?: string | null; label?: string | null },
) {
  if (value == null || value === '') {
    return '—'
  }

  const year = looksLikeYearColumn(column?.name, column?.label)
  switch (format) {
    case 'date':
      return formatDatePart(value, locale, { dateStyle: 'short' })
    case 'datetime':
      return formatDatePart(value, locale, {
        dateStyle: 'short',
        timeStyle: 'short',
      })
    case 'time':
      return formatDatePart(value, locale, { timeStyle: 'short' })
    case 'integer':
      return formatNumberPart(value, locale, 0, year)
    case 'decimal':
      return formatNumberPart(value, locale, 2)
    default:
      return formatAuto(value, locale, year)
  }
}

/** Year, FiscalYear, Year No, and سنة stay ungrouped so 2015 is not shown as 2,015. */
export function looksLikeYearColumn(name?: string | null, label?: string | null) {
  return [name, label].some((part) => {
    const text = part?.trim() ?? ''
    if (!text) {
      return false
    }
    return (
      text.includes('سنة') ||
      /year$/i.test(text) ||
      /(^|[^a-z0-9])(year|yr)([^a-z0-9]|$)/i.test(text)
    )
  })
}

function formatAuto(value: unknown, locale: string, year = false) {
  if (typeof value === 'number') {
    if (year && Number.isInteger(value)) {
      return formatPlainInteger(value, locale)
    }
    return Number.isInteger(value)
      ? value.toLocaleString(locale)
      : value.toLocaleString(locale, { maximumFractionDigits: 2 })
  }
  if (year && typeof value === 'string' && /^-?\d+$/.test(value.trim())) {
    return value.trim()
  }
  return String(value)
}

function formatPlainInteger(value: number, locale: string) {
  return value.toLocaleString(locale, {
    useGrouping: false,
    maximumFractionDigits: 0,
  })
}

function parseDate(value: unknown) {
  if (value instanceof Date) {
    return Number.isNaN(value.getTime()) ? null : value
  }
  if (typeof value === 'string' || typeof value === 'number') {
    const parsed = new Date(value)
    return Number.isNaN(parsed.getTime()) ? null : parsed
  }
  return null
}

function formatDatePart(
  value: unknown,
  locale: string,
  options: Intl.DateTimeFormatOptions,
) {
  const parsed = parseDate(value)
  if (!parsed) {
    return String(value)
  }
  return parsed.toLocaleString(locale, options)
}

function formatNumberPart(
  value: unknown,
  locale: string,
  fractionDigits: number,
  plain = false,
) {
  const number =
    typeof value === 'number' ? value : Number(String(value).trim())
  if (!Number.isFinite(number)) {
    return String(value)
  }
  if (plain) {
    return formatPlainInteger(number, locale)
  }
  return number.toLocaleString(locale, {
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits,
  })
}

export function buildFormatMap(
  layout: ReportLayout | undefined,
  result: ReportResult | null | undefined,
) {
  const map: Record<string, string | undefined> = {}
  if (!layout?.columns.length) {
    return map
  }

  layout.columns.forEach((column: LayoutColumn, index) => {
    if (!column.format) {
      return
    }

    const resultColumn = result?.columns[index]
    if (resultColumn?.name) {
      map[resultColumn.name] = column.format
    }
    map[column.field] = column.format
    if (column.alias?.trim()) {
      map[column.alias.trim()] = column.format
    }
  })

  return map
}

export function buildUrlMap(
  layout: ReportLayout | undefined,
  result: ReportResult | null | undefined,
) {
  const map: Record<string, string> = {}
  if (!layout?.columns.length) {
    return map
  }

  layout.columns.forEach((column: LayoutColumn, index) => {
    const template = column.urlTemplate?.trim()
    if (!template) {
      return
    }

    const resultColumn = result?.columns[index]
    if (resultColumn?.name) {
      map[resultColumn.name] = template
    }
    map[column.field] = template
    if (column.alias?.trim()) {
      map[column.alias.trim()] = template
    }
  })

  return map
}

export function urlTokensForResult(
  layout: ReportLayout | undefined,
  result: ReportResult | null | undefined,
) {
  return (result?.columns ?? []).map((column, index) => ({
    id: column.name,
    field: layout?.columns[index]?.field ?? column.name,
    label: column.label,
    displayName: column.displayName?.trim() || undefined,
    displayNameArabic: column.displayNameArabic?.trim() || undefined,
  }))
}

export function resultNameToField(
  layout: ReportLayout | undefined,
  result: ReportResult | null | undefined,
  columnName: string,
) {
  const lower = columnName.toLowerCase()
  const index = result?.columns.findIndex((column) => column.name.toLowerCase() === lower) ?? -1
  if (index >= 0 && layout?.columns[index]?.field) {
    return layout.columns[index].field
  }
  const match = layout?.columns.find(
    (column) =>
      column.field.toLowerCase() === lower ||
      column.alias?.trim().toLowerCase() === lower,
  )
  return match?.field ?? columnName
}

export function fieldToResultName(
  layout: ReportLayout | undefined,
  result: ReportResult | null | undefined,
  field: string,
) {
  const lower = field.toLowerCase()
  const index = layout?.columns.findIndex((column) => column.field.toLowerCase() === lower) ?? -1
  if (index >= 0 && result?.columns[index]?.name) {
    return result.columns[index].name
  }
  const aliased = layout?.columns.find((column) => column.alias?.trim().toLowerCase() === lower)
  if (aliased) {
    const aliasIndex = layout!.columns.indexOf(aliased)
    return result?.columns[aliasIndex]?.name ?? field
  }
  return field
}
