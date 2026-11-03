import type { DatasetColumn, ReportLayout, UserScope, UserScopeProperty } from '../api/types'
import { Field } from './AdminFields'
import {
  defaultPropertyForSqlType,
  filterableColumns,
  propertiesForSqlType,
  userScopePropertyLabelKey,
} from '../lib/userScope'
import { columnLabel } from '../i18n/localized'
import { useLocale } from '../i18n/LocaleProvider'

type Props = {
  layout: ReportLayout
  palette: DatasetColumn[]
  fieldError?: string
  propertyError?: string
  disabled?: boolean
  onChange: (userScope: UserScope | null) => void
}

function currentScope(layout: ReportLayout): UserScope {
  return {
    enabled: Boolean(layout.userScope?.enabled),
    field: layout.userScope?.field ?? '',
    property: (layout.userScope?.property ?? 'id') as UserScopeProperty,
  }
}

export function UserScopePanel({
  layout,
  palette,
  fieldError,
  propertyError,
  disabled = false,
  onChange,
}: Props) {
  const { lang, t } = useLocale()
  const scope = currentScope(layout)
  const options = filterableColumns(palette)
  const selected = options.find(
    (column) =>
      scope.field &&
      column.columnName.toLowerCase() === scope.field.toLowerCase(),
  )
  const propertyOptions = selected ? propertiesForSqlType(selected.sqlType) : []

  const updateScope = (patch: Partial<UserScope>) => {
    const next = { ...scope, ...patch }
    if (!next.enabled) {
      onChange(null)
      return
    }
    onChange({
      enabled: true,
      field: next.field ?? '',
      property: (next.property ?? 'id') as UserScopeProperty,
    })
  }

  return (
    <section className="access-card user-scope-card">
      <div>
        <h3>{t('userScope')}</h3>
        <p className="muted small">{t('userScopeHint')}</p>
      </div>
      <label className="filter-lock user-scope-toggle">
        <input
          type="checkbox"
          checked={scope.enabled}
          disabled={disabled}
          onChange={(event) => updateScope({ enabled: event.target.checked })}
        />
        {t('userScopeEnabled')}
      </label>
      {scope.enabled && (
        <div className="user-scope-fields">
          <Field label={t('userScopeField')} error={fieldError}>
            <select
              value={scope.field ?? ''}
              disabled={disabled || options.length === 0}
              aria-invalid={Boolean(fieldError)}
              onChange={(event) => {
                const field = event.target.value
                const meta = options.find((column) => column.columnName === field)
                updateScope({
                  field,
                  property: meta ? defaultPropertyForSqlType(meta.sqlType) : 'id',
                })
              }}
            >
              <option value="">{t('userScopePickField')}</option>
              {options.map((column) => (
                <option key={column.id} value={column.columnName}>
                  {columnLabel(lang, column)} ({column.columnName})
                </option>
              ))}
            </select>
          </Field>
          <Field label={t('userScopeProperty')} error={propertyError}>
            <select
              value={scope.property ?? 'id'}
              disabled={disabled || !selected || propertyOptions.length === 0}
              aria-invalid={Boolean(propertyError)}
              onChange={(event) =>
                updateScope({ property: event.target.value as UserScopeProperty })
              }
            >
              {propertyOptions.map((property) => (
                <option key={property} value={property}>
                  {t(userScopePropertyLabelKey(property))}
                </option>
              ))}
            </select>
          </Field>
          {selected && propertyOptions.length === 0 && (
            <p className="field-error" role="alert">
              {t('userScopeUnsupportedColumn')}
            </p>
          )}
          {selected && propertyOptions.length > 0 && (
            <p className="muted small">
              {t('userScopeMatchPreview', {
                column: columnLabel(lang, selected),
                property: t(userScopePropertyLabelKey(scope.property as UserScopeProperty)),
              })}
            </p>
          )}
        </div>
      )}
    </section>
  )
}
