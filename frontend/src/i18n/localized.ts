import type { DatasetColumn, ReportLayout, ReportResult } from '../api/types'
import { isLookupControl } from '../lib/lookupColumns'
import type { Lang } from './locale'

function withoutControlSuffix(label: string) {
  const cleaned = label.replace(/\s*[·.]\s*control$/i, '').trim()
  return cleaned || label
}

export function columnPresentation(
  layout: ReportLayout | null | undefined,
  result: ReportResult | null | undefined,
  lang: Lang,
) {
  const columnAlign: Record<string, 'left' | 'center' | 'right'> = {}
  const columnBands: Record<string, string> = {}
  const columnWrap: Record<string, true> = {}
  layout?.columns.forEach((column, index) => {
    const align = column.align?.toLowerCase()
    const band = localizedText(lang, column.headerBand, column.headerBandArabic).trim()
    const keys = [result?.columns[index]?.name, column.field, column.alias?.trim()].filter(
      (key): key is string => Boolean(key),
    )
    for (const key of keys) {
      if (align === 'left' || align === 'center' || align === 'right') {
        columnAlign[key] = align
      }
      if (band) {
        columnBands[key] = band
      }
      if (column.wrap) {
        columnWrap[key] = true
      }
    }
  })
  return { columnAlign, columnBands, columnWrap }
}

export function localizedText(
  lang: Lang,
  english?: string | null,
  arabic?: string | null,
) {
  const ar = arabic?.trim() ?? ''
  if (lang === 'ar' && ar) {
    return ar
  }
  const en = english?.trim() ?? ''
  return en || ar
}

export function datasetOptionLabel(
  lang: Lang,
  dataset: {
    displayName: string
    displayNameArabic?: string | null
    objectName: string
  },
) {
  const name = localizedText(lang, dataset.displayName, dataset.displayNameArabic)
  return name || dataset.objectName.trim()
}

export function columnLabel(
  lang: Lang,
  column: Pick<DatasetColumn, 'columnName' | 'displayName' | 'displayNameArabic'>,
  alias?: string | null,
) {
  if (alias?.trim()) {
    return alias.trim()
  }
  const text = localizedText(lang, column.displayName || column.columnName, column.displayNameArabic)
  return isLookupControl(column.columnName) ? withoutControlSuffix(text) : text
}

export function localizeResultColumns(
  lang: Lang,
  result: ReportResult,
  columns: DatasetColumn[],
  layout?: ReportLayout,
): ReportResult {
  const byName = new Map(
    columns.map((column) => [column.columnName.toLowerCase(), column]),
  )
  return {
    ...result,
    columns: result.columns.map((column, index) => {
      const layoutColumn = layout?.columns[index]
      if (layoutColumn?.alias?.trim()) {
        return { ...column, label: layoutColumn.alias.trim() }
      }
      const field = (layoutColumn?.field ?? column.name).toLowerCase()
      const meta = byName.get(field) ?? byName.get(column.name.toLowerCase())
      const label = localizedText(
        lang,
        meta?.displayName || column.displayName || column.label,
        meta?.displayNameArabic || column.displayNameArabic,
      )
      return {
        ...column,
        label:
          isLookupControl(field) || isLookupControl(column.name)
            ? withoutControlSuffix(label)
            : label,
      }
    }),
  }
}

export function columnVisibleInLang(
  column: { locale?: string | null } | null | undefined,
  lang: Lang,
) {
  const locale = (column?.locale ?? 'default').trim().toLowerCase()
  if (!locale || locale === 'default' || locale === 'all') {
    return true
  }
  if (locale === 'en' || locale === 'english') {
    return lang === 'en'
  }
  if (locale === 'ar' || locale === 'arabic') {
    return lang === 'ar'
  }
  return true
}

export function resultForLang(
  result: ReportResult,
  layout: ReportLayout | undefined,
  lang: Lang,
): ReportResult {
  return {
    ...result,
    columns: result.columns.filter((column, index) => {
      if (column.hidden) {
        return true
      }
      return columnVisibleInLang(layout?.columns[index], lang)
    }),
  }
}

export function layoutForLang(layout: ReportLayout, lang: Lang): ReportLayout {
  return {
    ...layout,
    columns: layout.columns.filter((column) => columnVisibleInLang(column, lang)),
  }
}
