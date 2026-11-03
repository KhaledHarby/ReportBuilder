import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type ReactElement } from 'react'
import { api } from '../api/client'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthProvider'
import { LangToggle } from '../i18n/LangToggle'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'
import { routeTitle, useRouteTitle } from '../lib/pageTitle'
import { readSidebarMode, writeSidebarMode, type SidebarMode } from '../lib/sidebarPref'
import {
  IconBookmark,
  IconChart,
  IconChevron,
  IconColumns,
  IconEyeOff,
  IconGrid,
  IconInfo,
  IconKey,
  IconLayers,
  IconList,
  IconMail,
  IconMenu,
  IconPin,
  IconShield,
  IconSettings,
  IconSpark,
  IconUsers,
} from './Icons'
import { ChangePasswordDialog } from './ChangePasswordDialog'
import { MyAccessDialog } from './MyAccessDialog'
import { ThemePicker } from './ThemePicker'

export function AppLayout() {
  const { user, can, logout } = useAuth()
  const { lang, t } = useLocale()
  const branding = useQuery({ queryKey: ['branding'], queryFn: api.getBranding, staleTime: 60_000 })
  const appName = localizedText(
    lang,
    branding.data?.applicationName || t('brandName'),
    branding.data?.applicationNameArabic,
  )
  const studioName = localizedText(
    lang,
    branding.data?.studioName || t('brandStudio'),
    branding.data?.studioNameArabic,
  )
  const logoUrl = branding.data?.hasLogo ? api.brandingLogoUrl(branding.data.logoVersion) : null
  const location = useLocation()
  useRouteTitle(routeTitle(location.pathname, t))
  const [accessOpen, setAccessOpen] = useState(false)
  const [passwordOpen, setPasswordOpen] = useState(false)
  const [mode, setMode] = useState<SidebarMode>(readSidebarMode)
  const [lastDock, setLastDock] = useState<'pinned' | 'collapsed'>(() =>
    readSidebarMode() === 'collapsed' ? 'collapsed' : 'pinned',
  )

  const collapsed = mode === 'collapsed'
  const hidden = mode === 'hidden'
  const compact = collapsed || hidden

  function setSidebar(next: SidebarMode) {
    setMode(next)
    writeSidebarMode(next)
    if (next !== 'hidden') {
      setLastDock(next)
    }
  }

  const catalogActive =
    location.pathname === '/catalog' || location.pathname.startsWith('/catalog/')
  const [catalogOpen, setCatalogOpen] = useState(catalogActive)

  useEffect(() => {
    if (catalogActive) setCatalogOpen(true)
  }, [catalogActive])

  type NavLinkItem = { kind: 'link'; to: string; end?: boolean; icon: ReactElement; label: string }
  type NavGroupItem = {
    kind: 'group'
    id: string
    icon: ReactElement
    label: string
    open: boolean
    current: boolean
    onToggle: () => void
    children: { to: string; end?: boolean; icon: ReactElement; label: string }[]
  }

  const items = [
    can('reports.view') && { kind: 'link' as const, to: '/', end: true, icon: <IconGrid />, label: t('navReports') },
    can('analytics.view') && { kind: 'link' as const, to: '/analytics', icon: <IconSpark />, label: t('navAnalytics') },
    (can('datasets.view') || can('lookups.view')) && {
      kind: 'group' as const,
      id: 'catalog',
      icon: <IconLayers />,
      label: t('navCatalog'),
      open: catalogOpen,
      current: catalogActive,
      onToggle: () => setCatalogOpen((open) => !open),
      children: [
        can('datasets.view') && {
          to: '/catalog',
          end: true,
          icon: <IconColumns />,
          label: t('navCatalogDatasets'),
        },
        can('lookups.view') && {
          to: '/catalog/lookups',
          icon: <IconBookmark />,
          label: t('navCatalogLookups'),
        },
      ].filter((item): item is { to: string; end?: boolean; icon: ReactElement; label: string } => Boolean(item)),
    },
    can('reports.delivery') && { kind: 'link' as const, to: '/delivery', icon: <IconMail />, label: t('navDelivery') },
    can('users.view') && { kind: 'link' as const, to: '/users', icon: <IconUsers />, label: t('navUsers') },
    can('roles.view') && { kind: 'link' as const, to: '/roles', icon: <IconShield />, label: t('navRoles') },
    can('permissions.view') && { kind: 'link' as const, to: '/permissions', icon: <IconKey />, label: t('navPermissions') },
    can('logs.view') && { kind: 'link' as const, to: '/activity', icon: <IconList />, label: t('navActivity') },
    can('insights.view') && { kind: 'link' as const, to: '/insights', icon: <IconChart />, label: t('navInsights') },
    can('settings.view') && { kind: 'link' as const, to: '/settings', icon: <IconSettings />, label: t('navSettings') },
  ].filter((item): item is NavLinkItem | NavGroupItem => Boolean(item))

  return (
    <div className={`app-shell${collapsed ? ' is-collapsed' : ''}${hidden ? ' is-hidden' : ''}`}>
      {hidden && (
        <button
          type="button"
          className="sidebar-reveal"
          aria-label={t('navShow')}
          title={t('navShow')}
          onClick={() => setSidebar(lastDock)}
        >
          <IconMenu />
        </button>
      )}
      <aside className={`sidebar${collapsed ? ' is-collapsed' : ''}`} hidden={hidden}>
        <div className="brand">
          {logoUrl ? (
            <img className="brand-logo" src={logoUrl} alt="" />
          ) : (
            <span className="brand-mark">{appName.trim().charAt(0) || 'M'}</span>
          )}
          <div className="brand-copy">
            <strong>{appName}</strong>
            <small>{studioName}</small>
          </div>
          <div className="sidebar-dock">
            <button
              type="button"
              className={`icon-btn${mode === 'pinned' ? ' is-on' : ''}`}
              aria-pressed={mode === 'pinned'}
              aria-label={mode === 'pinned' ? t('navUnpin') : t('navPin')}
              title={mode === 'pinned' ? t('navUnpin') : t('navPin')}
              onClick={() => setSidebar(mode === 'pinned' ? 'collapsed' : 'pinned')}
            >
              <IconPin />
            </button>
            <button
              type="button"
              className="icon-btn"
              aria-label={t('navHide')}
              title={t('navHide')}
              onClick={() => setSidebar('hidden')}
            >
              <IconEyeOff />
            </button>
          </div>
        </div>
        <nav>
          {items.map((item) =>
            item.kind === 'link' ? (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                title={item.label}
                aria-label={item.label}
              >
                {item.icon}
                <span className="sidebar-label">{item.label}</span>
              </NavLink>
            ) : (
              <div
                key={item.id}
                className={`nav-group${item.open ? ' is-open' : ''}${item.current ? ' is-current' : ''}`}
              >
                <button
                  type="button"
                  className="nav-group-toggle"
                  aria-expanded={item.open}
                  title={item.label}
                  aria-label={item.label}
                  onClick={item.onToggle}
                >
                  {item.icon}
                  <span className="sidebar-label">{item.label}</span>
                  <IconChevron className="nav-chevron" />
                </button>
                {item.open && (
                  <div className="nav-sub">
                    {item.children.map((child) => (
                      <NavLink
                        key={child.to}
                        to={child.to}
                        end={child.end}
                        title={child.label}
                        aria-label={child.label}
                      >
                        {child.icon}
                        <span className="sidebar-label">{child.label}</span>
                      </NavLink>
                    ))}
                  </div>
                )}
              </div>
            ),
          )}
        </nav>
        <div className="sidebar-foot">
          {user && (
            <div className="sidebar-user">
              <div className="sidebar-user-head">
                <div className="sidebar-user-copy">
                  <strong>{localizedText(lang, user.displayName, user.displayNameArabic)}</strong>
                  <small>{user.userName}</small>
                </div>
                <button
                  type="button"
                  className="icon-btn"
                  aria-label={t('myAccess')}
                  title={t('myAccess')}
                  onClick={() => setAccessOpen(true)}
                >
                  <IconInfo />
                </button>
              </div>
              <div className="sidebar-user-actions">
                <button type="button" className="link" onClick={() => setPasswordOpen(true)}>
                  {t('changePassword')}
                </button>
                <button type="button" className="link" onClick={() => void logout()}>
                  {t('signOut')}
                </button>
              </div>
            </div>
          )}
          <LangToggle />
          <ThemePicker compact={compact} />
        </div>
      </aside>
      <main className="main">
        <Outlet />
      </main>
      <MyAccessDialog open={accessOpen} onClose={() => setAccessOpen(false)} />
      <ChangePasswordDialog open={passwordOpen} onClose={() => setPasswordOpen(false)} />
    </div>
  )
}
