import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { UserGrants } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'
import type { Lang } from '../i18n/locale'
import {
  IconGrid,
  IconKey,
  IconLayers,
  IconShield,
} from './Icons'
import { Spinner } from './Spinner'
import { datasetTechnicalName } from '../lib/datasets'

type Props = {
  open: boolean
  onClose: () => void
}

type GrantTab = 'all' | 'reports' | 'datasets' | 'roles' | 'permissions'

function initials(name: string) {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length >= 2) {
    return `${parts[0][0] ?? ''}${parts[1][0] ?? ''}`.toUpperCase()
  }
  return (name.trim().slice(0, 2) || '?').toUpperCase()
}

function matches(query: string, ...values: Array<string | null | undefined>) {
  const q = query.trim().toLowerCase()
  if (!q) {
    return true
  }
  return values.some((value) => (value ?? '').toLowerCase().includes(q))
}

export function MyAccessDialog({ open, onClose }: Props) {
  const { t, lang } = useLocale()
  const { can, user } = useAuth()
  const searchRef = useRef<HTMLInputElement>(null)
  const [query, setQuery] = useState('')
  const [tab, setTab] = useState<GrantTab>('all')
  const grants = useQuery({
    queryKey: ['my-grants'],
    queryFn: api.listMyGrants,
    enabled: open,
  })

  useEffect(() => {
    if (!open) {
      setQuery('')
      setTab('all')
      return
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', onKey)
    const focus = window.setTimeout(() => searchRef.current?.focus(), 40)
    return () => {
      window.removeEventListener('keydown', onKey)
      window.clearTimeout(focus)
    }
  }, [open, onClose])

  const filtered = useMemo(() => filterGrants(grants.data, query, lang), [grants.data, query, lang])
  const visible = {
    reports: tab === 'all' || tab === 'reports',
    datasets: tab === 'all' || tab === 'datasets',
    roles: tab === 'all' || tab === 'roles',
    permissions: tab === 'all' || tab === 'permissions',
  }
  const shownCount =
    (visible.reports ? filtered.reports.length : 0) +
    (visible.datasets ? filtered.datasets.length : 0) +
    (visible.roles ? filtered.roles.length : 0) +
    (visible.permissions ? filtered.permissions.length : 0)
  const emptyCopy = query.trim()
    ? t('noMatchingAccess')
    : tab === 'reports'
      ? t('noGrantedReports')
      : tab === 'datasets'
        ? t('noGrantedDatasets')
        : tab === 'roles'
          ? t('noGrantedRoles')
          : tab === 'permissions'
            ? t('noGrantedPermissions')
            : t('myAccessHint')

  if (!open) {
    return null
  }

  const tabs: { id: GrantTab; label: string; count: number }[] = [
    {
      id: 'all',
      label: t('grantAll'),
      count:
        filtered.reports.length +
        filtered.datasets.length +
        filtered.roles.length +
        filtered.permissions.length,
    },
    { id: 'reports', label: t('grantReports'), count: filtered.reports.length },
    { id: 'datasets', label: t('grantDatasets'), count: filtered.datasets.length },
    { id: 'roles', label: t('grantRoles'), count: filtered.roles.length },
    { id: 'permissions', label: t('grantPermissions'), count: filtered.permissions.length },
  ]

  return (
    <div className="grants-overlay" onClick={onClose} role="presentation">
      <section
        className="grants-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="grants-title"
        onClick={(event) => event.stopPropagation()}
      >
        <header className="grants-chrome">
          <div className="grants-head">
            <div className="grants-identity">
              <span className="access-avatar grants-avatar" aria-hidden="true">
                {initials(
                  localizedText(lang, user?.displayName, user?.displayNameArabic) ||
                    user?.userName ||
                    '?',
                )}
              </span>
              <div className="grants-copy">
                <div className="kicker">{t('myAccess')}</div>
                <h2 id="grants-title">
                  {localizedText(lang, user?.displayName, user?.displayNameArabic)}
                </h2>
                <p className="muted small grants-user">
                  {user?.userName}
                  {user?.email ? ` · ${user.email}` : ''}
                </p>
              </div>
            </div>
            <div className="grants-head-end">
              {user?.externalSource && (
                <span
                  className="badge published grants-source"
                  title={t('fromSource', { source: user.externalSource })}
                >
                  {t('fromSource', { source: user.externalSource })}
                </span>
              )}
              <button
                type="button"
                className="icon-btn"
                aria-label={t('close')}
                onClick={onClose}
              >
                <span aria-hidden="true">×</span>
              </button>
            </div>
          </div>
          <input
            ref={searchRef}
            className="search grants-search"
            placeholder={t('searchAccess')}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
          <div className="grants-tabs" role="tablist" aria-label={t('myAccess')}>
            {tabs.map((item) => (
              <button
                key={item.id}
                type="button"
                role="tab"
                aria-selected={tab === item.id}
                className={tab === item.id ? 'active' : ''}
                onClick={() => setTab(item.id)}
              >
                {item.label}
                <span>{item.count}</span>
              </button>
            ))}
          </div>
        </header>

        <div className="grants-body">
          {grants.isLoading && <Spinner page label={t('loadingApp')} />}
          {grants.isError && (
            <div className="banner error">{(grants.error as Error).message}</div>
          )}
          {!grants.isLoading && !grants.isError && shownCount === 0 && (
            <div className="grants-empty">
              <p>{emptyCopy}</p>
            </div>
          )}
          {shownCount > 0 && (
            <div className="grants-sections">
              {visible.reports && filtered.reports.length > 0 && (
                <GrantSection
                  icon={<IconGrid />}
                  title={t('grantReports')}
                  count={filtered.reports.length}
                >
                  {filtered.reports.map((report) => {
                    const label = localizedText(lang, report.name, report.nameArabic)
                    return (
                      <GrantRow
                        key={report.id}
                        to={can('reports.view') ? `/reports/${report.id}/view` : undefined}
                        icon={<IconGrid />}
                        title={label}
                        subtitle={localizedText(
                          lang,
                          report.datasetName,
                          report.datasetNameArabic,
                        )}
                        badge={
                          report.status === 'published' ? t('published') : t('draft')
                        }
                        badgeClass={
                          report.status === 'published' ? 'published' : 'draft'
                        }
                        onNavigate={onClose}
                      />
                    )
                  })}
                </GrantSection>
              )}
              {visible.datasets && filtered.datasets.length > 0 && (
                <GrantSection
                  icon={<IconLayers />}
                  title={t('grantDatasets')}
                  count={filtered.datasets.length}
                >
                  {filtered.datasets.map((dataset) => (
                    <GrantRow
                      key={dataset.id}
                      to={can('datasets.view') ? '/catalog' : undefined}
                      icon={<IconLayers />}
                      title={localizedText(
                        lang,
                        dataset.displayName,
                        dataset.displayNameArabic,
                      )}
                      subtitle={datasetTechnicalName(dataset, t('datasetQuerySource'))}
                      badge={
                        dataset.isRestricted ? t('selectedPeople') : t('everyone')
                      }
                      badgeClass={dataset.isRestricted ? 'admin' : 'published'}
                      onNavigate={onClose}
                    />
                  ))}
                </GrantSection>
              )}
              {visible.roles && filtered.roles.length > 0 && (
                <GrantSection
                  icon={<IconShield />}
                  title={t('grantRoles')}
                  count={filtered.roles.length}
                >
                  {filtered.roles.map((role) => (
                    <GrantRow
                      key={role.name}
                      icon={<IconShield />}
                      title={role.name}
                      subtitle={role.description ?? undefined}
                    />
                  ))}
                </GrantSection>
              )}
              {visible.permissions && filtered.permissions.length > 0 && (
                <GrantSection
                  icon={<IconKey />}
                  title={t('grantPermissions')}
                  count={filtered.permissions.length}
                >
                  {filtered.permissions.map((permission) => (
                    <GrantRow
                      key={permission.code}
                      icon={<IconKey />}
                      title={permission.name}
                      subtitle={
                        permission.description
                          ? `${permission.code} · ${permission.description}`
                          : permission.code
                      }
                    />
                  ))}
                </GrantSection>
              )}
            </div>
          )}
        </div>
      </section>
    </div>
  )
}

function filterGrants(data: UserGrants | undefined, query: string, lang: Lang) {
  if (!data) {
    return {
      reports: [],
      datasets: [],
      roles: [],
      permissions: [],
    }
  }
  return {
    reports: data.reports.filter((report) =>
      matches(
        query,
        localizedText(lang, report.name, report.nameArabic),
        report.name,
        report.nameArabic,
        localizedText(lang, report.datasetName, report.datasetNameArabic),
        report.datasetName,
        report.status,
      ),
    ),
    datasets: data.datasets.filter((dataset) =>
      matches(
        query,
        localizedText(lang, dataset.displayName, dataset.displayNameArabic),
        dataset.displayName,
        dataset.displayNameArabic,
        dataset.schemaName,
        dataset.objectName,
        datasetTechnicalName(dataset, 'sql'),
      ),
    ),
    roles: data.roles.filter((role) => matches(query, role.name, role.description)),
    permissions: data.permissions.filter((permission) =>
      matches(query, permission.name, permission.code, permission.description),
    ),
  }
}

function GrantSection({
  icon,
  title,
  count,
  children,
}: {
  icon: ReactNode
  title: string
  count: number
  children: ReactNode
}) {
  return (
    <section className="grants-section">
      <h3>
        <span>
          {icon}
          {title}
        </span>
        <span className="muted small">{count}</span>
      </h3>
      <ul className="grants-list">{children}</ul>
    </section>
  )
}

function GrantRow({
  to,
  icon,
  title,
  subtitle,
  badge,
  badgeClass,
  onNavigate,
}: {
  to?: string
  icon: ReactNode
  title: string
  subtitle?: string
  badge?: string
  badgeClass?: string
  onNavigate?: () => void
}) {
  const copy = (
    <>
      <span className="grants-row-icon" aria-hidden="true">
        {icon}
      </span>
      <span className="grants-row-copy">
        <b>{title}</b>
        {subtitle && <span className="muted small">{subtitle}</span>}
      </span>
      {badge && <span className={`badge ${badgeClass ?? ''}`}>{badge}</span>}
    </>
  )
  return (
    <li className="grants-item">
      {to ? (
        <Link to={to} onClick={onNavigate}>
          {copy}
        </Link>
      ) : (
        <div>{copy}</div>
      )}
    </li>
  )
}
