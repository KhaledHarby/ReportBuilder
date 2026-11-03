export function homePath(can: (permission: string) => boolean) {
  if (can('reports.view')) {
    return '/'
  }
  if (can('datasets.view')) {
    return '/catalog'
  }
  if (can('lookups.view')) {
    return '/catalog/lookups'
  }
  if (can('users.view')) {
    return '/users'
  }
  if (can('roles.view')) {
    return '/roles'
  }
  if (can('permissions.view')) {
    return '/permissions'
  }
  if (can('logs.view')) {
    return '/activity'
  }
  if (can('insights.view')) {
    return '/insights'
  }
  if (can('analytics.view')) {
    return '/analytics'
  }
  return '/'
}

export function safeReturnPath(value: string | null | undefined) {
  const path = (value ?? '').trim()
  if (
    !path.startsWith('/') ||
    path.startsWith('//') ||
    path.includes('://')
  ) {
    return null
  }
  return path
}
