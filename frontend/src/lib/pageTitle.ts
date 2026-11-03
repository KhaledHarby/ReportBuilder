import { useQuery } from '@tanstack/react-query'
import { useEffect, useLayoutEffect } from 'react'
import { api } from '../api/client'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'

function applyTitle(page: string | null | undefined, studio: string) {
  const name = page?.trim()
  if (!name) return
  document.title = `${name} · ${studio}`
}

function useStudioName() {
  const { lang, t } = useLocale()
  const branding = useQuery({ queryKey: ['branding'], queryFn: api.getBranding, staleTime: 60_000 })
  const configured = branding.data
    ? localizedText(lang, branding.data.studioName, branding.data.studioNameArabic)
    : ''
  return configured || t('brandStudio')
}

export function usePageTitle(page?: string | null) {
  const studio = useStudioName()
  useEffect(() => {
    applyTitle(page, studio)
  }, [page, studio])
}

/** Route title runs before a page title, so a report name replaces it. */
export function useRouteTitle(page?: string | null) {
  const studio = useStudioName()
  useLayoutEffect(() => {
    applyTitle(page, studio)
  }, [page, studio])
}

export function routeTitle(
  pathname: string,
  t: (key: 'navReports' | 'navDelivery' | 'navCatalogDatasets' | 'navCatalogLookups' | 'newReport' | 'navUsers' | 'navRoles' | 'navPermissions' | 'navActivity' | 'navInsights' | 'navAnalytics' | 'analyticsNew' | 'navSettings' | 'brandStudio') => string,
) {
  if (pathname === '/' || pathname === '') return t('navReports')
  if (pathname.startsWith('/delivery')) return t('navDelivery')
  if (pathname.startsWith('/catalog/lookups')) return t('navCatalogLookups')
  if (pathname.startsWith('/catalog')) return t('navCatalogDatasets')
  if (pathname === '/reports/new') return t('newReport')
  if (pathname.startsWith('/reports/')) return t('navReports')
  if (pathname.startsWith('/users')) return t('navUsers')
  if (pathname.startsWith('/roles')) return t('navRoles')
  if (pathname.startsWith('/permissions')) return t('navPermissions')
  if (pathname.startsWith('/activity')) return t('navActivity')
  if (pathname.startsWith('/insights')) return t('navInsights')
  if (pathname === '/analytics/new') return t('analyticsNew')
  if (pathname.startsWith('/analytics')) return t('navAnalytics')
  if (pathname.startsWith('/settings')) return t('navSettings')
  return t('brandStudio')
}
