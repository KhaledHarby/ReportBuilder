function stripApiSuffix(value: string) {
  return value.replace(/\/+$/, '').replace(/\/api$/i, '')
}

function viteBase() {
  const base = import.meta.env.BASE_URL.replace(/\/+$/, '')
  return base === '/' ? '' : base
}

function sameOriginPath(value: string) {
  if (!/^https?:\/\//i.test(value) || typeof window === 'undefined') {
    return value
  }

  try {
    const url = new URL(value)
    if (url.origin === window.location.origin) {
      return stripApiSuffix(url.pathname) || viteBase()
    }
  } catch {
    return value
  }

  return value
}

export function apiBase() {
  if (import.meta.env.DEV) {
    return ''
  }

  const runtime = window.__API_BASE_URL__?.trim()
  const built = import.meta.env.VITE_API_BASE_URL?.trim()
  const resolved = stripApiSuffix(runtime || built || viteBase())
  return sameOriginPath(resolved)
}

export function apiUrl(path: string) {
  const suffix = path.startsWith('/') ? path : `/${path}`
  return `${apiBase()}${suffix}`
}
