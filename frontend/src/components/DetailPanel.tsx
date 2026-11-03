import type { Dataset, DatasetColumn, MasterDetail } from '../api/types'
import { Field } from './AdminFields'
import { columnLabel, localizedText } from '../i18n/localized'
import { useLocale } from '../i18n/LocaleProvider'

type Props = {
  masterColumns: DatasetColumn[]
  datasets: Dataset[]
  detail: MasterDetail | null | undefined
  disabled?: boolean
  onChange: (detail: MasterDetail | null) => void
}

function blankDetail(parentField = ''): MasterDetail {
  return {
    enabled: true,
    datasetId: '',
    parentField,
    childField: '',
    columns: [],
    filters: [],
    orderBy: [],
  }
}

export function DetailPanel({ masterColumns, datasets, detail, disabled = false, onChange }: Props) {
  const { lang, t } = useLocale()
  const enabled = Boolean(detail?.enabled)
  const dataset = datasets.find((item) => item.id === detail?.datasetId)
  const childColumns = (dataset?.columns ?? [])
    .filter((column) => !column.isHidden)
    .slice()
    .sort((a, b) => a.ordinal - b.ordinal)
  const selected = new Set((detail?.columns ?? []).map((column) => column.field.toLowerCase()))
  const sortField = detail?.orderBy[0]?.field ?? ''
  const sortDirection = detail?.orderBy[0]?.direction ?? 'asc'

  const update = (next: MasterDetail | null) => onChange(next)

  return (
    <section className="access-card user-scope-card">
      <div>
        <h3>{t('detailSection')}</h3>
        <p className="muted small">{t('detailHint')}</p>
      </div>
      <label className="filter-lock user-scope-toggle">
        <input
          type="checkbox"
          checked={enabled}
          disabled={disabled}
          onChange={(event) => {
            if (!event.target.checked) {
              update(null)
              return
            }
            update(detail?.enabled ? detail : blankDetail(detail?.parentField ?? ''))
          }}
        />
        {t('detailEnabled')}
      </label>
      {enabled && detail && (
        <div className="user-scope-fields">
          <Field label={t('detailDataset')}>
            <select
              value={detail.datasetId ?? ''}
              disabled={disabled}
              onChange={(event) =>
                update({
                  ...blankDetail(detail.parentField ?? ''),
                  datasetId: event.target.value,
                })
              }
            >
              <option value="">{t('detailPickDataset')}</option>
              {datasets.map((item) => (
                <option key={item.id} value={item.id}>
                  {localizedText(lang, item.displayName, item.displayNameArabic)}
                </option>
              ))}
            </select>
          </Field>
          <Field label={t('detailParentField')}>
            <select
              value={detail.parentField ?? ''}
              disabled={disabled || masterColumns.length === 0}
              onChange={(event) => update({ ...detail, parentField: event.target.value })}
            >
              <option value="">{t('detailPickColumn')}</option>
              {masterColumns.map((column) => (
                <option key={column.columnName} value={column.columnName}>
                  {columnLabel(lang, column)}
                </option>
              ))}
            </select>
          </Field>
          {masterColumns.length === 0 && <p className="muted small">{t('detailParentMissing')}</p>}
          <Field label={t('detailChildField')}>
            <select
              value={detail.childField ?? ''}
              disabled={disabled || !dataset}
              onChange={(event) => update({ ...detail, childField: event.target.value })}
            >
              <option value="">{t('detailPickColumn')}</option>
              {childColumns.map((column) => (
                <option key={column.columnName} value={column.columnName}>
                  {columnLabel(lang, column)}
                </option>
              ))}
            </select>
          </Field>
          <div>
            <div className="schedule-people-title">{t('detailColumns')}</div>
            <div className="detail-column-list">
              {childColumns.map((column) => {
                const checked = selected.has(column.columnName.toLowerCase())
                return (
                  <label key={column.columnName} className="check-row">
                    <input
                      type="checkbox"
                      checked={checked}
                      disabled={disabled}
                      onChange={() => {
                        const columns = checked
                          ? detail.columns.filter(
                              (item) => item.field.toLowerCase() !== column.columnName.toLowerCase(),
                            )
                          : [...detail.columns, { field: column.columnName }]
                        const orderBy =
                          sortField.toLowerCase() === column.columnName.toLowerCase() && checked
                            ? []
                            : detail.orderBy
                        update({ ...detail, columns, orderBy })
                      }}
                    />
                    <span>{columnLabel(lang, column)}</span>
                  </label>
                )
              })}
              {dataset && childColumns.length === 0 && (
                <p className="muted small">{t('detailNoColumns')}</p>
              )}
            </div>
          </div>
          <Field label={t('detailSort')}>
            <select
              value={sortField}
              disabled={disabled || detail.columns.length === 0}
              onChange={(event) => {
                const field = event.target.value
                update({
                  ...detail,
                  orderBy: field ? [{ field, direction: sortDirection }] : [],
                })
              }}
            >
              <option value="">{t('detailSortNone')}</option>
              {detail.columns.map((column) => {
                const meta = childColumns.find(
                  (item) => item.columnName.toLowerCase() === column.field.toLowerCase(),
                )
                return (
                  <option key={column.field} value={column.field}>
                    {meta ? columnLabel(lang, meta) : column.field}
                  </option>
                )
              })}
            </select>
          </Field>
          {sortField && (
            <Field label={t('detailSortDirection')}>
              <select
                value={sortDirection}
                disabled={disabled}
                onChange={(event) =>
                  update({
                    ...detail,
                    orderBy: [{ field: sortField, direction: event.target.value === 'desc' ? 'desc' : 'asc' }],
                  })
                }
              >
                <option value="asc">{t('ascending')}</option>
                <option value="desc">{t('descending')}</option>
              </select>
            </Field>
          )}
        </div>
      )}
    </section>
  )
}
