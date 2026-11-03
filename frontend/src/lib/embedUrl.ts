import type { LayoutFilter, UserAccount } from '../api/types'
import { needsFilterValue } from './filters'
import { resolveAppHref } from './reportLinks'

export type EmbedParameter = {
  name: string
  value: string
}

export type EmbedUrlDetails = {
  url: string
  path: string
  query: EmbedParameter[]
  launch: EmbedParameter[]
}

export function buildEmbedUrl(
  reportId: string,
  filters: LayoutFilter[],
  lang: string,
  user: UserAccount | null,
  origin = typeof window === 'undefined' ? '' : window.location.origin,
): EmbedUrlDetails {
  const query: EmbedParameter[] = [{ name: 'lang', value: lang === 'ar' ? 'ar' : 'en' }]
  for (const filter of filters) {
    const name = filter.field.trim()
    if (!name || !needsFilterValue(filter.operator)) {
      continue
    }
    query.push({ name, value: (filter.value ?? '').trim() })
  }

  const search = new URLSearchParams()
  for (const item of query) {
    if (item.name === 'lang' || item.value) {
      search.append(item.name, item.value)
    }
  }
  const path = `/embed/reports/${reportId}?${search.toString()}`
  const url = `${origin}${resolveAppHref(path)}`

  const launch: EmbedParameter[] = [
    { name: 'userName', value: user?.userName ?? '' },
    { name: 'externalId', value: user?.id ?? '' },
    { name: 'displayName', value: user?.displayName ?? '' },
    { name: 'displayNameArabic', value: user?.displayNameArabic ?? '' },
    { name: 'email', value: user?.email ?? '' },
    { name: 'source', value: user?.externalSource?.trim() || 'MECS' },
    { name: 'returnPath', value: path },
  ]

  return { url, path, query, launch }
}
