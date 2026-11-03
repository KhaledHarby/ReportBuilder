import type { Dataset } from '../api/types'
import { datasetOptionLabel } from '../i18n/localized'
import type { Lang } from '../i18n/locale'

export function isQueryDataset(dataset: { objectType?: string | null }) {
  return (dataset.objectType ?? '').toLowerCase() === 'query'
}

export function datasetTechnicalName(
  dataset: { objectType?: string | null; schemaName: string; objectName: string },
  queryLabel: string,
) {
  return isQueryDataset(dataset) ? queryLabel : `${dataset.schemaName}.${dataset.objectName}`
}

type DatasetSortFields = Pick<
  Dataset,
  'sortOrder' | 'displayName' | 'displayNameArabic' | 'objectName' | 'schemaName'
>

export function compareDatasetViewOrder(
  a: DatasetSortFields,
  b: DatasetSortFields,
  lang: Lang,
) {
  const bySort = (a.sortOrder ?? 0) - (b.sortOrder ?? 0)
  if (bySort !== 0) {
    return bySort
  }
  return datasetOptionLabel(lang, a).localeCompare(
    datasetOptionLabel(lang, b),
    lang === 'ar' ? 'ar' : 'en',
  )
}

export function sortDatasetsForDesigner(datasets: Dataset[], lang: Lang) {
  return [...datasets].sort((a, b) => compareDatasetViewOrder(a, b, lang))
}

export function sortDatasetsForCatalog(datasets: Dataset[]) {
  return [...datasets].sort((a, b) => {
    if (a.isEnabled !== b.isEnabled) {
      return a.isEnabled ? -1 : 1
    }
    const bySort = (a.sortOrder ?? 0) - (b.sortOrder ?? 0)
    if (bySort !== 0) {
      return bySort
    }
    const bySchema = a.schemaName.localeCompare(b.schemaName)
    if (bySchema !== 0) {
      return bySchema
    }
    return a.objectName.localeCompare(b.objectName)
  })
}
