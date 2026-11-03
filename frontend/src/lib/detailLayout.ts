import type { LayoutFilter, MasterDetail, ReportLayout } from '../api/types'

export function isDetailConfigured(
  detail: MasterDetail | null | undefined,
): detail is MasterDetail & { datasetId: string; parentField: string; childField: string } {
  return Boolean(
    detail?.enabled &&
      detail.datasetId &&
      detail.parentField?.trim() &&
      detail.childField?.trim() &&
      detail.columns.length > 0,
  )
}

export function detailQueryLayout(detail: MasterDetail, parentValue: string): ReportLayout {
  const child = detail.childField?.trim() ?? ''
  const filters: LayoutFilter[] = (detail.filters ?? []).filter(
    (filter) => filter.field.toLowerCase() !== child.toLowerCase(),
  )
  filters.unshift({
    field: child,
    operator: 'eq',
    value: parentValue,
    isFixed: true,
    isHidden: true,
  })
  return {
    kind: 'table',
    columns: detail.columns,
    groupBy: [],
    filters,
    orderBy: detail.orderBy ?? [],
  }
}
