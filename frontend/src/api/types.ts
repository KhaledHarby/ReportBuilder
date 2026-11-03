export type SortDirection = 'asc' | 'desc'

export type DatasetColumn = {
  id: string
  columnName: string
  sqlType: string
  displayName: string
  displayNameArabic?: string | null
  isHidden: boolean
  isFilterable: boolean
  isGroupable: boolean
  isIndexed: boolean
  defaultSortDirection: SortDirection
  lookupId?: string | null
  lookupName?: string | null
  lookupNameArabic?: string | null
  lookupColumns?: LookupColumn[]
  locale?: 'default' | 'en' | 'ar' | string | null
  ordinal: number
}

export type DatasetLiveColumn = {
  columnName: string
  sqlType: string
  ordinal: number
}

export type DatasetColumnGap = {
  liveCount: number
  catalogCount: number
  missing: DatasetLiveColumn[]
}

export type DatasetCacheStatus =
  | 'None'
  | 'Refreshing'
  | 'Ready'
  | 'Failed'
  | 'Cleared'

export type CacheScheduleMode = 'Manual' | 'Interval' | 'DailyAt'

export type DatasetCacheState = {
  status: DatasetCacheStatus | string
  cacheTableName?: string | null
  lastRefreshStartedAt?: string | null
  lastRefreshCompletedAt?: string | null
  lastSuccessAt?: string | null
  nextRefreshAt?: string | null
  rowCount?: number | null
  durationMs?: number | null
  sizeBytes?: number | null
  refreshedByServer?: string | null
  refreshedByUserId?: string | null
  lastError?: string | null
  cacheVersion: number
  cacheProvider?: string | null
  servingFrom: 'cache' | 'live' | string
}

export type Dataset = {
  id: string
  schemaName: string
  objectName: string
  objectType: string
  displayName: string
  displayNameArabic?: string | null
  isEnabled: boolean
  isRestricted: boolean
  accessMode?: ReportAccessMode
  isCached: boolean
  cacheScheduleMode: CacheScheduleMode | string
  cacheIntervalMinutes?: number | null
  cacheDailyAtLocalTime?: string | null
  sortOrder: number
  updatedAt: string
  accessUserIds: string[]
  accessUsers: ReportPerson[]
  accessRoleIds?: string[]
  accessRoles?: ReportAccessRole[]
  columns: DatasetColumn[]
  cache?: DatasetCacheState | null
}

export type ConditionalFormatRule = {
  operator: string
  value?: string | null
  backgroundColor?: string | null
  color?: string | null
  bold?: boolean
}

export type LayoutColumn = {
  field: string
  expression?: string | null
  alias?: string | null
  aggregate?: string | null
  format?: string | null
  urlTemplate?: string | null
  locale?: 'default' | 'en' | 'ar' | string | null
  align?: 'left' | 'center' | 'right' | null
  headerBand?: string | null
  headerBandArabic?: string | null
  sticky?: boolean
  wrap?: boolean
  conditionalFormat?: ConditionalFormatRule[] | null
}

export type LayoutFilter = {
  field: string
  operator: string
  value?: string | null
  isFixed?: boolean
  isHidden?: boolean
  isRequired?: boolean
  alias?: string | null
  aliasArabic?: string | null
  dependsOn?: string | null
}

export type LayoutOrderBy = {
  field: string
  direction: 'asc' | 'desc'
}

export type ReportKind = 'table' | 'chart'

export type ChartType = 'bar' | 'line' | 'area' | 'pie' | 'doughnut'

export type ChartConfig = {
  type: ChartType
  stacked?: boolean
  showLegend?: boolean
  showDataLabels?: boolean
}

export type UserScopeProperty = 'id' | 'userName' | 'email' | 'displayName'

export type UserScope = {
  enabled?: boolean
  field?: string | null
  property?: UserScopeProperty | null
}

export type ReportCategory = {
  id: string
  name: string
  nameArabic?: string | null
  sortOrder: number
  createdAt: string
  updatedAt: string
}

export type MasterDetail = {
  enabled?: boolean
  datasetId?: string | null
  parentField?: string | null
  childField?: string | null
  columns: LayoutColumn[]
  filters: LayoutFilter[]
  orderBy: LayoutOrderBy[]
}

export type ReportLayout = {
  kind?: ReportKind
  columns: LayoutColumn[]
  groupBy: string[]
  filters: LayoutFilter[]
  filtersPerRow?: number
  orderBy: LayoutOrderBy[]
  chart?: ChartConfig | null
  userScope?: UserScope | null
  detail?: MasterDetail | null
}

export type ReportStatus = 'draft' | 'published'

export type ReportAccessMode = 'Everyone' | 'People' | 'Roles'

export type ReportPerson = {
  id: string
  userName: string
  displayName: string
  displayNameArabic?: string | null
}

export type ReportAccessRole = {
  id: string
  name: string
  description?: string | null
}

export type ReportSummary = {
  id: string
  name: string
  nameArabic?: string | null
  description?: string | null
  descriptionArabic?: string | null
  datasetId: string
  datasetName: string
  datasetNameArabic?: string | null
  categoryId?: string | null
  categoryName?: string | null
  categoryNameArabic?: string | null
  categorySortOrder?: number
  kind: ReportKind
  status: ReportStatus
  createdBy?: string | null
  createdByName?: string | null
  isAdminCreated: boolean
  isRestricted: boolean
  accessMode?: ReportAccessMode
  canEdit: boolean
  canSchedule: boolean
  canDelete: boolean
  hasDraft: boolean
  publishedVersion: number
  draftVersion?: number | null
  accessUserIds: string[]
  accessUsers: ReportPerson[]
  accessRoleIds?: string[]
  accessRoles?: ReportAccessRole[]
  createdAt: string
  updatedAt: string
}

export type ReportDraft = {
  version: number
  name: string
  nameArabic?: string | null
  description?: string | null
  descriptionArabic?: string | null
  datasetId: string
  categoryId?: string | null
  isRestricted: boolean
  accessMode?: ReportAccessMode
  layout: ReportLayout
  accessUserIds: string[]
  accessRoleIds?: string[]
}

export type ReportLayoutDiff = {
  onlyHere: string[]
  onlyInPublished: string[]
  filtersChanged: boolean
  sortChanged: boolean
  nameChanged: boolean
}

export type ReportRevision = {
  version: number
  name: string
  nameArabic?: string | null
  publishedAt: string
  publishedByName?: string | null
  isCurrent: boolean
  onlyHere: string[]
  onlyInPublished: string[]
  filtersChanged: boolean
  sortChanged: boolean
  nameChanged: boolean
}

export type ReportHistory = {
  currentVersion: number
  draftVersusPublished?: ReportLayoutDiff | null
  revisions: ReportRevision[]
}

export type ReportDetail = ReportSummary & {
  layout: ReportLayout
  draft?: ReportDraft | null
  filterColumns: DatasetColumn[]
}

export type ReportResult = {
  columns: {
    name: string
    label: string
    hidden?: boolean
    displayName?: string | null
    displayNameArabic?: string | null
  }[]
  rows: Record<string, unknown>[]
  totalCount: number
  skip: number
  take: number
}

export type AiStatus = {
  configured: boolean
  model: string
}

export type AiAssistResult = {
  name: string
  nameArabic?: string | null
  description: string
  descriptionArabic?: string | null
  datasetId: string
  datasetName: string
  layout: ReportLayout
  explanation: string
}

export type ColumnValueChoice = {
  value: string
  label: string
}

export type ColumnValues = {
  field: string
  values: string[]
  choices?: ColumnValueChoice[]
  truncated: boolean
}

export type LookupObject = {
  schemaName: string
  objectName: string
  objectType: string
}

export type LookupColumn = {
  name: string
  sqlType: string
  displayName?: string | null
  displayNameArabic?: string | null
  locale?: 'default' | 'en' | 'ar' | string | null
  included?: boolean | null
  showInDesigner?: boolean | null
}

export type LookupSourceKind = 'View' | 'Query' | 'Table'

export type LookupDefinition = {
  id: string
  name: string
  nameArabic?: string | null
  sourceKind: LookupSourceKind | string
  schemaName: string
  objectName: string
  sqlText?: string | null
  keyColumn: string
  labelColumn: string
  sortColumn?: string | null
  sortDirection: SortDirection
  isValid: boolean
  validationError?: string | null
  columns: LookupColumn[]
}

export type DatasetSqlScript = {
  datasetId: string
  schemaName: string
  objectName: string
  objectType: string
  script: string
}

export type ExportFormat = 'csv' | 'xlsx' | 'pdf' | 'docx'

export type UserAccount = {
  id: string
  userName: string
  email: string
  displayName: string
  displayNameArabic?: string | null
  externalSource?: string | null
  isActive: boolean
  roles: string[]
  permissions: string[]
  roleIds: string[]
  directPermissionIds: string[]
  createdBy?: string | null
  createdByName?: string | null
  createdAt: string
  modifiedBy?: string | null
  modifiedByName?: string | null
  modifiedAt: string
}

export type RoleMember = {
  id: string
  userName: string
  displayName: string
  displayNameArabic?: string | null
  email: string
  isActive: boolean
}

export type RoleRecord = {
  id: string
  name: string
  description?: string | null
  isSystem: boolean
  permissionIds: string[]
  permissions: string[]
  userCount: number
  createdBy?: string | null
  createdByName?: string | null
  createdAt: string
  modifiedBy?: string | null
  modifiedByName?: string | null
  modifiedAt: string
}

export type PermissionRecord = {
  id: string
  code: string
  name: string
  description?: string | null
  isSystem: boolean
  createdBy?: string | null
  createdByName?: string | null
  createdAt: string
  modifiedBy?: string | null
  modifiedByName?: string | null
  modifiedAt: string
}

export type AuthSession = {
  accessToken: string
  refreshToken: string
  accessTokenExpiresAt: string
  user: UserAccount
}

export type AuthPublicOptions = {
  externalLoginUrl?: string | null
  launchEnabled: boolean
}

export type UserGrants = {
  roles: { name: string; description?: string | null }[]
  permissions: { code: string; name: string; description?: string | null }[]
  reports: {
    id: string
    name: string
    nameArabic?: string | null
    status: ReportStatus
    datasetName: string
    datasetNameArabic?: string | null
  }[]
  datasets: {
    id: string
    displayName: string
    displayNameArabic?: string | null
    schemaName: string
    objectName: string
    objectType?: string
    isRestricted: boolean
  }[]
}

export type ActivityLogRow = {
  id: string
  occurredAt: string
  level: string
  category: string
  action: string
  message: string
  actorUserId?: string | null
  actorUserName?: string | null
  targetUserId?: string | null
  targetUserName?: string | null
  entityType?: string | null
  entityId?: string | null
  entityName?: string | null
  httpMethod?: string | null
  path?: string | null
  statusCode?: number | null
  durationMs?: number | null
  ipAddress?: string | null
  correlationId?: string | null
  details?: string | null
}

export type ActivityLogPage = {
  total: number
  items: ActivityLogRow[]
}

export type CacheProviderConnection = {
  host: string
  port: number
  database: string
  username: string
  password: string
  secure: boolean
  connectionString: string
  redisDatabase?: number | null
  hasPassword: boolean
}

export type AppBranding = {
  applicationName: string
  applicationNameArabic: string
  studioName: string
  studioNameArabic: string
  organizationName: string
  organizationNameArabic: string
  defaultLanguage: 'en' | 'ar'
  hasLogo: boolean
  logoVersion: number
}

export type AppBrandingWrite = {
  applicationName: string
  applicationNameArabic: string
  studioName: string
  studioNameArabic: string
  organizationName: string
  organizationNameArabic: string
  defaultLanguage: 'en' | 'ar'
  logoBase64?: string | null
  removeLogo: boolean
}

export type CacheSettings = {
  defaultProvider: string
  clickHouse: CacheProviderConnection
  timescale: CacheProviderConnection
  redis: CacheProviderConnection
  postgres: CacheProviderConnection
  mongo: CacheProviderConnection
  implementedProviders: string[]
}

export type SmtpProfile = {
  id: string
  name: string
  host: string
  port: number
  username?: string | null
  hasPassword: boolean
  fromAddress: string
  fromName?: string | null
  enableSsl: boolean
  isDefault: boolean
}

export type SmtpProfileWrite = {
  name: string
  host: string
  port: number
  username?: string | null
  password?: string | null
  fromAddress: string
  fromName?: string | null
  enableSsl: boolean
  isDefault: boolean
}

export type SmtpProfileOption = {
  id: string
  name: string
  isDefault: boolean
}

export type AiConnection = {
  id: string
  name: string
  baseUrl: string
  model: string
  hasApiKey: boolean
  isEnabled: boolean
  isDefault: boolean
}

export type AiConnectionWrite = {
  id?: string | null
  name: string
  baseUrl: string
  model: string
  apiKey?: string | null
  isEnabled: boolean
  isDefault: boolean
}

export type EmailTemplate = {
  id: string
  name: string
  subject: string
  body: string
  isHtml: boolean
  isDefault: boolean
}

export type EmailTemplateWrite = {
  name: string
  subject: string
  body: string
  isHtml: boolean
  isDefault: boolean
}

export type EmailTemplateOption = {
  id: string
  name: string
  isDefault: boolean
  isHtml: boolean
}

export type ReportSchedulePerson = {
  id: string
  displayName: string
  userName: string
  email?: string | null
}

export type ReportScheduleRecipient = {
  userId?: string | null
  roleId?: string | null
  email?: string | null
  displayName?: string | null
}

export type ReportScheduleRole = {
  id: string
  name: string
  activeCount: number
}

export type ReportScheduleRun = {
  startedAt: string
  status: string
  recipientCount: number
  message?: string | null
}

export type ReportDeliveryRow = {
  reportId: string
  name: string
  nameArabic?: string | null
  published: boolean
  enabled: boolean
  cadence: string
  dayOfWeek?: number | null
  dayOfMonth?: number | null
  localTime: string
  format: string
  language: string
  recipientCount: number
  nextRunAt?: string | null
  lastRunAt?: string | null
  lastStatus?: string | null
  lastRecipientCount?: number | null
  lastMessage?: string | null
  busy: boolean
  canRetry: boolean
}

export type ReportSchedule = {
  enabled: boolean
  cadence: string
  dayOfWeek?: number | null
  dayOfMonth?: number | null
  localTime: string
  format: string
  language: string
  smtpProfileId?: string | null
  emailTemplateId?: string | null
  userScoped: boolean
  scopeField?: string | null
  scopeProperty?: string | null
  sendOnlyIfRows: boolean
  scopeColumns: ReportScheduleScopeColumn[]
  published: boolean
  nextRunAt?: string | null
  recipients: ReportScheduleRecipient[]
  people: ReportSchedulePerson[]
  roles: ReportScheduleRole[]
  runs: ReportScheduleRun[]
}

export type ReportScheduleWrite = {
  enabled: boolean
  cadence: string
  dayOfWeek?: number | null
  dayOfMonth?: number | null
  localTime: string
  format: string
  language: string
  smtpProfileId?: string | null
  emailTemplateId?: string | null
  userIds: string[]
  roleIds: string[]
  emails: string[]
  scopeField?: string | null
  scopeProperty?: string | null
  sendOnlyIfRows: boolean
}

export type ReportScheduleScopeColumn = {
  name: string
  label: string
  labelArabic?: string | null
  sqlType: string
}

export type ActivityLogSettings = {
  retentionDays: number
  minRetentionDays: number
  maxRetentionDays: number
}

export type ReportInsightsSummary = {
  days: number
  reportRuns: number
  reportExports: number
  uniqueReportsRun: number
  activeUsers: number
  cachedDatasets: number
  cacheReady: number
  cacheFailed: number
  servingFromCache: number
  analyticsViews?: number
  analyticsQueries?: number
  analyticsExports?: number
  uniqueDashboardsViewed?: number
}

export type AnalyticsUsageInsight = {
  dashboardId: string
  dashboardName: string
  dashboardNameArabic?: string | null
  status: string
  viewCount: number
  queryCount: number
  exportCount: number
  lastViewAt?: string | null
  lastViewBy?: string | null
  lastExportAt?: string | null
  lastExportBy?: string | null
}

export type ReportUsageInsight = {
  reportId: string
  reportName: string
  reportNameArabic?: string | null
  status: string
  datasetId: string
  datasetName: string
  runCount: number
  exportCount: number
  lastRunAt?: string | null
  lastRunBy?: string | null
  lastExportAt?: string | null
  lastExportBy?: string | null
  servingFrom: string
  cacheLastSuccessAt?: string | null
  cacheStatus?: string | null
}

export type DatasetFreshnessInsight = {
  datasetId: string
  datasetName: string
  datasetNameArabic?: string | null
  objectKey: string
  isEnabled: boolean
  isCached: boolean
  servingFrom: string
  cacheStatus?: string | null
  lastSuccessAt?: string | null
  nextRefreshAt?: string | null
  rowCount?: number | null
  refreshedByServer?: string | null
  lastError?: string | null
  reportCount: number
}

export type ReportInsights = {
  summary: ReportInsightsSummary
  reports: ReportUsageInsight[]
  analytics?: AnalyticsUsageInsight[]
  freshness: DatasetFreshnessInsight[]
}

export type AnalyticsAggregation = 'count' | 'sum' | 'avg' | 'min' | 'max' | 'expr'

export type AnalyticsJoinType = 'inner' | 'left'

export type AnalyticsSource = {
  id: string
  datasetId: string
  alias: string
}

export type AnalyticsLink = {
  id: string
  leftSourceId: string
  rightSourceId: string
  leftKeys: string[]
  rightKeys: string[]
  joinType: AnalyticsJoinType
}

export type AnalyticsDimension = {
  id: string
  sourceId: string
  field: string
  expression?: string | null
  label: string
}

export type AnalyticsMeasure = {
  id: string
  sourceId: string
  field?: string | null
  expression?: string | null
  aggregation: AnalyticsAggregation
  label: string
  format?: string | null
}

export type AnalyticsDetailColumn = {
  id: string
  sourceId: string
  field: string
  label: string
  urlTemplate?: string | null
}

export type AnalyticsChartType = 'bar' | 'line' | 'pie'

export type AnalyticsSelectionState = 'default' | 'compare'

export type AnalyticsChart = {
  id: string
  type: AnalyticsChartType
  dimensionId: string
  measureId: string
  title: string
  state?: AnalyticsSelectionState | null
}

export type AnalyticsMap = {
  enabled: boolean
  sourceId: string
  latField: string
  lngField: string
  labelField?: string | null
}

export type AnalyticsDashboardConfig = {
  version: number
  sources: AnalyticsSource[]
  links: AnalyticsLink[]
  dimensions: AnalyticsDimension[]
  measures: AnalyticsMeasure[]
  detailColumns: AnalyticsDetailColumn[]
  charts?: AnalyticsChart[]
  map?: AnalyticsMap | null
}

export type AnalyticsDashboardSummary = {
  id: string
  name: string
  nameArabic?: string | null
  description?: string | null
  status: ReportStatus
  isRestricted: boolean
  accessMode?: ReportAccessMode
  canEdit: boolean
  canDelete: boolean
  sourceCount: number
  dimensionCount: number
  measureCount: number
  updatedAt: string
}

export type AnalyticsDashboardDetail = {
  id: string
  name: string
  nameArabic?: string | null
  description?: string | null
  descriptionArabic?: string | null
  status: ReportStatus
  isRestricted: boolean
  accessMode?: ReportAccessMode
  canEdit: boolean
  canDelete: boolean
  accessUserIds: string[]
  accessRoleIds?: string[]
  accessRoles?: ReportAccessRole[]
  config: AnalyticsDashboardConfig
  createdBy?: string | null
  createdAt: string
  updatedAt: string
}

export type AnalyticsDashboardWrite = {
  name: string
  nameArabic?: string | null
  description?: string | null
  descriptionArabic?: string | null
  config: AnalyticsDashboardConfig
  status?: ReportStatus
  isRestricted?: boolean
  accessMode?: ReportAccessMode
  accessUserIds?: string[]
  accessRoleIds?: string[]
}

export const emptyAnalyticsConfig = (): AnalyticsDashboardConfig => ({
  version: 1,
  sources: [],
  links: [],
  dimensions: [],
  measures: [],
  detailColumns: [],
  charts: [],
  map: null,
})

export type AnalyticsValueState = 'selected' | 'associated' | 'excluded'

export type AnalyticsDimensionValue = {
  value: string
  count: number
  state: AnalyticsValueState
}

export type AnalyticsDimensionResult = {
  dimensionId: string
  label: string
  selectedValues: string[]
  values: AnalyticsDimensionValue[]
}

export type AnalyticsMeasureResult = {
  measureId: string
  label: string
  formattedValue?: string | null
  numericValue?: number | null
}

export type AnalyticsGridColumn = {
  id: string
  label: string
  urlTemplate?: string | null
}

export type AnalyticsChartPoint = {
  value: string
  metric: number
}

export type AnalyticsChartResult = {
  chartId: string
  title: string
  type: AnalyticsChartType
  dimensionId: string
  measureId: string
  state: AnalyticsSelectionState
  points: AnalyticsChartPoint[]
}

export type AnalyticsMapPoint = {
  lat: number
  lng: number
  label: string
}

export type AnalyticsDataSource = 'live' | 'sqlserver' | 'mongo'

export type AnalyticsSnapshotResult = {
  dimensions: AnalyticsDimensionResult[]
  measures: AnalyticsMeasureResult[]
  charts?: AnalyticsChartResult[]
  /** Where the numbers came from; absent on older API builds. */
  source?: AnalyticsDataSource
  /** Engine time for this snapshot in milliseconds. */
  elapsedMs?: number
}

export type AnalyticsDataResult = {
  columns: AnalyticsGridColumn[]
  rows: Record<string, unknown>[]
  totalCount: number
  skip: number
  take: number
}

export type AnalyticsMapResult = {
  mapPoints: AnalyticsMapPoint[]
}

export type AnalyticsQueryRequest = {
  selections: Record<string, string[]>
  skip?: number
  take?: number
  track?: boolean
}

export type AnalyticsValueSearchRequest = {
  selections: Record<string, string[]>
  query: string
  dimensionId?: string
  take?: number
}

export type AnalyticsSearchHit = {
  dimensionId: string
  dimensionLabel: string
  value: string
  count: number
  state: AnalyticsValueState
}

export type AnalyticsSearchResult = {
  hits: AnalyticsSearchHit[]
}

export const EXPORT_FORMATS: { id: ExportFormat; label: string; hint: string }[] = [
  { id: 'csv', label: 'CSV', hint: 'Spreadsheet · all rows' },
  { id: 'xlsx', label: 'Excel', hint: 'Spreadsheet · all rows' },
  { id: 'pdf', label: 'PDF', hint: 'Printable document' },
  { id: 'docx', label: 'Word', hint: 'Word document' },
]

export const emptyLayout = (): ReportLayout => ({
  kind: 'table',
  columns: [],
  groupBy: [],
  filters: [],
  orderBy: [],
  userScope: null,
})
