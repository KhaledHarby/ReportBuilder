import type { AnalyticsViewState } from './analyticsFilters'

export type AnalyticsBookmark = {
  id: string
  name: string
  createdAt: string
  view: AnalyticsViewState
}

function key(dashboardId: string) {
  return `rb.analytics.bookmarks.${dashboardId}`
}

export function loadBookmarks(dashboardId: string): AnalyticsBookmark[] {
  try {
    const raw = localStorage.getItem(key(dashboardId))
    if (!raw) {
      return []
    }
    const parsed = JSON.parse(raw) as AnalyticsBookmark[]
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

export function persistBookmarks(dashboardId: string, bookmarks: AnalyticsBookmark[]) {
  localStorage.setItem(key(dashboardId), JSON.stringify(bookmarks))
}

export function createBookmark(
  dashboardId: string,
  name: string,
  view: AnalyticsViewState,
): AnalyticsBookmark[] {
  const next = [
    {
      id: crypto.randomUUID(),
      name: name.trim() || 'Bookmark',
      createdAt: new Date().toISOString(),
      view,
    },
    ...loadBookmarks(dashboardId),
  ].slice(0, 40)
  persistBookmarks(dashboardId, next)
  return next
}

export function deleteBookmark(dashboardId: string, id: string): AnalyticsBookmark[] {
  const next = loadBookmarks(dashboardId).filter((item) => item.id !== id)
  persistBookmarks(dashboardId, next)
  return next
}
