import type {
  AiAssistResult,
  AiStatus,
  AuthPublicOptions,
  AuthSession,
  ColumnValues,
  Dataset,
  DatasetLiveColumn,
  LookupColumn,
  LookupDefinition,
  LookupObject,
  LookupSourceKind,
  DatasetColumnGap,
  PermissionRecord,
  ReportDetail,
  ReportLayout,
  ReportAccessMode,
  ReportAccessRole,
  ReportPerson,
  ReportResult,
  ReportStatus,
  ReportSummary,
  ReportCategory,
  RoleMember,
  RoleRecord,
  SortDirection,
  UserAccount,
  UserGrants,
  ExportFormat,
  ReportHistory,
  ActivityLogPage,
  ActivityLogSettings,
  CacheProviderConnection,
  CacheSettings,
  AppBranding,
  AppBrandingWrite,
  SmtpProfile,
  SmtpProfileOption,
  SmtpProfileWrite,
  AiConnection,
  AiConnectionWrite,
  EmailTemplate,
  EmailTemplateOption,
  EmailTemplateWrite,
  ReportSchedule,
  ReportDeliveryRow,
  ReportScheduleWrite,
  DatasetSqlScript,
  LayoutFilter,
  ReportInsights,
  AnalyticsDashboardSummary,
  AnalyticsDashboardDetail,
  AnalyticsDashboardWrite,
  AnalyticsQueryRequest,
  AnalyticsSnapshotResult,
  AnalyticsDataResult,
  AnalyticsMapResult,
  AnalyticsValueSearchRequest,
  AnalyticsSearchResult,
} from './types'
import { apiUrl } from './baseUrl'
import { bilingualExportName, exportFileName, saveDownload } from '../lib/download'
import {
  clearSession,
  getAccessExpiresAt,
  getAccessToken,
  getRefreshToken,
  setSession,
} from '../auth/session'

let refreshPromise: Promise<boolean> | null = null

export class SessionExpiredError extends Error {
  constructor() {
    super('Session expired')
    this.name = 'SessionExpiredError'
  }
}

export function isSessionExpired(error: unknown): boolean {
  return error instanceof SessionExpiredError
}

function isAnonymousAuth(path: string) {
  return (
    path.startsWith('/api/auth/login') ||
    path.startsWith('/api/auth/refresh') ||
    path.startsWith('/api/auth/exchange') ||
    path.startsWith('/api/auth/options') ||
    path.startsWith('/api/auth/launch') ||
    path.startsWith('/api/auth/forgot-password') ||
    path.startsWith('/api/auth/reset-password') ||
    path.startsWith('/api/settings/branding')
  )
}

function failExpired(): never {
  clearSession()
  throw new SessionExpiredError()
}

async function readError(response: Response) {
  const body = await response.json().catch(() => null)
  const detail =
    (body && (body.detail || body.title || body.message)) || response.statusText
  return typeof detail === 'string' ? detail : 'Request failed'
}

async function tryRefresh() {
  if (refreshPromise) {
    return refreshPromise
  }

  refreshPromise = (async () => {
    const refreshToken = getRefreshToken()
    if (!refreshToken) {
      return false
    }

    const response = await fetch(apiUrl('/api/auth/refresh'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })

    if (!response.ok) {
      clearSession()
      return false
    }

    const session = (await response.json()) as AuthSession
    setSession(session)
    return true
  })().finally(() => {
    refreshPromise = null
  })

  return refreshPromise
}

async function refreshOrExpire() {
  try {
    return await tryRefresh()
  } catch {
    return false
  }
}

// Renew a token that is about to expire before a burst of analytics calls all receive 401.
async function ensureFreshAccess(path: string) {
  if (isAnonymousAuth(path) || !getAccessToken()) {
    return
  }
  const raw = getAccessExpiresAt()
  if (!raw) {
    return
  }
  const expires = Date.parse(raw)
  if (!Number.isFinite(expires) || expires - Date.now() > 30_000) {
    return
  }
  if (!(await refreshOrExpire())) {
    failExpired()
  }
}

function withAuth(headers: Headers) {
  const token = getAccessToken()
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }
}

async function request<T>(
  path: string,
  init?: RequestInit,
  retried = false,
): Promise<T> {
  if (init?.signal?.aborted) {
    throw new DOMException('The operation was aborted.', 'AbortError')
  }
  await ensureFreshAccess(path)

  const headers = new Headers(init?.headers)
  if (!headers.has('Content-Type') && init?.body) {
    headers.set('Content-Type', 'application/json')
  }
  withAuth(headers)

  const response = await fetch(apiUrl(path), {
    ...init,
    headers,
  })

  if (response.status === 401 && !isAnonymousAuth(path)) {
    if (!retried && (await refreshOrExpire())) {
      return request<T>(path, init, true)
    }
    failExpired()
  }

  if (response.status === 204) {
    return undefined as T
  }

  if (!response.ok) {
    throw new Error(await readError(response))
  }

  return (await response.json()) as T
}

async function download(
  path: string,
  init: RequestInit,
  fallbackName: string,
  file?: FileSystemFileHandle,
) {
  await ensureFreshAccess(path)
  const headers = new Headers(init.headers)
  withAuth(headers)
  const response = await fetch(apiUrl(path), { ...init, headers })
  if (response.status === 401) {
    if (await refreshOrExpire()) {
      const retryHeaders = new Headers(init.headers)
      withAuth(retryHeaders)
      const retry = await fetch(apiUrl(path), { ...init, headers: retryHeaders })
      if (retry.status === 401) {
        failExpired()
      }
      return saveDownload(retry, fallbackName, file)
    }
    failExpired()
  }
  return saveDownload(response, fallbackName, file)
}

function queryString(params: Record<string, string | number | undefined | null>) {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') {
      continue
    }
    search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

export const api = {
  getAuthOptions: () => request<AuthPublicOptions>('/api/auth/options'),
  login: (userName: string, password: string) =>
    request<AuthSession>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ userName, password }),
    }),
  exchangeSso: (code: string) =>
    request<AuthSession>('/api/auth/exchange', {
      method: 'POST',
      body: JSON.stringify({ code }),
    }),
  refresh: (refreshToken: string) =>
    request<AuthSession>('/api/auth/refresh', {
      method: 'POST',
      body: JSON.stringify({ refreshToken }),
    }),
  logout: (refreshToken?: string | null) =>
    request<void>('/api/auth/logout', {
      method: 'POST',
      body: JSON.stringify({ refreshToken: refreshToken || null }),
    }),
  me: () => request<UserAccount>('/api/auth/me'),
  listMyGrants: () => request<UserGrants>('/api/auth/grants'),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>('/api/auth/password', {
      method: 'POST',
      body: JSON.stringify({
        currentPassword,
        newPassword,
        refreshToken: getRefreshToken(),
      }),
    }),
  requestPasswordReset: (userName: string) =>
    request<void>('/api/auth/forgot-password', {
      method: 'POST',
      body: JSON.stringify({ userName }),
    }),
  resetPassword: (token: string, newPassword: string) =>
    request<void>('/api/auth/reset-password', {
      method: 'POST',
      body: JSON.stringify({ token, newPassword }),
    }),
  listUsers: () => request<UserAccount[]>('/api/users'),
  createUser: (body: {
    userName: string
    email: string
    displayName: string
    displayNameArabic?: string | null
    password?: string
    isActive: boolean
    roleIds: string[]
    permissionIds: string[]
  }) =>
    request<UserAccount>('/api/users', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updateUser: (
    id: string,
    body: {
      userName: string
      email: string
      displayName: string
      displayNameArabic?: string | null
      password?: string
      isActive: boolean
      roleIds: string[]
      permissionIds: string[]
    },
  ) =>
    request<UserAccount>(`/api/users/${id}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  activateUser: (id: string, isActive: boolean) =>
    request<UserAccount>(`/api/users/${id}/activate`, {
      method: 'POST',
      body: JSON.stringify({ isActive }),
    }),
  deleteUser: (id: string) =>
    request<void>(`/api/users/${id}/delete`, { method: 'POST' }),
  signOutUser: (id: string) =>
    request<void>(`/api/users/${id}/sign-out`, { method: 'POST' }),
  listLogs: (params: {
    skip?: number
    take?: number
    q?: string
    level?: string
    category?: string
    from?: string
    to?: string
    actor?: string
  }) => request<ActivityLogPage>(`/api/logs${queryString(params)}`),
  getInsights: (days = 30) =>
    request<ReportInsights>(`/api/insights${queryString({ days })}`),
  listAnalyticsDashboards: () =>
    request<AnalyticsDashboardSummary[]>('/api/analytics'),
  getAnalyticsDashboard: (id: string) =>
    request<AnalyticsDashboardDetail>(`/api/analytics/${id}`),
  createAnalyticsDashboard: (body: AnalyticsDashboardWrite) =>
    request<AnalyticsDashboardDetail>('/api/analytics', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updateAnalyticsDashboard: (id: string, body: AnalyticsDashboardWrite) =>
    request<AnalyticsDashboardDetail>(`/api/analytics/${id}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  deleteAnalyticsDashboard: (id: string) =>
    request<void>(`/api/analytics/${id}/delete`, { method: 'POST' }),
  listAnalyticsPeople: () => request<ReportPerson[]>('/api/analytics/people'),
  listAnalyticsAccessRoles: () => request<ReportAccessRole[]>('/api/analytics/access-roles'),
  queryAnalyticsDashboard: (
    id: string,
    body: AnalyticsQueryRequest,
    signal?: AbortSignal,
  ) =>
    request<AnalyticsSnapshotResult>(`/api/analytics/${id}/query`, {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),
  queryAnalyticsData: (
    id: string,
    body: AnalyticsQueryRequest,
    signal?: AbortSignal,
  ) =>
    request<AnalyticsDataResult>(`/api/analytics/${id}/data`, {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),
  queryAnalyticsMap: (
    id: string,
    body: AnalyticsQueryRequest,
    signal?: AbortSignal,
  ) =>
    request<AnalyticsMapResult>(`/api/analytics/${id}/map`, {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),
  searchAnalyticsValues: (
    id: string,
    body: AnalyticsValueSearchRequest,
    signal?: AbortSignal,
  ) =>
    request<AnalyticsSearchResult>(`/api/analytics/${id}/search`, {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),
  exportAnalyticsDashboard: (id: string, body: AnalyticsQueryRequest, name?: string) =>
    download(
      `/api/analytics/${id}/export`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      },
      exportFileName(name, 'csv'),
    ),
  recordAnalyticsExport: (id: string, format: 'pdf' | 'pptx' | 'csv' | 'screen') =>
    request<void>(`/api/analytics/${id}/export-event`, {
      method: 'POST',
      body: JSON.stringify({ format }),
    }),
  getBranding: () => request<AppBranding>('/api/settings/branding'),
  updateBranding: (body: AppBrandingWrite) =>
    request<AppBranding>('/api/settings/general', {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  brandingLogoUrl: (version: number) => apiUrl(`/api/settings/branding/logo?v=${version}`),
  getCacheSettings: () => request<CacheSettings>('/api/settings/cache'),
  updateCacheSettings: (body: CacheSettings) =>
    request<CacheSettings>('/api/settings/cache', {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  testCacheSettings: (provider: string, connection?: CacheProviderConnection) =>
    request<{ ok: boolean }>('/api/settings/cache/test', {
      method: 'POST',
      body: JSON.stringify({ provider, connection }),
    }),
  listSmtpProfiles: () => request<SmtpProfile[]>('/api/settings/smtp'),
  listSmtpOptions: () => request<SmtpProfileOption[]>('/api/settings/smtp/options'),
  createSmtpProfile: (body: SmtpProfileWrite) =>
    request<SmtpProfile>('/api/settings/smtp', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updateSmtpProfile: (id: string, body: SmtpProfileWrite) =>
    request<SmtpProfile>(`/api/settings/smtp/${id}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  deleteSmtpProfile: (id: string) =>
    request<void>(`/api/settings/smtp/${id}`, { method: 'DELETE' }),
  testSmtpProfile: (body: SmtpProfileWrite) =>
    request<{ ok: boolean }>('/api/settings/smtp/test', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  listAiConnections: () => request<AiConnection[]>('/api/settings/ai'),
  createAiConnection: (body: AiConnectionWrite) =>
    request<AiConnection>('/api/settings/ai', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updateAiConnection: (id: string, body: AiConnectionWrite) =>
    request<AiConnection>(`/api/settings/ai/${id}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  deleteAiConnection: (id: string) =>
    request<void>(`/api/settings/ai/${id}`, { method: 'DELETE' }),
  testAiConnection: (body: AiConnectionWrite) =>
    request<{ ok: boolean; reply: string }>('/api/settings/ai/test', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  listEmailTemplates: () => request<EmailTemplate[]>('/api/settings/email-templates'),
  listEmailTemplateOptions: () => request<EmailTemplateOption[]>('/api/settings/email-templates/options'),
  createEmailTemplate: (body: EmailTemplateWrite) =>
    request<EmailTemplate>('/api/settings/email-templates', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updateEmailTemplate: (id: string, body: EmailTemplateWrite) =>
    request<EmailTemplate>(`/api/settings/email-templates/${id}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  deleteEmailTemplate: (id: string) =>
    request<void>(`/api/settings/email-templates/${id}`, { method: 'DELETE' }),
  getReportSchedule: (id: string) => request<ReportSchedule>(`/api/reports/${id}/schedule`),
  listDeliveryHealth: () => request<ReportDeliveryRow[]>('/api/reports/schedules/health'),
  saveReportSchedule: (id: string, body: ReportScheduleWrite) =>
    request<ReportSchedule>(`/api/reports/${id}/schedule`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),
  sendReportSchedule: (id: string) =>
    request<ReportSchedule>(`/api/reports/${id}/schedule/send`, { method: 'POST' }),
  getLogSettings: () => request<ActivityLogSettings>('/api/logs/settings'),
  updateLogSettings: (retentionDays: number) =>
    request<ActivityLogSettings>('/api/logs/settings', {
      method: 'PUT',
      body: JSON.stringify({ retentionDays }),
    }),
  listRoles: () => request<RoleRecord[]>('/api/roles'),
  listRoleUsers: (id: string) => request<RoleMember[]>(`/api/roles/${id}/users`),
  createRole: (body: {
    name: string
    description?: string
    permissionIds: string[]
  }) =>
    request<RoleRecord>('/api/roles', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updateRole: (
    id: string,
    body: { name: string; description?: string; permissionIds: string[] },
  ) =>
    request<RoleRecord>(`/api/roles/${id}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  deleteRole: (id: string) =>
    request<void>(`/api/roles/${id}/delete`, { method: 'POST' }),
  listPermissions: () => request<PermissionRecord[]>('/api/permissions'),
  createPermission: (body: {
    code: string
    name: string
    description?: string
  }) =>
    request<PermissionRecord>('/api/permissions', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updatePermission: (
    id: string,
    body: { code: string; name: string; description?: string },
  ) =>
    request<PermissionRecord>(`/api/permissions/${id}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  deletePermission: (id: string) =>
    request<void>(`/api/permissions/${id}/delete`, { method: 'POST' }),
  listDatasets: (enabledOnly = false) =>
    request<Dataset[]>(`/api/datasets?enabledOnly=${enabledOnly}`),
  listLookups: () => request<LookupDefinition[]>('/api/lookups'),
  listLookupObjects: (schema: string) =>
    request<LookupObject[]>(`/api/lookups/objects?schema=${encodeURIComponent(schema)}`),
  previewLookupColumns: (schema: string, objectName: string) =>
    request<LookupColumn[]>(
      `/api/lookups/columns?schema=${encodeURIComponent(schema)}&objectName=${encodeURIComponent(objectName)}`,
    ),
  previewLookupQueryColumns: (sqlText: string) =>
    request<LookupColumn[]>('/api/lookups/columns', {
      method: 'POST',
      body: JSON.stringify({ sqlText }),
    }),
  saveLookup: (body: {
    id?: string
    name: string
    nameArabic?: string | null
    sourceKind: LookupSourceKind | 'view' | 'query' | 'table'
    schemaName?: string
    objectName?: string
    sqlText?: string
    keyColumn: string
    labelColumn: string
    sortColumn?: string
    sortDirection?: SortDirection
    columnLocales?: {
      name: string
      locale?: string | null
      displayName?: string | null
      displayNameArabic?: string | null
      included?: boolean
      showInDesigner?: boolean
    }[]
  }) =>
    request<LookupDefinition>('/api/lookups', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  deleteLookup: (id: string) => request<void>(`/api/lookups/${id}`, { method: 'DELETE' }),
  getDataset: (id: string) => request<Dataset>(`/api/datasets/${id}`),
  listDatasetPeople: () => request<ReportPerson[]>('/api/datasets/people'),
  listDatasetAccessRoles: () => request<ReportAccessRole[]>('/api/datasets/access-roles'),
  listColumnValues: (datasetId: string, field: string, filters?: LayoutFilter[], lang?: string) =>
    filters || field.includes(':')
      ? request<ColumnValues>(`/api/datasets/${datasetId}/values`, {
          method: 'POST',
          body: JSON.stringify({ field, filters: filters ?? [], lang }),
        })
      : request<ColumnValues>(
          `/api/datasets/${datasetId}/values/${encodeURIComponent(field)}${lang ? `?lang=${encodeURIComponent(lang)}` : ''}`,
        ),
  listReportFilterValues: (reportId: string, field: string, filters?: LayoutFilter[], lang?: string) =>
    filters || field.includes(':')
      ? request<ColumnValues>(`/api/reports/${reportId}/filter-values`, {
          method: 'POST',
          body: JSON.stringify({ field, filters: filters ?? [], lang }),
        })
      : request<ColumnValues>(
          `/api/reports/${reportId}/filter-values/${encodeURIComponent(field)}${lang ? `?lang=${encodeURIComponent(lang)}` : ''}`,
        ),
  discoverDatasets: () =>
    request<Dataset[]>('/api/datasets/discover', { method: 'POST' }),
  previewDatasetQuery: (sqlText: string) =>
    request<DatasetLiveColumn[]>('/api/datasets/query/preview', {
      method: 'POST',
      body: JSON.stringify({ sqlText }),
    }),
  createDatasetQuery: (body: {
    displayName: string
    displayNameArabic?: string
    sqlText: string
  }) =>
    request<Dataset>('/api/datasets/query', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  listMissingColumns: (id: string) =>
    request<DatasetColumnGap>(`/api/datasets/${id}/missing-columns`),
  getDatasetSql: (id: string) =>
    request<DatasetSqlScript>(`/api/datasets/${id}/sql`),
  applyDatasetSql: (id: string, script: string) =>
    request<DatasetSqlScript>(`/api/datasets/${id}/sql`, {
      method: 'POST',
      body: JSON.stringify({ script }),
    }),
  importDatasetColumns: (id: string, columnNames: string[]) =>
    request<Dataset>(`/api/datasets/${id}/import-columns`, {
      method: 'POST',
      body: JSON.stringify({ columnNames }),
    }),
  updateDataset: (
    id: string,
    body: {
      displayName?: string
      displayNameArabic?: string | null
      isEnabled?: boolean
      isRestricted?: boolean
      accessMode?: ReportAccessMode
      sortOrder?: number
      accessUserIds?: string[]
      accessRoleIds?: string[]
      columns?: {
        id: string
        displayName?: string
        displayNameArabic?: string | null
        isHidden?: boolean
        isFilterable?: boolean
        isGroupable?: boolean
        isIndexed?: boolean
        defaultSortDirection?: SortDirection
        updateLookup?: boolean
        lookupId?: string | null
      }[]
    },
  ) =>
    request<Dataset>(`/api/datasets/${id}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  configureDatasetCache: (
    id: string,
    body: {
      isCached?: boolean
      cacheScheduleMode?: string
      cacheIntervalMinutes?: number | null
      cacheDailyAtLocalTime?: string | null
    },
  ) =>
    request<Dataset>(`/api/datasets/${id}/cache`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  refreshDatasetCache: (id: string) =>
    request<Dataset>(`/api/datasets/${id}/cache/refresh`, { method: 'POST' }),
  clearDatasetCache: (id: string) =>
    request<Dataset>(`/api/datasets/${id}/cache`, { method: 'DELETE' }),
  listReports: () => request<ReportSummary[]>('/api/reports'),
  listReportCategories: () => request<ReportCategory[]>('/api/report-categories'),
  createReportCategory: (body: { name: string; nameArabic?: string; sortOrder?: number }) =>
    request<ReportCategory>('/api/report-categories', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updateReportCategory: (
    id: string,
    body: { name: string; nameArabic?: string; sortOrder?: number },
  ) =>
    request<ReportCategory>(`/api/report-categories/${id}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  deleteReportCategory: (id: string) =>
    request<void>(`/api/report-categories/${id}/delete`, { method: 'POST' }),
  listReportPeople: () => request<ReportPerson[]>('/api/reports/people'),
  listReportAccessRoles: () => request<ReportAccessRole[]>('/api/reports/access-roles'),
  getReport: (id: string) => request<ReportDetail>(`/api/reports/${id}`),
  duplicateReport: (id: string) =>
    request<ReportDetail>(`/api/reports/${id}/duplicate`, { method: 'POST' }),
  createReport: (body: {
    name: string
    nameArabic?: string
    description?: string
    descriptionArabic?: string
    datasetId: string
    layout: ReportLayout
    status?: ReportStatus
    categoryId?: string | null
    isRestricted?: boolean
    accessMode?: ReportAccessMode
    accessUserIds?: string[]
    accessRoleIds?: string[]
  }) =>
    request<ReportDetail>('/api/reports', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  updateReport: (
    id: string,
    body: {
      name: string
      nameArabic?: string
      description?: string
      descriptionArabic?: string
      datasetId: string
      layout: ReportLayout
      status?: ReportStatus
      categoryId?: string | null
      isRestricted?: boolean
      accessMode?: ReportAccessMode
      accessUserIds?: string[]
      accessRoleIds?: string[]
    },
  ) =>
    request<ReportDetail>(`/api/reports/${id}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  publishReport: (id: string) =>
    request<ReportDetail>(`/api/reports/${id}/publish`, { method: 'POST' }),
  unpublishReport: (id: string) =>
    request<ReportDetail>(`/api/reports/${id}/unpublish`, { method: 'POST' }),
  reportHistory: (id: string) => request<ReportHistory>(`/api/reports/${id}/revisions`),
  restoreReportRevision: (id: string, version: number) =>
    request<ReportDetail>(`/api/reports/${id}/revisions/${version}/restore`, { method: 'POST' }),
  deleteReport: (id: string) =>
    request<void>(`/api/reports/${id}/delete`, { method: 'POST' }),
  validateExpression: (body: {
    datasetId: string
    expression: string
    allowAggregates: boolean
  }) =>
    request<void>('/api/reports/expressions/validate', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  preview: (body: {
    datasetId: string
    layout: ReportLayout
    skip: number
    take: number
    lang?: string
  }) =>
    request<ReportResult>('/api/reports/preview', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  runReport: (
    id: string,
    body: {
      skip: number
      take: number
      layout?: ReportLayout
      lang?: string
    },
  ) =>
    request<ReportResult>(`/api/reports/${id}/run`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  queryReportDetails: (
    id: string,
    body: {
      parentValue: string
      skip: number
      take: number
      lang?: string
      layout?: ReportLayout
    },
  ) =>
    request<ReportResult>(`/api/reports/${id}/details`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  exportReport: (
    id: string,
    format: ExportFormat = 'csv',
    lang?: string,
    file?: FileSystemFileHandle,
    name?: string,
  ) =>
    download(
      `/api/reports/${id}/export?format=${format}${lang ? `&lang=${encodeURIComponent(lang)}` : ''}`,
      { method: 'GET' },
      exportFileName(name, format),
      file,
    ),
  exportPreview: (
    body: {
      name: string
      nameArabic?: string
      datasetId: string
      layout: ReportLayout
    },
    format: ExportFormat = 'csv',
    lang?: string,
    file?: FileSystemFileHandle,
  ) =>
    download(
      `/api/reports/preview/export?format=${format}${lang ? `&lang=${encodeURIComponent(lang)}` : ''}`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      },
      exportFileName(bilingualExportName(body.name, body.nameArabic), format),
      file,
    ),
  aiStatus: () => request<AiStatus>('/api/ai/status'),
  aiAssist: (body: {
    prompt: string
    datasetId?: string
    name?: string
    nameArabic?: string
    description?: string
    descriptionArabic?: string
    layout?: ReportLayout
  }) =>
    request<AiAssistResult>('/api/ai/assist', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
}
