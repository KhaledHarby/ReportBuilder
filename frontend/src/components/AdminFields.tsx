import type { ReactNode } from 'react'
import type { PermissionRecord } from '../api/types'
import { useLocale } from '../i18n/LocaleProvider'
import { localeFor } from '../i18n/locale'

export function Field({
  label,
  error,
  children,
}: {
  label: string
  error?: string
  children: ReactNode
}) {
  return (
    <label className={`field${error ? ' has-error' : ''}`}>
      {label}
      {children}
      {error ? (
        <span className="field-error" role="alert">
          {error}
        </span>
      ) : null}
    </label>
  )
}

export function AuditLine({
  createdByName,
  createdAt,
  modifiedByName,
  modifiedAt,
}: {
  createdByName?: string | null
  createdAt: string
  modifiedByName?: string | null
  modifiedAt: string
}) {
  const { lang, t } = useLocale()
  return (
    <p className="muted small audit-line">
      {t('createdAt', { when: formatWhen(createdAt, lang) })}
      {createdByName ? t('createdBy', { name: createdByName }) : ''}
      {' · '}
      {t('modifiedAt', { when: formatWhen(modifiedAt, lang) })}
      {modifiedByName ? t('modifiedBy', { name: modifiedByName }) : ''}
    </p>
  )
}

function formatWhen(value: string, lang: 'en' | 'ar') {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }
  return date.toLocaleString(localeFor(lang))
}

export function PermissionPicker({
  permissions,
  selected,
  onChange,
  disabled,
}: {
  permissions: PermissionRecord[]
  selected: string[]
  onChange: (ids: string[]) => void
  disabled?: boolean
}) {
  const groups = groupPermissions(permissions)

  function toggle(id: string) {
    if (disabled) {
      return
    }
    onChange(
      selected.includes(id)
        ? selected.filter((item) => item !== id)
        : [...selected, id],
    )
  }

  return (
    <div className="perm-groups">
      {groups.map((group) => (
        <section key={group.key} className="perm-group">
          <h3>{group.key}</h3>
          <div className="check-grid">
            {group.items.map((permission) => (
              <label key={permission.id} className="check-item">
                <input
                  type="checkbox"
                  checked={selected.includes(permission.id)}
                  disabled={disabled}
                  onChange={() => toggle(permission.id)}
                />
                <span>
                  <b>{permission.name}</b>
                  <code>{permission.code}</code>
                </span>
              </label>
            ))}
          </div>
        </section>
      ))}
    </div>
  )
}

function groupPermissions(permissions: PermissionRecord[]) {
  const groups = new Map<string, PermissionRecord[]>()
  for (const permission of permissions) {
    const key = permission.code.split('.')[0] || 'other'
    const list = groups.get(key) ?? []
    list.push(permission)
    groups.set(key, list)
  }
  return [...groups.entries()].map(([key, items]) => ({ key, items }))
}
