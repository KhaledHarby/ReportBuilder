import { useMemo } from 'react'
import type { Dataset } from '../api/types'
import { useLocale } from '../i18n/LocaleProvider'
import { datasetOptionLabel } from '../i18n/localized'
import { sortDatasetsForDesigner } from '../lib/datasets'

type Props = {
  value: string
  datasets: Dataset[]
  onChange: (datasetId: string) => void
}

export function DatasetPicker({ value, datasets, onChange }: Props) {
  const { lang, t } = useLocale()

  const ordered = useMemo(
    () => sortDatasetsForDesigner(datasets, lang),
    [datasets, lang],
  )

  return (
    <label className="dataset-picker">
      {t('dataset')}
      <select
        className="dataset-picker-select"
        value={value}
        onChange={(change) => onChange(change.target.value)}
      >
        <option value="">{t('selectTableOrView')}</option>
        {ordered.map((item) => (
          <option key={item.id} value={item.id}>
            {datasetOptionLabel(lang, item)}
          </option>
        ))}
      </select>
    </label>
  )
}
