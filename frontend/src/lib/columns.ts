import type { DatasetColumn, SortDirection } from '../api/types'

export function defaultSortDirection(
  column: Pick<DatasetColumn, 'defaultSortDirection'>,
): SortDirection {
  return column.defaultSortDirection === 'asc' ? 'asc' : 'desc'
}

export function columnForField(columns: DatasetColumn[], field: string) {
  const lower = field.toLowerCase()
  return columns.find((column) => column.columnName.toLowerCase() === lower)
}
