import { useMemo, useState } from 'react'
import type { ReportAccessMode, ReportAccessRole, ReportPerson } from '../api/types'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'

type Props = {
  isRestricted: boolean
  accessUserIds: string[]
  people: ReportPerson[]
  loading?: boolean
  currentUserId?: string | null
  heading?: string
  hint?: string
  accessLabel?: string
  onRestrictedChange: (restricted: boolean) => void
  onAccessUserIdsChange: (ids: string[]) => void
  accessMode?: ReportAccessMode
  onAccessModeChange?: (mode: ReportAccessMode) => void
  roles?: ReportAccessRole[]
  rolesLoading?: boolean
  accessRoleIds?: string[]
  onAccessRoleIdsChange?: (ids: string[]) => void
  disabled?: boolean
}

function initials(name: string) {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length >= 2) {
    return `${parts[0][0] ?? ''}${parts[1][0] ?? ''}`.toUpperCase()
  }
  return (name.trim().slice(0, 2) || '?').toUpperCase()
}

export function ReportAccessPanel({
  isRestricted,
  accessUserIds,
  people,
  loading = false,
  currentUserId,
  heading,
  hint,
  accessLabel,
  onRestrictedChange,
  onAccessUserIdsChange,
  accessMode,
  onAccessModeChange,
  roles = [],
  rolesLoading = false,
  accessRoleIds = [],
  onAccessRoleIdsChange,
  disabled = false,
}: Props) {
  const { lang, t } = useLocale()
  const [query, setQuery] = useState('')
  const [roleQuery, setRoleQuery] = useState('')
  const roleMode = Boolean(onAccessModeChange)
  const mode: ReportAccessMode = roleMode
    ? (accessMode ?? (isRestricted ? 'People' : 'Everyone'))
    : isRestricted
      ? 'People'
      : 'Everyone'
  const selected = useMemo(() => new Set(accessUserIds), [accessUserIds])
  const selectedPeople = useMemo(
    () =>
      accessUserIds
        .map((id) => people.find((person) => person.id === id))
        .filter((person): person is ReportPerson => Boolean(person)),
    [accessUserIds, people],
  )
  const candidates = useMemo(() => {
    const q = query.trim().toLowerCase()
    return people
      .filter((person) => {
        if (person.id === currentUserId) {
          return false
        }
        if (!q) {
          return true
        }
        return (
          person.displayName.toLowerCase().includes(q) ||
          (person.displayNameArabic ?? '').toLowerCase().includes(q) ||
          person.userName.toLowerCase().includes(q)
        )
      })
      .sort((a, b) => {
        const aSelected = selected.has(a.id)
        const bSelected = selected.has(b.id)
        if (aSelected !== bSelected) {
          return aSelected ? -1 : 1
        }
        return localizedText(lang, a.displayName, a.displayNameArabic).localeCompare(
          localizedText(lang, b.displayName, b.displayNameArabic),
        )
      })
  }, [people, query, currentUserId, selected, lang])

  const toggle = (id: string) => {
    const next = new Set(selected)
    if (next.has(id)) {
      next.delete(id)
    } else {
      next.add(id)
    }
    onAccessUserIdsChange([...next])
  }

  const selectedRoleSet = useMemo(() => new Set(accessRoleIds), [accessRoleIds])
  const selectedRoles = useMemo(
    () =>
      accessRoleIds
        .map((id) => roles.find((role) => role.id === id))
        .filter((role): role is ReportAccessRole => Boolean(role)),
    [accessRoleIds, roles],
  )
  const roleCandidates = useMemo(() => {
    const q = roleQuery.trim().toLowerCase()
    return [...roles]
      .filter((role) => {
        if (!q) {
          return true
        }
        return (
          role.name.toLowerCase().includes(q) ||
          (role.description ?? '').toLowerCase().includes(q)
        )
      })
      .sort((a, b) => {
        const aSelected = selectedRoleSet.has(a.id)
        const bSelected = selectedRoleSet.has(b.id)
        if (aSelected !== bSelected) {
          return aSelected ? -1 : 1
        }
        return a.name.localeCompare(b.name)
      })
  }, [roles, roleQuery, selectedRoleSet])

  const toggleRole = (id: string) => {
    if (!onAccessRoleIdsChange) {
      return
    }
    const next = new Set(selectedRoleSet)
    if (next.has(id)) {
      next.delete(id)
    } else {
      next.add(id)
    }
    onAccessRoleIdsChange([...next])
  }

  const chooseMode = (next: ReportAccessMode) => {
    onAccessModeChange?.(next)
    onRestrictedChange(next !== 'Everyone')
  }

  return (
    <section className="access-card">
      <div>
        <div className="kicker">{heading ?? t('whoCanAccess')}</div>
        <p className="muted small">{hint ?? t('accessHint')}</p>
      </div>
      <div className="access-modes" role="tablist" aria-label={accessLabel ?? t('reportAccess')}>
        <button
          type="button"
          role="tab"
          aria-selected={mode === 'Everyone'}
          className={mode === 'Everyone' ? 'active' : ''}
          disabled={disabled}
          onClick={() => (roleMode ? chooseMode('Everyone') : onRestrictedChange(false))}
        >
          {t('everyone')}
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={mode === 'People'}
          className={mode === 'People' ? 'active' : ''}
          disabled={disabled}
          onClick={() => (roleMode ? chooseMode('People') : onRestrictedChange(true))}
        >
          {t('selectedPeople')}
        </button>
        {roleMode && (
          <button
            type="button"
            role="tab"
            aria-selected={mode === 'Roles'}
            className={mode === 'Roles' ? 'active' : ''}
            disabled={disabled}
            onClick={() => chooseMode('Roles')}
          >
            {t('selectedRole')}
          </button>
        )}
      </div>
      {mode === 'Roles' && (
        <div className="access-people">
          <p className="muted small">{t('selectedRoleHint')}</p>
          <div className="access-people-toolbar">
            <span className="muted small">
              {selectedRoles.length
                ? t('selectedCount', { n: selectedRoles.length })
                : t('noneSelectedRolesHint')}
            </span>
            {selectedRoles.length > 0 && (
              <button
                type="button"
                className="link"
                disabled={disabled}
                onClick={() => onAccessRoleIdsChange?.([])}
              >
                {t('clear')}
              </button>
            )}
          </div>
          {selectedRoles.length > 0 && (
            <div className="access-chips" aria-label={t('selectedRole')}>
              {selectedRoles.map((role) => (
                <span key={role.id} className="access-chip">
                  <span className="access-chip-name">{role.name}</span>
                  <button
                    type="button"
                    disabled={disabled}
                    aria-label={t('removeNamed', { name: role.name })}
                    onClick={() => toggleRole(role.id)}
                  >
                    <span aria-hidden="true">×</span>
                  </button>
                </span>
              ))}
            </div>
          )}
          <input
            className="search access-people-search"
            placeholder={t('searchRoles')}
            value={roleQuery}
            disabled={disabled}
            onChange={(event) => setRoleQuery(event.target.value)}
          />
          <div className="access-people-list" role="list">
            {rolesLoading && <p className="muted small">{t('loadingRoles')}</p>}
            {!rolesLoading && roleCandidates.length === 0 && (
              <p className="muted small">{roles.length === 0 ? t('accessNoRoles') : t('noMatchingRoles')}</p>
            )}
            {roleCandidates.map((role) => {
              const checked = selectedRoleSet.has(role.id)
              return (
                <label
                  key={role.id}
                  className={`access-person${checked ? ' checked' : ''}`}
                  role="listitem"
                >
                  <span className="access-person-copy">
                    <b>{role.name}</b>
                    {role.description ? <span className="muted small">{role.description}</span> : null}
                  </span>
                  <input
                    type="checkbox"
                    checked={checked}
                    disabled={disabled}
                    onChange={() => toggleRole(role.id)}
                  />
                </label>
              )
            })}
          </div>
        </div>
      )}
      {mode === 'People' && (
        <div className="access-people">
          <div className="access-people-toolbar">
            <span className="muted small">
              {selectedPeople.length
                ? t('selectedCount', { n: selectedPeople.length })
                : t('noneSelectedHint')}
            </span>
            {selectedPeople.length > 0 && (
              <button
                type="button"
                className="link"
                disabled={disabled}
                onClick={() => onAccessUserIdsChange([])}
              >
                {t('clear')}
              </button>
            )}
          </div>
          {selectedPeople.length > 0 && (
            <div className="access-chips" aria-label={t('selectedPeople')}>
              {selectedPeople.map((person) => {
                const label = localizedText(lang, person.displayName, person.displayNameArabic)
                return (
                <span key={person.id} className="access-chip">
                  <span className="access-avatar" aria-hidden="true">
                    {initials(label || person.userName)}
                  </span>
                  <span className="access-chip-name">{label}</span>
                  <button
                    type="button"
                    disabled={disabled}
                    aria-label={t('removeNamed', { name: label })}
                    onClick={() => toggle(person.id)}
                  >
                    <span aria-hidden="true">×</span>
                  </button>
                </span>
                )
              })}
            </div>
          )}
          <input
            className="search access-people-search"
            placeholder={t('searchPeople')}
            value={query}
            disabled={disabled}
            onChange={(event) => setQuery(event.target.value)}
          />
          <div className="access-people-list" role="list">
            {loading && <p className="muted small">{t('loadingPeople')}</p>}
            {!loading && candidates.length === 0 && (
              <p className="muted small">
                {people.every((person) => person.id === currentUserId)
                  ? t('noOtherUsers')
                  : t('noMatchingPeople')}
              </p>
            )}
            {candidates.map((person) => {
              const checked = selected.has(person.id)
              const label = localizedText(lang, person.displayName, person.displayNameArabic)
              return (
                <label
                  key={person.id}
                  className={`access-person${checked ? ' checked' : ''}`}
                  role="listitem"
                >
                  <span className="access-avatar" aria-hidden="true">
                    {initials(label || person.userName)}
                  </span>
                  <span className="access-person-copy">
                    <b>{label}</b>
                    <span className="muted small">{person.userName}</span>
                  </span>
                  <input
                    type="checkbox"
                    checked={checked}
                    disabled={disabled}
                    onChange={() => toggle(person.id)}
                  />
                </label>
              )
            })}
          </div>
        </div>
      )}
    </section>
  )
}
