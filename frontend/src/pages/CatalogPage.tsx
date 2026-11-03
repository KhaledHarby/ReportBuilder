import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { Dataset, DatasetCacheState, LookupDefinition, ReportAccessMode, ReportAccessRole } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { DatasetQueryDialog } from '../components/DatasetQueryDialog'
import { DatasetSqlDialog } from '../components/DatasetSqlDialog'
import { IconLayers, IconSql, typeTone } from '../components/Icons'
import { ReportAccessPanel } from '../components/ReportAccessPanel'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'
import type { MessageKey } from '../i18n/messages'
import { datasetTechnicalName, isQueryDataset, sortDatasetsForCatalog } from '../lib/datasets'

function datasetAccessMode(dataset: Dataset): ReportAccessMode {
  return (
    dataset.accessMode ??
    (dataset.isRestricted ? 'People' : 'Everyone')
  )
}

function datasetAccessLabel(
  dataset: Dataset,
  t: (key: 'everyone' | 'datasetAccessCount' | 'datasetAccessRoleCount' | 'datasetAccessAdminsOnly', vars?: { n: number }) => string,
) {
  const mode = datasetAccessMode(dataset)
  if (mode === 'Roles') {
    const count = dataset.accessRoleIds?.length ?? 0
    return count ? t('datasetAccessRoleCount', { n: count }) : t('datasetAccessAdminsOnly')
  }
  if (mode === 'People') {
    const count = dataset.accessUserIds?.length ?? 0
    return count ? t('datasetAccessCount', { n: count }) : t('datasetAccessAdminsOnly')
  }
  return t('everyone')
}

export function CatalogPage() {
  const { can } = useAuth()
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const lookups = useQuery({
    queryKey: ['lookups'],
    queryFn: api.listLookups,
    enabled: can('datasets.view'),
  })
  const datasets = useQuery({
    queryKey: ['datasets'],
    queryFn: () => api.listDatasets(false),
    refetchInterval: (query) =>
      query.state.data?.some((item) => item.cache?.status === 'Refreshing')
        ? 3000
        : false,
  })
  const people = useQuery({
    queryKey: ['dataset-people'],
    queryFn: api.listDatasetPeople,
    enabled: can('datasets.update'),
  })
  const accessRoles = useQuery({
    queryKey: ['dataset-access-roles'],
    queryFn: api.listDatasetAccessRoles,
    enabled: can('datasets.update'),
  })
  const [openId, setOpenId] = useState<string | null>(null)
  const [sqlDatasetId, setSqlDatasetId] = useState<string | null>(null)
  const [queryOpen, setQueryOpen] = useState(false)
  const [filter, setFilter] = useState('')

  const discover = useMutation({
    mutationFn: api.discoverDatasets,
    onSuccess: (data) => {
      queryClient.setQueryData(['datasets'], data)
    },
  })

  const update = useMutation({
    mutationFn: ({
      id,
      body,
    }: {
      id: string
      body: Parameters<typeof api.updateDataset>[1]
    }) => api.updateDataset(id, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['datasets'] }),
  })

  const visible = useMemo(() => {
    const list = datasets.data ?? []
    const q = filter.trim().toLowerCase()
    const filtered = q
      ? list.filter((item) =>
          `${datasetTechnicalName(item, 'sql query')} ${item.displayName} ${item.displayNameArabic ?? ''}`
            .toLowerCase()
            .includes(q),
        )
      : list
    return sortDatasetsForCatalog(filtered)
  }, [datasets.data, filter])

  const enabledCount = datasets.data?.filter((item) => item.isEnabled).length ?? 0
  const cachedCount = datasets.data?.filter((item) => item.isCached).length ?? 0
  const sqlDataset = datasets.data?.find((item) => item.id === sqlDatasetId) ?? null
  const canViewSql = can('sql.view') || can('sql.modify')
  const canModifySql = can('sql.modify')

  return (
    <section className="page catalog">
      <header className="page-header">
        <div>
          <div className="kicker">{t('sourcesKicker')}</div>
          <h1>{t('catalogTitle')}</h1>
          <p className="lede">{t('catalogLede')}</p>
        </div>
        {(can('datasets.update') || can('datasets.discover')) && (
          <div className="header-actions">
            {can('datasets.update') && (
              <button type="button" className="btn" onClick={() => setQueryOpen(true)}>
                {t('datasetFromSql')}
              </button>
            )}
            {can('datasets.discover') && (
              <button
                type="button"
                className="btn primary"
                onClick={() => discover.mutate()}
                disabled={discover.isPending}
              >
                {discover.isPending ? t('scanning') : t('discoverObjects')}
              </button>
            )}
          </div>
        )}
      </header>

      <div className="stats">
        <div className="stat">
          <span className="muted small">{t('objectsFound')}</span>
          <b>{datasets.data?.length ?? 0}</b>
        </div>
        <div className="stat">
          <span className="muted small">{t('enabledForReports')}</span>
          <b>{enabledCount}</b>
        </div>
        <div className="stat">
          <span className="muted small">{t('datasetCacheStat')}</span>
          <b>{cachedCount}</b>
        </div>
      </div>

      {(datasets.isError || discover.isError || update.isError || lookups.isError) && (
        <div className="banner error">
          {(
            (datasets.error || discover.error || update.error || lookups.error) as Error
          ).message}
        </div>
      )}

      <div className="page-header catalog-toolbar">
        <h2>{t('catalog')}</h2>
        <input
          className="search"
          placeholder={t('filterObjects')}
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
        />
      </div>

      {datasets.isLoading ? (
        <Spinner page label={t('loadingApp')} />
      ) : (
        <div className="catalog-grid">
          {visible.map((dataset) => (
            <DatasetCard
              key={dataset.id}
              dataset={dataset}
              expanded={openId === dataset.id}
              onToggle={() =>
                setOpenId((current) =>
                  current === dataset.id ? null : dataset.id,
                )
              }
              onUpdate={(body) =>
                can('datasets.update')
                  ? update.mutate({ id: dataset.id, body })
                  : undefined
              }
              canUpdate={can('datasets.update')}
              canCache={can('datasets.cache')}
              canViewSql={canViewSql || (isQueryDataset(dataset) && can('datasets.update'))}
              onViewSql={() => setSqlDatasetId(dataset.id)}
              people={people.data ?? []}
              peopleLoading={people.isLoading}
              roles={accessRoles.data ?? []}
              rolesLoading={accessRoles.isLoading}
              lookups={lookups.data ?? []}
            />
          ))}
          {!visible.length && (
            <div className="empty">
              <IconLayers />
              <h2>{t('catalogEmptyTitle')}</h2>
              <p>{t('catalogEmptyHint')}</p>
            </div>
          )}
        </div>
      )}

      <DatasetSqlDialog
        datasetId={sqlDatasetId}
        title={
          sqlDataset
            ? localizedText(lang, sqlDataset.displayName, sqlDataset.displayNameArabic)
            : t('viewSql')
        }
        objectName={
          sqlDataset ? datasetTechnicalName(sqlDataset, t('datasetQuerySource')) : ''
        }
        canModify={sqlDataset && isQueryDataset(sqlDataset) ? can('datasets.update') : canModifySql}
        query={sqlDataset ? isQueryDataset(sqlDataset) : false}
        onClose={() => setSqlDatasetId(null)}
      />
      <DatasetQueryDialog
        open={queryOpen}
        onClose={() => setQueryOpen(false)}
        onCreated={(id) => setOpenId(id)}
      />
    </section>
  )
}

function DatasetCard({
  dataset,
  expanded,
  onToggle,
  onUpdate,
  canUpdate = true,
  canCache = false,
  canViewSql = false,
  onViewSql,
  people,
  peopleLoading,
  roles,
  rolesLoading,
  lookups = [],
}: {
  dataset: Dataset
  expanded: boolean
  onToggle: () => void
  onUpdate: (body: Parameters<typeof api.updateDataset>[1]) => void
  canUpdate?: boolean
  canCache?: boolean
  canViewSql?: boolean
  onViewSql?: () => void
  people: Parameters<typeof ReportAccessPanel>[0]['people']
  peopleLoading: boolean
  roles: ReportAccessRole[]
  rolesLoading: boolean
  lookups?: LookupDefinition[]
}) {
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const setCacheable = useMutation({
    mutationFn: (isCached: boolean) =>
      api.configureDatasetCache(dataset.id, {
        isCached,
        cacheScheduleMode: dataset.cacheScheduleMode || 'Manual',
        cacheIntervalMinutes: dataset.cacheIntervalMinutes ?? 60,
        cacheDailyAtLocalTime: dataset.cacheDailyAtLocalTime ?? '02:00',
      }),
    onSuccess: (updated) => {
      queryClient.setQueryData(['datasets'], (current: Dataset[] | undefined) =>
        current?.map((item) => (item.id === updated.id ? updated : item)),
      )
    },
  })

  return (
    <article className={`catalog-card ${dataset.isEnabled ? 'enabled' : ''}`}>
      <div className="catalog-top">
        <div>
          <button type="button" className="link" onClick={onToggle}>
            {localizedText(lang, dataset.displayName, dataset.displayNameArabic)}
          </button>
          <div className="object-name">
            {datasetTechnicalName(dataset, t('datasetQuerySource'))}
          </div>
        </div>
        <div className="catalog-meta">
          <span className={`badge ${dataset.objectType.toLowerCase()}`}>
            {isQueryDataset(dataset) ? t('lookupQuery') : dataset.objectType}
          </span>
          <span
            className={`badge ${dataset.isRestricted ? 'admin' : 'published'}`}
          >
            {datasetAccessLabel(dataset, t)}
          </span>
          {dataset.isCached && (
            <span
              className={`badge ${
                dataset.cache?.status === 'Ready'
                  ? 'published'
                  : dataset.cache?.status === 'Failed'
                    ? 'admin'
                    : 'draft'
              }`}
            >
              {dataset.cache?.status === 'Ready'
                ? t('datasetCacheOn')
                : dataset.cache?.status ?? t('datasetCacheable')}
            </span>
          )}
          {canViewSql && (
            <button
              type="button"
              className="btn icon sql-view-btn"
              aria-label={t('viewSql')}
              title={t('viewSql')}
              onClick={(event) => {
                event.stopPropagation()
                onViewSql?.()
              }}
            >
              <IconSql />
            </button>
          )}
          {canCache && (
            <label
              className="switch"
              title={
                !dataset.isEnabled
                  ? t('datasetCacheEnableFirst')
                  : t('datasetCacheable')
              }
            >
              <input
                type="checkbox"
                checked={Boolean(dataset.isCached)}
                disabled={!dataset.isEnabled || setCacheable.isPending}
                onChange={(event) => {
                  event.stopPropagation()
                  setCacheable.mutate(event.target.checked)
                }}
              />
              {dataset.isCached ? t('datasetCacheable') : t('datasetCacheableOff')}
            </label>
          )}
          <label className="switch">
            <input
              type="checkbox"
              checked={dataset.isEnabled}
              disabled={!canUpdate}
              onChange={(event) =>
                onUpdate({ isEnabled: event.target.checked })
              }
            />
            {dataset.isEnabled ? t('enabled') : t('off')}
          </label>
        </div>
      </div>
      {setCacheable.isError && (
        <div className="banner error" style={{ marginTop: 12 }}>
          {(setCacheable.error as Error).message}
        </div>
      )}
      {expanded && (
        <div style={{ marginTop: 16 }}>
        <div className="catalog-names">
          <label className="muted small">
            {t('displayName')}
            <input
              defaultValue={dataset.displayName}
              disabled={!canUpdate}
              onBlur={(event) => {
                const value = event.target.value.trim()
                if (value && value !== dataset.displayName) {
                  onUpdate({ displayName: value })
                }
              }}
            />
          </label>
          <label className="muted small">
            {t('displayNameArabic')}
            <input
              dir="rtl"
              lang="ar"
              defaultValue={dataset.displayNameArabic ?? ''}
              disabled={!canUpdate}
              onBlur={(event) => {
                const value = event.target.value.trim()
                if (value !== (dataset.displayNameArabic ?? '').trim()) {
                  onUpdate({ displayNameArabic: value })
                }
              }}
            />
          </label>
          <label className="muted small">
            {t('datasetSort')}
            <input
              type="number"
              inputMode="numeric"
              defaultValue={dataset.sortOrder ?? 0}
              disabled={!canUpdate}
              onBlur={(event) => {
                const value = Number.parseInt(event.target.value, 10)
                if (!Number.isFinite(value) || value === (dataset.sortOrder ?? 0)) {
                  event.target.value = String(dataset.sortOrder ?? 0)
                  return
                }
                onUpdate({ sortOrder: value })
              }}
            />
            <span className="muted">{t('datasetSortHint')}</span>
          </label>
        </div>
          {(canCache || dataset.isCached) && (
            <DatasetCachePanel dataset={dataset} canCache={canCache} />
          )}
          <ReportAccessPanel
            isRestricted={datasetAccessMode(dataset) !== 'Everyone'}
            accessMode={datasetAccessMode(dataset)}
            accessUserIds={dataset.accessUserIds ?? []}
            people={people}
            loading={peopleLoading}
            roles={roles}
            rolesLoading={rolesLoading}
            accessRoleIds={dataset.accessRoleIds ?? []}
            heading={t('datasetAccess')}
            hint={t('datasetAccessHint')}
            accessLabel={t('datasetAccess')}
            disabled={!canUpdate}
            onRestrictedChange={() => undefined}
            onAccessModeChange={(mode) =>
              onUpdate({
                accessMode: mode,
                isRestricted: mode !== 'Everyone',
                accessUserIds: mode === 'People' ? dataset.accessUserIds : [],
                accessRoleIds: mode === 'Roles' ? dataset.accessRoleIds : [],
              })
            }
            onAccessUserIdsChange={(ids) =>
              onUpdate({
                accessMode: 'People',
                isRestricted: true,
                accessUserIds: ids,
                accessRoleIds: [],
              })
            }
            onAccessRoleIdsChange={(ids) =>
              onUpdate({
                accessMode: 'Roles',
                isRestricted: true,
                accessUserIds: [],
                accessRoleIds: ids,
              })
            }
          />
          <ColumnGapPanel
            dataset={dataset}
            canUpdate={canUpdate}
          />
          {dataset.columns.length === 0 ? (
            <p className="muted">{t('enableToLoadColumns')}</p>
          ) : (
            <table className="data-table compact">
              <thead>
                <tr>
                  <th>{t('column')}</th>
                  <th>{t('type')}</th>
                  <th>{t('label')}</th>
                  <th>{t('labelArabic')}</th>
                  <th>{t('hide')}</th>
                  <th>{t('filter')}</th>
                  <th>{t('group')}</th>
                  <th title={t('cacheIndexHint')}>{t('cacheIndex')}</th>
                  <th>{t('columnSort')}</th>
                  <th>{t('lookupColumn')}</th>
                </tr>
              </thead>
              <tbody>
                {dataset.columns.map((column) => (
                  <tr key={column.id}>
                    <td>
                      <span className={`tone ${typeTone(column.sqlType)}`} />{' '}
                      {column.columnName}
                    </td>
                    <td>{column.sqlType}</td>
                    <td>
                      <input
                        defaultValue={column.displayName}
                        onBlur={(event) => {
                          const value = event.target.value.trim()
                          if (value && value !== column.displayName) {
                            onUpdate({
                              columns: [{ id: column.id, displayName: value }],
                            })
                          }
                        }}
                      />
                    </td>
                    <td>
                      <input
                        dir="rtl"
                        lang="ar"
                        defaultValue={column.displayNameArabic ?? ''}
                        onBlur={(event) => {
                          const value = event.target.value.trim()
                          if (value !== (column.displayNameArabic ?? '').trim()) {
                            onUpdate({
                              columns: [
                                { id: column.id, displayNameArabic: value },
                              ],
                            })
                          }
                        }}
                      />
                    </td>
                    <td>
                      <input
                        type="checkbox"
                        checked={column.isHidden}
                        onChange={(event) =>
                          onUpdate({
                            columns: [
                              { id: column.id, isHidden: event.target.checked },
                            ],
                          })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="checkbox"
                        checked={column.isFilterable}
                        onChange={(event) =>
                          onUpdate({
                            columns: [
                              {
                                id: column.id,
                                isFilterable: event.target.checked,
                              },
                            ],
                          })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="checkbox"
                        checked={column.isGroupable}
                        onChange={(event) =>
                          onUpdate({
                            columns: [
                              {
                                id: column.id,
                                isGroupable: event.target.checked,
                              },
                            ],
                          })
                        }
                      />
                    </td>
                    <td>
                      <input
                        type="checkbox"
                        checked={Boolean(column.isIndexed)}
                        disabled={!canIndexSqlType(column.sqlType)}
                        title={
                          canIndexSqlType(column.sqlType)
                            ? t('cacheIndexHint')
                            : t('cacheIndexUnavailable')
                        }
                        onChange={(event) =>
                          onUpdate({
                            columns: [
                              {
                                id: column.id,
                                isIndexed: event.target.checked,
                              },
                            ],
                          })
                        }
                      />
                    </td>
                    <td>
                      <select
                        value={column.defaultSortDirection ?? 'desc'}
                        onChange={(event) =>
                          onUpdate({
                            columns: [
                              {
                                id: column.id,
                                defaultSortDirection: event.target.value as 'asc' | 'desc',
                              },
                            ],
                          })
                        }
                      >
                        <option value="desc">{t('descending')}</option>
                        <option value="asc">{t('ascending')}</option>
                      </select>
                    </td>
                    <td>
                      <select
                        value={column.lookupId ?? ''}
                        disabled={!canUpdate}
                        onChange={(event) =>
                          onUpdate({
                            columns: [
                              {
                                id: column.id,
                                updateLookup: true,
                                lookupId: event.target.value || null,
                              },
                            ],
                          })
                        }
                      >
                        <option value="">{t('lookupNone')}</option>
                        {lookups
                          .filter((lookup) => lookup.isValid)
                          .map((lookup) => (
                            <option key={lookup.id} value={lookup.id}>
                              {localizedText(lang, lookup.name, lookup.nameArabic)}
                            </option>
                          ))}
                      </select>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
    </article>
  )
}

function servingLabel(
  cache: DatasetCacheState | null | undefined,
  t: (key: MessageKey) => string,
) {
  if (cache?.servingFrom !== 'cache') {
    return t('datasetCacheServingLive')
  }

  switch (cache.cacheProvider) {
    case 'ClickHouse':
      return t('datasetCacheServingClickHouse')
    case 'Redis':
      return t('datasetCacheServingRedis')
    case 'Mongo':
      return t('datasetCacheServingMongo')
    case 'Postgres':
      return t('datasetCacheServingPostgres')
    default:
      return t('datasetCacheServingSqlServer')
  }
}

function formatWhen(value?: string | null, neverLabel?: string) {
  if (!value) return neverLabel ?? '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return neverLabel ?? '—'
  return date.toLocaleString()
}

function formatBytes(value?: number | null) {
  if (value == null || value <= 0) return '—'
  if (value < 1024) return `${value} B`
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`
  if (value < 1024 * 1024 * 1024) return `${(value / (1024 * 1024)).toFixed(1)} MB`
  return `${(value / (1024 * 1024 * 1024)).toFixed(2)} GB`
}

function canIndexSqlType(sqlType: string) {
  const type = sqlType.trim().toLowerCase()
  if (
    type.includes('max') ||
    type === 'text' ||
    type === 'ntext' ||
    type === 'image' ||
    type === 'xml' ||
    type === 'sql_variant' ||
    type === 'geography' ||
    type === 'geometry' ||
    type === 'hierarchyid' ||
    type === 'timestamp' ||
    type === 'rowversion'
  ) {
    return false
  }
  const base = (type.split('(')[0] ?? type).trim()
  return (
    base === 'int' ||
    base === 'bigint' ||
    base === 'smallint' ||
    base === 'tinyint' ||
    base === 'bit' ||
    base === 'uniqueidentifier' ||
    base === 'date' ||
    base === 'datetime' ||
    base === 'datetime2' ||
    base === 'smalldatetime' ||
    base === 'time' ||
    base === 'nvarchar' ||
    base === 'varchar' ||
    base === 'nchar' ||
    base === 'char' ||
    base === 'sysname' ||
    base === 'decimal' ||
    base === 'numeric' ||
    base === 'money' ||
    base === 'smallmoney'
  )
}

function DatasetCachePanel({
  dataset,
  canCache,
}: {
  dataset: Dataset
  canCache: boolean
}) {
  const { t } = useLocale()
  const queryClient = useQueryClient()
  const mode = (dataset.cacheScheduleMode ?? 'Manual') as string
  const cache = dataset.cache

  const patchCache = useMutation({
    mutationFn: (body: Parameters<typeof api.configureDatasetCache>[1]) =>
      api.configureDatasetCache(dataset.id, body),
    onSuccess: (updated) => {
      queryClient.setQueryData(['datasets'], (current: Dataset[] | undefined) =>
        current?.map((item) => (item.id === updated.id ? updated : item)),
      )
    },
  })

  const refresh = useMutation({
    mutationFn: () => api.refreshDatasetCache(dataset.id),
    onSuccess: (updated) => {
      queryClient.setQueryData(['datasets'], (current: Dataset[] | undefined) =>
        current?.map((item) => (item.id === updated.id ? updated : item)),
      )
    },
  })

  const clear = useMutation({
    mutationFn: () => api.clearDatasetCache(dataset.id),
    onSuccess: (updated) => {
      queryClient.setQueryData(['datasets'], (current: Dataset[] | undefined) =>
        current?.map((item) => (item.id === updated.id ? updated : item)),
      )
    },
  })

  const busy =
    patchCache.isPending || refresh.isPending || clear.isPending || cache?.status === 'Refreshing'
  const error =
    (patchCache.error || refresh.error || clear.error) as Error | null

  return (
    <div className="dataset-cache">
      <div className="dataset-cache-head">
        <div>
          <div className="dataset-cache-title">{t('datasetCache')}</div>
          <p className="muted small">{t('datasetCacheHint')}</p>
          {!canCache && (
            <p className="muted small">{t('datasetCacheNeedPermission')}</p>
          )}
          {canCache && !dataset.isEnabled && (
            <p className="muted small">{t('datasetCacheEnableFirst')}</p>
          )}
        </div>
        <label className="switch">
          <input
            type="checkbox"
            checked={Boolean(dataset.isCached)}
            disabled={!canCache || !dataset.isEnabled || busy}
            onChange={(event) =>
              patchCache.mutate({
                isCached: event.target.checked,
                cacheScheduleMode: mode || 'Manual',
                cacheIntervalMinutes: dataset.cacheIntervalMinutes ?? 60,
                cacheDailyAtLocalTime: dataset.cacheDailyAtLocalTime ?? '02:00',
              })
            }
          />
          {dataset.isCached ? t('datasetCacheable') : t('datasetCacheableOff')}
        </label>
      </div>

      {error && <div className="banner error">{error.message}</div>}

      {dataset.isCached && (
        <>
          <div className="dataset-cache-controls">
            <label className="muted small">
              {t('datasetCacheSchedule')}
              <select
                value={mode}
                disabled={!canCache || busy}
                onChange={(event) =>
                  patchCache.mutate({
                    isCached: true,
                    cacheScheduleMode: event.target.value,
                    cacheIntervalMinutes: dataset.cacheIntervalMinutes ?? 60,
                    cacheDailyAtLocalTime: dataset.cacheDailyAtLocalTime ?? '02:00',
                  })
                }
              >
                <option value="Manual">{t('datasetCacheManual')}</option>
                <option value="Interval">{t('datasetCacheInterval')}</option>
                <option value="DailyAt">{t('datasetCacheDailyAt')}</option>
              </select>
            </label>
            {mode === 'Interval' && (
              <label className="muted small">
                {t('datasetCacheIntervalMinutes')}
                <input
                  type="number"
                  min={5}
                  max={10080}
                  defaultValue={dataset.cacheIntervalMinutes ?? 60}
                  disabled={!canCache || busy}
                  onBlur={(event) => {
                    const value = Number.parseInt(event.target.value, 10)
                    if (!Number.isFinite(value) || value === (dataset.cacheIntervalMinutes ?? 60)) {
                      event.target.value = String(dataset.cacheIntervalMinutes ?? 60)
                      return
                    }
                    patchCache.mutate({
                      isCached: true,
                      cacheScheduleMode: 'Interval',
                      cacheIntervalMinutes: value,
                    })
                  }}
                />
              </label>
            )}
            {mode === 'DailyAt' && (
              <label className="muted small">
                {t('datasetCacheDailyTime')}
                <input
                  type="time"
                  defaultValue={dataset.cacheDailyAtLocalTime ?? '02:00'}
                  disabled={!canCache || busy}
                  onBlur={(event) => {
                    const value = event.target.value
                    if (!value || value === (dataset.cacheDailyAtLocalTime ?? '02:00')) {
                      return
                    }
                    patchCache.mutate({
                      isCached: true,
                      cacheScheduleMode: 'DailyAt',
                      cacheDailyAtLocalTime: value,
                    })
                  }}
                />
              </label>
            )}
          </div>

          <div className="dataset-cache-actions">
            {canCache && (
              <>
                <button
                  type="button"
                  className="btn primary"
                  disabled={busy || !dataset.isEnabled}
                  onClick={() => refresh.mutate()}
                >
                  {refresh.isPending || cache?.status === 'Refreshing'
                    ? t('datasetCacheRefreshing')
                    : t('datasetCacheRefreshNow')}
                </button>
                <button
                  type="button"
                  className="btn"
                  disabled={busy}
                  onClick={() => clear.mutate()}
                >
                  {t('datasetCacheClear')}
                </button>
              </>
            )}
          </div>

          <dl className="dataset-cache-status">
            <div>
              <dt>{t('datasetCacheStatus')}</dt>
              <dd>{cache?.status ?? 'None'}</dd>
            </div>
            <div>
              <dt>{t('datasetCacheServing')}</dt>
              <dd>{servingLabel(cache, t)}</dd>
            </div>
            <div>
              <dt>{t('datasetCacheLastSync')}</dt>
              <dd>{formatWhen(cache?.lastSuccessAt, t('datasetCacheNever'))}</dd>
            </div>
            <div>
              <dt>{t('datasetCacheNextSync')}</dt>
              <dd>{formatWhen(cache?.nextRefreshAt, t('datasetCacheNotScheduled'))}</dd>
            </div>
            <div>
              <dt>{t('datasetCacheServer')}</dt>
              <dd>{cache?.refreshedByServer || '—'}</dd>
            </div>
            <div>
              <dt>{t('datasetCacheRows')}</dt>
              <dd>{cache?.rowCount != null ? cache.rowCount.toLocaleString() : '—'}</dd>
            </div>
            <div>
              <dt>{t('datasetCacheDuration')}</dt>
              <dd>
                {cache?.durationMs != null
                  ? `${(cache.durationMs / 1000).toFixed(1)}s`
                  : '—'}
              </dd>
            </div>
            <div>
              <dt>{t('datasetCacheSize')}</dt>
              <dd>{formatBytes(cache?.sizeBytes)}</dd>
            </div>
            <div className="dataset-cache-wide">
              <dt>{t('datasetCacheTable')}</dt>
              <dd>
                {cache?.cacheTableName
                  ? cache.cacheProvider === 'ClickHouse' || cache.cacheProvider === 'Redis' || cache.cacheProvider === 'Mongo'
                    ? cache.cacheTableName
                    : `cache.${cache.cacheTableName}`
                  : '—'}
              </dd>
            </div>
            {cache?.lastError && (
              <div className="dataset-cache-wide">
                <dt>{t('datasetCacheError')}</dt>
                <dd className="dataset-cache-error">{cache.lastError}</dd>
              </div>
            )}
          </dl>
        </>
      )}
    </div>
  )
}

function ColumnGapPanel({
  dataset,
  canUpdate,
}: {
  dataset: Dataset
  canUpdate: boolean
}) {
  const { t } = useLocale()
  const queryClient = useQueryClient()
  const gap = useQuery({
    queryKey: ['dataset-missing-columns', dataset.id],
    queryFn: () => api.listMissingColumns(dataset.id),
  })
  const [selected, setSelected] = useState<Set<string>>(new Set())

  const missingKey = (gap.data?.missing ?? []).map((column) => column.columnName).join('\0')
  useEffect(() => {
    setSelected(new Set(missingKey.length > 0 ? missingKey.split('\0') : []))
  }, [dataset.id, missingKey])

  const importColumns = useMutation({
    mutationFn: (columnNames: string[]) =>
      api.importDatasetColumns(dataset.id, columnNames),
    onSuccess: (updated) => {
      queryClient.setQueryData(['datasets'], (current: Dataset[] | undefined) =>
        current?.map((item) => (item.id === updated.id ? updated : item)),
      )
      queryClient.invalidateQueries({ queryKey: ['datasets'] })
      queryClient.invalidateQueries({
        queryKey: ['dataset-missing-columns', dataset.id],
      })
    },
  })

  if (gap.isLoading) {
    return (
      <div className="column-gap-status">
        <Spinner size="sm" label={t('checkingColumns')} />
      </div>
    )
  }

  if (gap.isError) {
    return (
      <div className="banner error">
        {(gap.error as Error).message}
      </div>
    )
  }

  const missing = gap.data?.missing ?? []
  if ((gap.data?.liveCount ?? 0) === 0) {
    return <p className="muted column-gap-status">{t('columnsNotOnServer')}</p>
  }

  if (missing.length === 0) {
    return null
  }

  const selectedCount = missing.filter((column) => selected.has(column.columnName)).length
  const allSelected = selectedCount === missing.length
  const importedCount = importColumns.isSuccess
    ? importColumns.variables?.length ?? 0
    : 0

  return (
    <div className="column-gap">
      <div className="column-gap-head">
        <div>
          <div className="column-gap-title">{t('missingColumnsTitle')}</div>
          <p className="muted small">
            {t('missingColumnsHint', {
              n: missing.length,
              object: datasetTechnicalName(dataset, t('datasetQuerySource')),
            })}
          </p>
        </div>
        {canUpdate && (
          <div className="column-gap-actions">
            <button
              type="button"
              className="btn"
              onClick={() =>
                setSelected(
                  allSelected
                    ? new Set()
                    : new Set(missing.map((column) => column.columnName)),
                )
              }
            >
              {allSelected ? t('selectNone') : t('selectAll')}
            </button>
            <button
              type="button"
              className="btn primary"
              disabled={selectedCount === 0 || importColumns.isPending}
              onClick={() =>
                importColumns.mutate(
                  missing
                    .filter((column) => selected.has(column.columnName))
                    .map((column) => column.columnName),
                )
              }
            >
              {importColumns.isPending
                ? t('importingColumns')
                : t('importColumns', { n: selectedCount })}
            </button>
          </div>
        )}
      </div>
      {importColumns.isError && (
        <div className="banner error">
          {(importColumns.error as Error).message}
        </div>
      )}
      {importColumns.isSuccess && importedCount > 0 && (
        <div className="banner ok">{t('importedColumns', { n: importedCount })}</div>
      )}
      <table className="data-table compact">
        <thead>
          <tr>
            {canUpdate && <th>{t('import')}</th>}
            <th>{t('column')}</th>
            <th>{t('type')}</th>
          </tr>
        </thead>
        <tbody>
          {missing.map((column) => (
            <tr key={column.columnName}>
              {canUpdate && (
                <td>
                  <input
                    type="checkbox"
                    checked={selected.has(column.columnName)}
                    onChange={(event) => {
                      setSelected((current) => {
                        const next = new Set(current)
                        if (event.target.checked) {
                          next.add(column.columnName)
                        } else {
                          next.delete(column.columnName)
                        }
                        return next
                      })
                    }}
                  />
                </td>
              )}
              <td>
                <span className={`tone ${typeTone(column.sqlType)}`} />{' '}
                {column.columnName}
              </td>
              <td>{column.sqlType}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
