export type SidebarMode = 'pinned' | 'collapsed' | 'hidden'

const KEY = 'rb.sidebarMode'

export function readSidebarMode(): SidebarMode {
  if (typeof localStorage === 'undefined') {
    return 'pinned'
  }
  const value = localStorage.getItem(KEY)
  return value === 'collapsed' || value === 'hidden' || value === 'pinned' ? value : 'pinned'
}

export function writeSidebarMode(mode: SidebarMode) {
  localStorage.setItem(KEY, mode)
}
