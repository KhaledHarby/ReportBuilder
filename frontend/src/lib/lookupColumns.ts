import type { DatasetColumn } from '../api/types'

export const lookupControl = 'control'

export function lookupField(columnId: string, lookupColumn: string) {
  return `lookup:${columnId}:${lookupColumn}`
}

export function lookupControlField(columnId: string) {
  return lookupField(columnId, lookupControl)
}

export function isLookupControl(field: string) {
  return isLookupField(field) && field.toLowerCase().endsWith(`:${lookupControl}`)
}

export function isLookupField(field: string) {
  return field.toLowerCase().startsWith('lookup:')
}

export function withLookupColumns(columns: DatasetColumn[]) {
  const expanded: DatasetColumn[] = []
  for (const column of columns) {
    expanded.push(column)
    if ((column.lookupColumns ?? []).some((item) => item.name?.trim())) {
      expanded.push({
        id: `${column.id}:${lookupControl}`,
        columnName: lookupControlField(column.id),
        sqlType: 'nvarchar',
        displayName: column.displayName,
        displayNameArabic: column.displayNameArabic,
        isHidden: false,
        isFilterable: true,
        isGroupable: false,
        isIndexed: false,
        defaultSortDirection: 'asc',
        ordinal: column.ordinal,
      })
    }
  }
  return expanded
}

export function qualifyLookupLabels(columns: DatasetColumn[]) {
  return columns.map((column) => {
    if (!isLookupField(column.columnName) || isLookupControl(column.columnName)) {
      return column
    }
    const factId = column.columnName.split(':')[1]
    const fact = columns.find((item) => item.id === factId)
    if (!fact) {
      return column
    }
    const factArabic = fact.displayNameArabic?.trim() || fact.displayName
    const labelArabic = column.displayNameArabic?.trim() || column.displayName
    return {
      ...column,
      displayName: `${fact.displayName} · ${column.displayName}`,
      displayNameArabic: `${factArabic} · ${labelArabic}`,
    }
  })
}
