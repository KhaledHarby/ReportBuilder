import {
  closestCenter,
  DndContext,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import {
  arrayMove,
  SortableContext,
  useSortable,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api, isSessionExpired } from '../api/client'
import type {
  AnalyticsAggregation,
  AnalyticsChart,
  AnalyticsChartType,
  AnalyticsDashboardConfig,
  AnalyticsDimension,
  AnalyticsDetailColumn,
  AnalyticsJoinType,
  AnalyticsLink,
  AnalyticsMeasure,
  AnalyticsSelectionState,
  AnalyticsSource,
  Dataset,
  ReportAccessMode,
  ReportStatus,
} from '../api/types'
import { emptyAnalyticsConfig } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { Field } from '../components/AdminFields'
import { ReportAccessPanel } from '../components/ReportAccessPanel'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'
import { usePageTitle } from '../lib/pageTitle'
import { localizedText } from '../i18n/localized'
import type { Lang } from '../i18n/locale'

type Step = 1 | 2 | 3

function newId(prefix: string) {
  return `${prefix}_${crypto.randomUUID().slice(0, 8)}`
}

function datasetLabel(lang: Lang, dataset: Dataset) {
  return localizedText(lang, dataset.displayName, dataset.displayNameArabic)
}

function sourceLabel(source: AnalyticsSource, datasets: Dataset[], lang: Lang) {
  const dataset = datasets.find((item) => item.id === source.datasetId)
  const alias = source.alias.trim()
  if (alias) {
    return alias
  }
  return dataset ? datasetLabel(lang, dataset) : source.id
}

/**
 * Selects keep an id in local state, but the underlying list can change while the
 * step is open. Fall back to the first option so the id always matches what the
 * user sees in the dropdown.
 */
function pickId(current: string, options: { id: string }[]) {
  return options.some((item) => item.id === current) ? current : (options[0]?.id ?? '')
}

function columnsForSource(
  sourceId: string,
  config: AnalyticsDashboardConfig,
  datasets: Dataset[],
) {
  const source = config.sources.find((item) => item.id === sourceId)
  if (!source) {
    return []
  }
  const dataset = datasets.find((item) => item.id === source.datasetId)
  return (dataset?.columns ?? [])
    .filter((column) => !column.isHidden)
    .slice()
    .sort((a, b) => a.ordinal - b.ordinal)
}

function reorderById<T extends { id: string }>(items: T[], activeId: string, overId: string) {
  const oldIndex = items.findIndex((item) => item.id === activeId)
  const newIndex = items.findIndex((item) => item.id === overId)
  if (oldIndex < 0 || newIndex < 0 || oldIndex === newIndex) {
    return items
  }
  return arrayMove(items, oldIndex, newIndex)
}

function SortableStack({
  items,
  onReorder,
  children,
}: {
  items: { id: string }[]
  onReorder: (activeId: string, overId: string) => void
  children: ReactNode
}) {
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
  )

  function handleDragEnd(event: DragEndEvent) {
    const overId = event.over?.id
    if (!overId || event.active.id === overId) {
      return
    }
    onReorder(String(event.active.id), String(overId))
  }

  return (
    <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
      <SortableContext items={items.map((item) => item.id)} strategy={verticalListSortingStrategy}>
        <div className="analytics-sort-stack">{children}</div>
      </SortableContext>
    </DndContext>
  )
}

function SortableItemRow({ id, children }: { id: string; children: ReactNode }) {
  const { t } = useLocale()
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id,
  })

  return (
    <div
      ref={setNodeRef}
      style={{
        transform: CSS.Transform.toString(transform),
        transition,
      }}
      className={`analytics-sort-row${isDragging ? ' is-dragging' : ''}`}
    >
      <button
        type="button"
        className="analytics-reorder-handle"
        title={t('analyticsReorder')}
        aria-label={t('analyticsReorder')}
        {...listeners}
        {...attributes}
      >
        ⋮⋮
      </button>
      {children}
    </div>
  )
}

export function AnalyticsDesignerPage() {
  const { id } = useParams()
  const isNew = !id
  const { user } = useAuth()
  const { lang, t } = useLocale()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const datasetsQuery = useQuery({
    queryKey: ['datasets', 'enabled'],
    queryFn: () => api.listDatasets(true),
  })
  const peopleQuery = useQuery({
    queryKey: ['analytics-people'],
    queryFn: api.listAnalyticsPeople,
  })
  const rolesQuery = useQuery({
    queryKey: ['analytics-access-roles'],
    queryFn: api.listAnalyticsAccessRoles,
  })
  const dashboardQuery = useQuery({
    queryKey: ['analytics', id],
    queryFn: () => api.getAnalyticsDashboard(id!),
    enabled: Boolean(id),
  })

  const [step, setStep] = useState<Step>(1)
  const [name, setName] = useState('')
  const [nameArabic, setNameArabic] = useState('')
  usePageTitle(localizedText(lang, name, nameArabic) || (isNew ? t('analyticsNew') : null))
  const [description, setDescription] = useState('')
  const [descriptionArabic, setDescriptionArabic] = useState('')
  const [status, setStatus] = useState<ReportStatus>('draft')
  const [accessMode, setAccessMode] = useState<ReportAccessMode>('Everyone')
  const [accessUserIds, setAccessUserIds] = useState<string[]>([])
  const [accessRoleIds, setAccessRoleIds] = useState<string[]>([])
  const [config, setConfig] = useState<AnalyticsDashboardConfig>(emptyAnalyticsConfig)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [hydrated, setHydrated] = useState(isNew)

  useEffect(() => {
    if (!dashboardQuery.data || hydrated) {
      return
    }
    const item = dashboardQuery.data
    setName(item.name)
    setNameArabic(item.nameArabic ?? '')
    setDescription(item.description ?? '')
    setDescriptionArabic(item.descriptionArabic ?? '')
    setStatus(item.status)
    setAccessMode(item.accessMode ?? (item.isRestricted ? 'People' : 'Everyone'))
    setAccessUserIds(item.accessUserIds ?? [])
    setAccessRoleIds(item.accessRoleIds ?? [])
    setConfig({
      ...emptyAnalyticsConfig(),
      ...item.config,
      sources: item.config.sources ?? [],
      links: item.config.links ?? [],
      dimensions: item.config.dimensions ?? [],
      measures: item.config.measures ?? [],
      detailColumns: item.config.detailColumns ?? [],
      charts: item.config.charts ?? [],
      map: item.config.map ?? null,
    })
    setHydrated(true)
  }, [dashboardQuery.data, hydrated])

  const datasets = datasetsQuery.data ?? []

  const save = useMutation({
    mutationFn: (nextStatus: ReportStatus) => {
      const body = {
        name,
        nameArabic: nameArabic || null,
        description: description || null,
        descriptionArabic: descriptionArabic || null,
        config,
        status: nextStatus,
        isRestricted: accessMode !== 'Everyone',
        accessMode,
        accessUserIds,
        accessRoleIds,
      }
      return isNew
        ? api.createAnalyticsDashboard(body)
        : api.updateAnalyticsDashboard(id!, body)
    },
    onSuccess: async (saved) => {
      setSaveError(null)
      setStatus(saved.status)
      setAccessMode(saved.accessMode ?? (saved.isRestricted ? 'People' : 'Everyone'))
      setAccessUserIds(saved.accessUserIds ?? [])
      setAccessRoleIds(saved.accessRoleIds ?? [])
      await queryClient.invalidateQueries({ queryKey: ['analytics'] })
      if (isNew) {
        navigate(`/analytics/${saved.id}`, { replace: true })
      }
    },
    onError: (error) => setSaveError((error as Error).message),
  })

  const canContinueStep1 = config.sources.length > 0 && name.trim().length >= 2
  const canContinueStep2 = config.dimensions.length > 0

  if (
    isSessionExpired(dashboardQuery.error) ||
    isSessionExpired(datasetsQuery.error)
  ) {
    return <Spinner fullscreen label={t('loadingApp')} />
  }

  if ((!isNew && dashboardQuery.isLoading) || datasetsQuery.isLoading || !hydrated) {
    return <Spinner fullscreen label={t('loading')} />
  }

  if (!isNew && dashboardQuery.isError) {
    return (
      <section className="page">
        <div className="banner error">{dashboardQuery.error?.message}</div>
        <Link to="/analytics">{t('back')}</Link>
      </section>
    )
  }

  return (
    <section className="page analytics-designer analytics-designer-stage">
      <header className="page-header">
        <div>
          <div className="kicker">{t('analyticsDesignerKicker')}</div>
          <h1>{isNew ? t('analyticsDesignerNewTitle') : t('analyticsDesignerEditTitle')}</h1>
          <p className="lede">{t('analyticsDesignerLede')}</p>
        </div>
        <div className="page-actions">
          <span className={`badge ${status === 'published' ? 'published' : 'warn'}`}>
            {status === 'published' ? t('published') : t('draft')}
          </span>
          <Link className="btn" to="/analytics">
            {t('back')}
          </Link>
          {!isNew && (
            <Link className="btn" to={`/analytics/${id}/view`}>
              {t('analyticsOpen')}
            </Link>
          )}
          <button
            type="button"
            className="btn"
            disabled={save.isPending || !canContinueStep1}
            onClick={() => save.mutate('draft')}
          >
            {save.isPending ? t('saving') : t('saveDraft')}
          </button>
          <button
            type="button"
            className="btn primary"
            disabled={save.isPending || !canContinueStep1 || !canContinueStep2}
            onClick={() => save.mutate('published')}
          >
            {save.isPending ? t('publishing') : t('publish')}
          </button>
        </div>
      </header>

      <ol className="analytics-steps" aria-label={t('analyticsSteps')}>
        {(
          [
            {
              value: 1 as Step,
              label: t('analyticsStepSources'),
              done: canContinueStep1,
              hint: String(config.sources.length),
            },
            {
              value: 2 as Step,
              label: t('analyticsStepDimensions'),
              done: canContinueStep2,
              hint: String(config.dimensions.length),
            },
            {
              value: 3 as Step,
              label: t('analyticsStepMeasures'),
              done: config.measures.length > 0 || (config.charts?.length ?? 0) > 0,
              hint: String(config.measures.length),
            },
          ] as const
        ).map((item) => (
          <li key={item.value}>
            <button
              type="button"
              className={`${step === item.value ? 'active' : ''}${item.done && step !== item.value ? ' is-done' : ''}`}
              onClick={() => setStep(item.value)}
            >
              <span className="analytics-step-index">{item.done && step !== item.value ? '✓' : item.value}</span>
              <span className="analytics-step-copy">
                <em>{item.label}</em>
                <small>{item.hint}</small>
              </span>
            </button>
          </li>
        ))}
      </ol>

      {saveError && <div className="banner error">{saveError}</div>}
      {datasetsQuery.isError && (
        <div className="banner error">{datasetsQuery.error?.message}</div>
      )}
      {!datasets.length && (
        <div className="banner">{t('analyticsNeedCatalog')}</div>
      )}

      {step === 1 && (
        <SourcesStep
          name={name}
          nameArabic={nameArabic}
          description={description}
          descriptionArabic={descriptionArabic}
          isRestricted={accessMode !== 'Everyone'}
          accessMode={accessMode}
          accessUserIds={accessUserIds}
          accessRoleIds={accessRoleIds}
          people={peopleQuery.data ?? []}
          peopleLoading={peopleQuery.isLoading}
          roles={rolesQuery.data ?? []}
          rolesLoading={rolesQuery.isLoading}
          currentUserId={user?.id}
          config={config}
          datasets={datasets}
          lang={lang}
          t={t}
          onName={setName}
          onNameArabic={setNameArabic}
          onDescription={setDescription}
          onDescriptionArabic={setDescriptionArabic}
          onRestrictedChange={() => undefined}
          onAccessModeChange={setAccessMode}
          onAccessUserIdsChange={setAccessUserIds}
          onAccessRoleIdsChange={setAccessRoleIds}
          onConfig={setConfig}
          onNext={() => canContinueStep1 && setStep(2)}
        />
      )}

      {step === 2 && (
        <DimensionsStep
          config={config}
          datasets={datasets}
          lang={lang}
          t={t}
          onConfig={setConfig}
          onBack={() => setStep(1)}
          onNext={() => canContinueStep2 && setStep(3)}
        />
      )}

      {step === 3 && (
        <MeasuresStep
          config={config}
          datasets={datasets}
          lang={lang}
          t={t}
          onConfig={setConfig}
          onBack={() => setStep(2)}
        />
      )}
    </section>
  )
}

function SourcesStep({
  name,
  nameArabic,
  description,
  descriptionArabic,
  isRestricted,
  accessMode,
  accessUserIds,
  accessRoleIds,
  people,
  peopleLoading,
  roles,
  rolesLoading,
  currentUserId,
  config,
  datasets,
  lang,
  t,
  onName,
  onNameArabic,
  onDescription,
  onDescriptionArabic,
  onRestrictedChange,
  onAccessModeChange,
  onAccessUserIdsChange,
  onAccessRoleIdsChange,
  onConfig,
  onNext,
}: {
  name: string
  nameArabic: string
  description: string
  descriptionArabic: string
  isRestricted: boolean
  accessMode: ReportAccessMode
  accessUserIds: string[]
  accessRoleIds: string[]
  people: import('../api/types').ReportPerson[]
  peopleLoading: boolean
  roles: import('../api/types').ReportAccessRole[]
  rolesLoading: boolean
  currentUserId?: string | null
  config: AnalyticsDashboardConfig
  datasets: Dataset[]
  lang: Lang
  t: ReturnType<typeof useLocale>['t']
  onName: (value: string) => void
  onNameArabic: (value: string) => void
  onDescription: (value: string) => void
  onDescriptionArabic: (value: string) => void
  onRestrictedChange: (value: boolean) => void
  onAccessModeChange: (mode: ReportAccessMode) => void
  onAccessUserIdsChange: (ids: string[]) => void
  onAccessRoleIdsChange: (ids: string[]) => void
  onConfig: (value: AnalyticsDashboardConfig) => void
  onNext: () => void
}) {
  const [datasetId, setDatasetId] = useState('')

  function addSource() {
    if (!datasetId) {
      return
    }
    const dataset = datasets.find((item) => item.id === datasetId)
    if (!dataset) {
      return
    }
    const source: AnalyticsSource = {
      id: newId('src'),
      datasetId: dataset.id,
      alias: datasetLabel(lang, dataset),
    }
    onConfig({ ...config, sources: [...config.sources, source] })
    setDatasetId('')
  }

  function updateSource(id: string, patch: Partial<AnalyticsSource>) {
    onConfig({
      ...config,
      sources: config.sources.map((item) =>
        item.id === id ? { ...item, ...patch } : item,
      ),
    })
  }

  function removeSource(id: string) {
    onConfig({
      ...config,
      sources: config.sources.filter((item) => item.id !== id),
      links: config.links.filter(
        (item) => item.leftSourceId !== id && item.rightSourceId !== id,
      ),
      dimensions: config.dimensions.filter((item) => item.sourceId !== id),
      measures: config.measures.filter((item) => item.sourceId !== id),
      detailColumns: config.detailColumns.filter((item) => item.sourceId !== id),
      charts: (config.charts ?? []).filter((chart) => {
        const dim = config.dimensions.find((item) => item.id === chart.dimensionId)
        const measure = config.measures.find((item) => item.id === chart.measureId)
        return dim?.sourceId !== id && measure?.sourceId !== id
      }),
      map: config.map?.sourceId === id ? null : config.map,
    })
  }

  function addLink() {
    if (config.sources.length < 2) {
      return
    }
    const [left, right] = config.sources
    const link: AnalyticsLink = {
      id: newId('lnk'),
      leftSourceId: left.id,
      rightSourceId: right.id,
      leftKeys: [''],
      rightKeys: [''],
      joinType: 'inner',
    }
    onConfig({ ...config, links: [...config.links, link] })
  }

  function updateLink(id: string, patch: Partial<AnalyticsLink>) {
    onConfig({
      ...config,
      links: config.links.map((item) => (item.id === id ? { ...item, ...patch } : item)),
    })
  }

  function removeLink(id: string) {
    onConfig({ ...config, links: config.links.filter((item) => item.id !== id) })
  }

  return (
    <div className="analytics-panel">
      <div className="analytics-meta-grid">
        <Field label={t('name')}>
          <input value={name} onChange={(event) => onName(event.target.value)} />
        </Field>
        <Field label={t('nameArabic')}>
          <input
            value={nameArabic}
            onChange={(event) => onNameArabic(event.target.value)}
            dir="rtl"
          />
        </Field>
        <Field label={t('description')}>
          <textarea
            rows={2}
            value={description}
            onChange={(event) => onDescription(event.target.value)}
          />
        </Field>
        <Field label={t('descriptionArabic')}>
          <textarea
            rows={2}
            value={descriptionArabic}
            onChange={(event) => onDescriptionArabic(event.target.value)}
            dir="rtl"
          />
        </Field>
      </div>

      <ReportAccessPanel
        isRestricted={isRestricted}
        accessMode={accessMode}
        accessUserIds={accessUserIds}
        people={people}
        loading={peopleLoading}
        currentUserId={currentUserId}
        roles={roles}
        rolesLoading={rolesLoading}
        accessRoleIds={accessRoleIds}
        heading={t('analyticsAccessHeading')}
        hint={t('analyticsAccessHint')}
        onRestrictedChange={onRestrictedChange}
        onAccessModeChange={onAccessModeChange}
        onAccessUserIdsChange={onAccessUserIdsChange}
        onAccessRoleIdsChange={onAccessRoleIdsChange}
      />

      <DesignerSection title={t('analyticsSourcesTitle')} lede={t('analyticsSourcesLede')} count={config.sources.length}>
      <div className="analytics-inline-form">
        <select value={datasetId} onChange={(event) => setDatasetId(event.target.value)}>
          <option value="">{t('analyticsPickDataset')}</option>
          {datasets.map((dataset) => (
            <option key={dataset.id} value={dataset.id}>
              {datasetLabel(lang, dataset)} ({dataset.schemaName}.{dataset.objectName})
            </option>
          ))}
        </select>
        <button type="button" className="btn" disabled={!datasetId} onClick={addSource}>
          {t('analyticsAddSource')}
        </button>
      </div>

      {config.sources.length === 0 ? (
        <div className="analytics-empty-hint">{t('analyticsEmptySources')}</div>
      ) : (
      <div className="analytics-stack">
        {config.sources.map((source) => (
          <div key={source.id} className="analytics-item-row">
            <Field label={t('analyticsAlias')}>
              <input
                value={source.alias}
                onChange={(event) => updateSource(source.id, { alias: event.target.value })}
              />
            </Field>
            <div className="object-name">
              {datasets.find((item) => item.id === source.datasetId)?.schemaName}.
              {datasets.find((item) => item.id === source.datasetId)?.objectName}
            </div>
            <button type="button" className="btn danger" onClick={() => removeSource(source.id)}>
              {t('remove')}
            </button>
          </div>
        ))}
      </div>
      )}
      </DesignerSection>

      <DesignerSection title={t('analyticsLinksTitle')} lede={t('analyticsLinksLede')} count={config.links.length}>

      <RelationshipMap
        config={config}
        datasets={datasets}
        lang={lang}
        emptyLabel={t('analyticsRelationshipEmpty')}
        title={t('analyticsRelationshipMap')}
      />

      <button
        type="button"
        className="btn"
        disabled={config.sources.length < 2}
        onClick={addLink}
      >
        {t('analyticsAddLink')}
      </button>

      <div className="analytics-stack">
        {config.links.map((link) => {
          const pairCount = Math.max(link.leftKeys.length, link.rightKeys.length, 1)
          return (
            <div key={link.id} className="analytics-link-card">
              <div className="analytics-inline-form">
                <select
                  value={link.leftSourceId}
                  onChange={(event) =>
                    updateLink(link.id, {
                      leftSourceId: event.target.value,
                      leftKeys: link.leftKeys.map(() => ''),
                    })
                  }
                >
                  {config.sources.map((source) => (
                    <option key={source.id} value={source.id}>
                      {sourceLabel(source, datasets, lang)}
                    </option>
                  ))}
                </select>
                <select
                  value={link.joinType}
                  onChange={(event) =>
                    updateLink(link.id, { joinType: event.target.value as AnalyticsJoinType })
                  }
                >
                  <option value="inner">INNER</option>
                  <option value="left">LEFT</option>
                </select>
                <select
                  value={link.rightSourceId}
                  onChange={(event) =>
                    updateLink(link.id, {
                      rightSourceId: event.target.value,
                      rightKeys: link.rightKeys.map(() => ''),
                    })
                  }
                >
                  {config.sources.map((source) => (
                    <option key={source.id} value={source.id}>
                      {sourceLabel(source, datasets, lang)}
                    </option>
                  ))}
                </select>
                <button type="button" className="btn danger" onClick={() => removeLink(link.id)}>
                  {t('remove')}
                </button>
              </div>

              {Array.from({ length: pairCount }, (_, index) => (
                <div key={`${link.id}-pair-${index}`} className="analytics-inline-form">
                  <select
                    value={link.leftKeys[index] ?? ''}
                    onChange={(event) => {
                      const leftKeys = [...link.leftKeys]
                      const rightKeys = [...link.rightKeys]
                      while (leftKeys.length <= index) leftKeys.push('')
                      while (rightKeys.length <= index) rightKeys.push('')
                      leftKeys[index] = event.target.value
                      updateLink(link.id, { leftKeys, rightKeys })
                    }}
                  >
                    <option value="">{t('analyticsPickField')}</option>
                    {columnsForSource(link.leftSourceId, config, datasets).map((column) => (
                      <option key={column.columnName} value={column.columnName}>
                        {column.columnName}
                      </option>
                    ))}
                  </select>
                  <span className="muted">=</span>
                  <select
                    value={link.rightKeys[index] ?? ''}
                    onChange={(event) => {
                      const leftKeys = [...link.leftKeys]
                      const rightKeys = [...link.rightKeys]
                      while (leftKeys.length <= index) leftKeys.push('')
                      while (rightKeys.length <= index) rightKeys.push('')
                      rightKeys[index] = event.target.value
                      updateLink(link.id, { leftKeys, rightKeys })
                    }}
                  >
                    <option value="">{t('analyticsPickField')}</option>
                    {columnsForSource(link.rightSourceId, config, datasets).map((column) => (
                      <option key={column.columnName} value={column.columnName}>
                        {column.columnName}
                      </option>
                    ))}
                  </select>
                  {pairCount > 1 && (
                    <button
                      type="button"
                      className="btn"
                      onClick={() => {
                        const leftKeys = link.leftKeys.filter((_, i) => i !== index)
                        const rightKeys = link.rightKeys.filter((_, i) => i !== index)
                        updateLink(link.id, {
                          leftKeys: leftKeys.length ? leftKeys : [''],
                          rightKeys: rightKeys.length ? rightKeys : [''],
                        })
                      }}
                    >
                      {t('analyticsRemoveKeyPair')}
                    </button>
                  )}
                </div>
              ))}

              <button
                type="button"
                className="btn"
                onClick={() =>
                  updateLink(link.id, {
                    leftKeys: [...link.leftKeys, ''],
                    rightKeys: [...link.rightKeys, ''],
                  })
                }
              >
                {t('analyticsAddKeyPair')}
              </button>
            </div>
          )
        })}
      </div>
      {config.links.length === 0 && (
        <div className="analytics-empty-hint">{t('analyticsEmptyLinks')}</div>
      )}
      </DesignerSection>

      <div className="analytics-step-actions">
        <button type="button" className="btn primary" disabled={config.sources.length === 0 || name.trim().length < 2} onClick={onNext}>
          {t('analyticsNext')}
        </button>
      </div>
    </div>
  )
}

function RelationshipMap({
  config,
  datasets,
  lang,
  title,
  emptyLabel,
}: {
  config: AnalyticsDashboardConfig
  datasets: Dataset[]
  lang: Lang
  title: string
  emptyLabel: string
}) {
  const width = Math.max(260, config.sources.length * 118)
  const height = 84
  const nodes = config.sources.map((source, index) => {
    const x =
      config.sources.length === 1
        ? width / 2
        : 52 + (index * (width - 104)) / Math.max(config.sources.length - 1, 1)
    return { source, x, y: height / 2 }
  })
  const byId = Object.fromEntries(nodes.map((node) => [node.source.id, node]))

  return (
    <div className="analytics-rel-map" aria-label={title}>
      <div className="analytics-rel-map-title">{title}</div>
      {config.sources.length < 2 || config.links.length === 0 ? (
        <p className="muted">{emptyLabel}</p>
      ) : (
        <svg viewBox={`0 0 ${width} ${height}`} role="img">
          {config.links.map((link) => {
            const left = byId[link.leftSourceId]
            const right = byId[link.rightSourceId]
            if (!left || !right) {
              return null
            }
            const midY = left.y + (left.x < right.x ? -20 : 20)
            const label = `${link.joinType.toUpperCase()} · ${Math.max(link.leftKeys.filter(Boolean).length, 1)}`
            return (
              <g key={link.id}>
                <path
                  d={`M ${left.x} ${left.y} C ${left.x} ${midY}, ${right.x} ${midY}, ${right.x} ${right.y}`}
                  className="analytics-rel-edge"
                  fill="none"
                />
                <text
                  x={(left.x + right.x) / 2}
                  y={midY + (midY < left.y ? -8 : 16)}
                  textAnchor="middle"
                  className="analytics-rel-edge-label"
                >
                  {label}
                </text>
              </g>
            )
          })}
          {nodes.map((node) => {
            const label = sourceLabel(node.source, datasets, lang)
            const shown = label.length > 14 ? `${label.slice(0, 12)}…` : label
            return (
              <g key={node.source.id}>
                <rect
                  x={node.x - 48}
                  y={node.y - 14}
                  width={96}
                  height={28}
                  rx={8}
                  className="analytics-rel-node"
                />
                <text x={node.x} y={node.y + 4} textAnchor="middle" className="analytics-rel-node-label">
                  {shown}
                  <title>{label}</title>
                </text>
              </g>
            )
          })}
        </svg>
      )}
    </div>
  )
}

function DimensionsStep({
  config,
  datasets,
  lang,
  t,
  onConfig,
  onBack,
  onNext,
}: {
  config: AnalyticsDashboardConfig
  datasets: Dataset[]
  lang: Lang
  t: ReturnType<typeof useLocale>['t']
  onConfig: (value: AnalyticsDashboardConfig) => void
  onBack: () => void
  onNext: () => void
}) {
  const [sourceId, setSourceId] = useState(config.sources[0]?.id ?? '')
  const [field, setField] = useState('')
  const [useExpression, setUseExpression] = useState(false)
  const [expression, setExpression] = useState('')

  const activeSourceId = pickId(sourceId, config.sources)
  const fields = useMemo(
    () => columnsForSource(activeSourceId, config, datasets),
    [activeSourceId, config, datasets],
  )

  function addDimension() {
    if (!activeSourceId) {
      return
    }
    if (useExpression) {
      if (!expression.trim()) {
        return
      }
      const dimension: AnalyticsDimension = {
        id: newId('dim'),
        sourceId: activeSourceId,
        field: '',
        expression: expression.trim(),
        label: expression.trim(),
      }
      onConfig({ ...config, dimensions: [...config.dimensions, dimension] })
      setExpression('')
      return
    }
    if (!field) {
      return
    }
    const column = fields.find((item) => item.columnName === field)
    const dimension: AnalyticsDimension = {
      id: newId('dim'),
      sourceId: activeSourceId,
      field,
      expression: null,
      label: column ? localizedText(lang, column.displayName, column.displayNameArabic) : field,
    }
    onConfig({ ...config, dimensions: [...config.dimensions, dimension] })
    setField('')
  }

  function updateDimension(id: string, patch: Partial<AnalyticsDimension>) {
    onConfig({
      ...config,
      dimensions: config.dimensions.map((item) =>
        item.id === id ? { ...item, ...patch } : item,
      ),
    })
  }

  function removeDimension(id: string) {
    onConfig({
      ...config,
      dimensions: config.dimensions.filter((item) => item.id !== id),
      charts: (config.charts ?? []).filter((item) => item.dimensionId !== id),
    })
  }

  return (
    <div className="analytics-panel">
      <DesignerSection title={t('analyticsDimensionsTitle')} lede={t('analyticsDimensionsLede')} count={config.dimensions.length}>
      <label className="analytics-check">
        <input
          type="checkbox"
          checked={useExpression}
          onChange={(event) => setUseExpression(event.target.checked)}
        />
        {t('analyticsUseExpression')}
      </label>
      <div className="analytics-inline-form">
        <Field label={t('analyticsFieldSource')}>
          <select value={activeSourceId} onChange={(event) => { setSourceId(event.target.value); setField('') }}>
            {config.sources.map((source) => (
              <option key={source.id} value={source.id}>
                {sourceLabel(source, datasets, lang)}
              </option>
            ))}
          </select>
        </Field>
        {useExpression ? (
          <Field label={t('analyticsExpression')}>
            <input
              className="analytics-expr-input"
              value={expression}
              placeholder="YEAR([OrderDate])"
              onChange={(event) => setExpression(event.target.value)}
            />
          </Field>
        ) : (
          <Field label={t('analyticsFieldColumn')}>
            <select value={field} onChange={(event) => setField(event.target.value)}>
              <option value="">{t('analyticsPickField')}</option>
              {fields.map((column) => (
                <option key={column.columnName} value={column.columnName}>
                  {column.columnName}
                </option>
              ))}
            </select>
          </Field>
        )}
        <button
          type="button"
          className="btn"
          disabled={useExpression ? !expression.trim() : !field}
          onClick={addDimension}
        >
          {t('analyticsAddDimension')}
        </button>
      </div>
      {useExpression && <p className="muted">{t('analyticsExpressionHint')}</p>}

      {config.dimensions.length > 0 ? (
      <SortableStack
        items={config.dimensions}
        onReorder={(activeId, overId) =>
          onConfig({
            ...config,
            dimensions: reorderById(config.dimensions, activeId, overId),
          })
        }
      >
        {config.dimensions.map((dimension) => {
          const source = config.sources.find((item) => item.id === dimension.sourceId)
          const path = source
            ? `${sourceLabel(source, datasets, lang)}.${dimension.field}`
            : dimension.field
          return (
          <SortableItemRow key={dimension.id} id={dimension.id}>
            <input
              value={dimension.label}
              aria-label={t('analyticsLabel')}
              onChange={(event) => updateDimension(dimension.id, { label: event.target.value })}
            />
            {dimension.expression ? (
              <input
                className="analytics-sort-expr"
                value={dimension.expression}
                aria-label={t('analyticsExpression')}
                onChange={(event) =>
                  updateDimension(dimension.id, { expression: event.target.value, field: '' })
                }
              />
            ) : (
              <span className="object-name" title={path}>{path}</span>
            )}
            <button type="button" className="link danger" onClick={() => removeDimension(dimension.id)}>
              {t('remove')}
            </button>
          </SortableItemRow>
          )
        })}
      </SortableStack>
      ) : (
        <div className="analytics-empty-hint">{t('analyticsEmptyDimensions')}</div>
      )}
      </DesignerSection>

      <div className="analytics-step-actions">
        <button type="button" className="btn" onClick={onBack}>
          {t('analyticsBack')}
        </button>
        <button
          type="button"
          className="btn primary"
          disabled={config.dimensions.length === 0}
          onClick={onNext}
        >
          {t('analyticsNext')}
        </button>
      </div>
    </div>
  )
}

function MeasuresStep({
  config,
  datasets,
  lang,
  t,
  onConfig,
  onBack,
}: {
  config: AnalyticsDashboardConfig
  datasets: Dataset[]
  lang: Lang
  t: ReturnType<typeof useLocale>['t']
  onConfig: (value: AnalyticsDashboardConfig) => void
  onBack: () => void
}) {
  const [sourceId, setSourceId] = useState(config.sources[0]?.id ?? '')
  const [field, setField] = useState('')
  const [aggregation, setAggregation] = useState<AnalyticsAggregation>('count')
  const [format, setFormat] = useState('')
  const [expression, setExpression] = useState('')
  const [detailSourceId, setDetailSourceId] = useState(config.sources[0]?.id ?? '')
  const [detailField, setDetailField] = useState('')
  const [chartType, setChartType] = useState<AnalyticsChartType>('bar')
  const [chartDimensionId, setChartDimensionId] = useState(config.dimensions[0]?.id ?? '')
  const [chartMeasureId, setChartMeasureId] = useState(config.measures[0]?.id ?? '')
  const [chartState, setChartState] = useState<AnalyticsSelectionState>('default')
  const [mapSourceId, setMapSourceId] = useState(config.map?.sourceId || config.sources[0]?.id || '')
  const [mapLat, setMapLat] = useState(config.map?.latField ?? '')
  const [mapLng, setMapLng] = useState(config.map?.lngField ?? '')
  const [mapLabel, setMapLabel] = useState(config.map?.labelField ?? '')

  const activeSourceId = pickId(sourceId, config.sources)
  const activeDetailSourceId = pickId(detailSourceId, config.sources)
  const activeChartDimensionId = pickId(chartDimensionId, config.dimensions)
  const activeChartMeasureId = pickId(chartMeasureId, config.measures)
  const activeMapSourceId = pickId(mapSourceId, config.sources)

  const fields = useMemo(
    () => columnsForSource(activeSourceId, config, datasets),
    [activeSourceId, config, datasets],
  )
  const detailFields = useMemo(
    () => columnsForSource(activeDetailSourceId, config, datasets),
    [activeDetailSourceId, config, datasets],
  )

  function addMeasure() {
    if (!activeSourceId) {
      return
    }
    if (aggregation === 'expr') {
      if (!expression.trim()) {
        return
      }
      const measure: AnalyticsMeasure = {
        id: newId('kpi'),
        sourceId: activeSourceId,
        field: null,
        expression: expression.trim(),
        aggregation: 'expr',
        label: expression.trim(),
        format: format || null,
      }
      onConfig({ ...config, measures: [...config.measures, measure] })
      setExpression('')
      return
    }
    if (aggregation !== 'count' && !field) {
      return
    }
    const column = fields.find((item) => item.columnName === field)
    const measure: AnalyticsMeasure = {
      id: newId('kpi'),
      sourceId: activeSourceId,
      field: aggregation === 'count' ? field || null : field,
      expression: null,
      aggregation,
      format: format || null,
      label:
        aggregation === 'count' && !field
          ? t('analyticsCountAll')
          : `${aggregation.toUpperCase()}(${column?.columnName ?? field})`,
    }
    onConfig({ ...config, measures: [...config.measures, measure] })
    setField('')
  }

  function updateMeasure(id: string, patch: Partial<AnalyticsMeasure>) {
    onConfig({
      ...config,
      measures: config.measures.map((item) =>
        item.id === id ? { ...item, ...patch } : item,
      ),
    })
  }

  function removeMeasure(id: string) {
    onConfig({
      ...config,
      measures: config.measures.filter((item) => item.id !== id),
      charts: (config.charts ?? []).filter((item) => item.measureId !== id),
    })
  }

  function addDetailColumn() {
    if (!activeDetailSourceId || !detailField) {
      return
    }
    const column = detailFields.find((item) => item.columnName === detailField)
    const detail: AnalyticsDetailColumn = {
      id: newId('col'),
      sourceId: activeDetailSourceId,
      field: detailField,
      label: column
        ? localizedText(lang, column.displayName, column.displayNameArabic)
        : detailField,
    }
    onConfig({ ...config, detailColumns: [...config.detailColumns, detail] })
    setDetailField('')
  }

  function updateDetail(id: string, patch: Partial<AnalyticsDetailColumn>) {
    onConfig({
      ...config,
      detailColumns: config.detailColumns.map((item) =>
        item.id === id ? { ...item, ...patch } : item,
      ),
    })
  }

  function removeDetail(id: string) {
    onConfig({
      ...config,
      detailColumns: config.detailColumns.filter((item) => item.id !== id),
    })
  }

  function addChart() {
    if (!activeChartDimensionId || !activeChartMeasureId) {
      return
    }
    const dimension = config.dimensions.find((item) => item.id === activeChartDimensionId)
    const measure = config.measures.find((item) => item.id === activeChartMeasureId)
    const chart: AnalyticsChart = {
      id: newId('cht'),
      type: chartType,
      dimensionId: activeChartDimensionId,
      measureId: activeChartMeasureId,
      state: chartState,
      title: `${measure?.label ?? 'KPI'} by ${dimension?.label ?? 'dimension'}`,
    }
    onConfig({ ...config, charts: [...(config.charts ?? []), chart] })
  }

  function updateChart(id: string, patch: Partial<AnalyticsChart>) {
    onConfig({
      ...config,
      charts: (config.charts ?? []).map((item) => (item.id === id ? { ...item, ...patch } : item)),
    })
  }

  function removeChart(id: string) {
    onConfig({
      ...config,
      charts: (config.charts ?? []).filter((item) => item.id !== id),
    })
  }

  const mapFields = useMemo(
    () => columnsForSource(activeMapSourceId, config, datasets),
    [activeMapSourceId, config, datasets],
  )

  function applyMap() {
    if (!activeMapSourceId || !mapLat || !mapLng) {
      onConfig({ ...config, map: null })
      return
    }
    onConfig({
      ...config,
      map: {
        enabled: true,
        sourceId: activeMapSourceId,
        latField: mapLat,
        lngField: mapLng,
        labelField: mapLabel || null,
      },
    })
  }

  return (
    <div className="analytics-panel">
      <DesignerSection title={t('analyticsMeasuresTitle')} lede={t('analyticsMeasuresLede')} count={config.measures.length}>
      <div className="analytics-inline-form">
        <Field label={t('analyticsFieldSource')}>
          <select value={activeSourceId} onChange={(event) => { setSourceId(event.target.value); setField('') }}>
            {config.sources.map((source) => (
              <option key={source.id} value={source.id}>
                {sourceLabel(source, datasets, lang)}
              </option>
            ))}
          </select>
        </Field>
        <Field label={t('analyticsFieldAggregation')}>
          <select
            value={aggregation}
            onChange={(event) => setAggregation(event.target.value as AnalyticsAggregation)}
          >
            <option value="count">COUNT</option>
            <option value="sum">SUM</option>
            <option value="avg">AVG</option>
            <option value="min">MIN</option>
            <option value="max">MAX</option>
            <option value="expr">{t('analyticsAggExpr')}</option>
          </select>
        </Field>
        {aggregation === 'expr' ? (
          <Field label={t('analyticsExpression')}>
            <input
              className="analytics-expr-input"
              value={expression}
              placeholder="SUM([Amount]) / NULLIF(COUNT([Id]), 0)"
              onChange={(event) => setExpression(event.target.value)}
            />
          </Field>
        ) : (
          <Field label={t('analyticsFieldColumn')}>
            <select value={field} onChange={(event) => setField(event.target.value)}>
              <option value="">{t('analyticsPickField')}</option>
              {fields.map((column) => (
                <option key={column.columnName} value={column.columnName}>
                  {column.columnName}
                </option>
              ))}
            </select>
          </Field>
        )}
        <Field label={t('analyticsFormat')}>
          <select value={format} onChange={(event) => setFormat(event.target.value)}>
            <option value="">{t('analyticsFormatDefault')}</option>
            <option value="N0">{t('analyticsFormatInteger')}</option>
            <option value="N2">{t('analyticsFormatDecimal')}</option>
            <option value="P1">{t('analyticsFormatPercent')}</option>
            <option value="aed">{t('analyticsFormatAed')}</option>
          </select>
        </Field>
        <button
          type="button"
          className="btn"
          disabled={
            aggregation === 'expr'
              ? !expression.trim()
              : aggregation !== 'count' && !field
          }
          onClick={addMeasure}
        >
          {t('analyticsAddMeasure')}
        </button>
      </div>
      {aggregation === 'expr' && <p className="muted">{t('analyticsMeasureExprHint')}</p>}

      {config.measures.length > 0 ? (
      <SortableStack
        items={config.measures}
        onReorder={(activeId, overId) =>
          onConfig({
            ...config,
            measures: reorderById(config.measures, activeId, overId),
          })
        }
      >
        {config.measures.map((measure) => (
          <SortableItemRow key={measure.id} id={measure.id}>
            <input
              value={measure.label}
              aria-label={t('analyticsLabel')}
              onChange={(event) => updateMeasure(measure.id, { label: event.target.value })}
            />
            <select
              value={measure.format ?? ''}
              aria-label={t('analyticsFormat')}
              onChange={(event) =>
                updateMeasure(measure.id, { format: event.target.value || null })
              }
            >
              <option value="">{t('analyticsFormatDefault')}</option>
              <option value="N0">{t('analyticsFormatInteger')}</option>
              <option value="N2">{t('analyticsFormatDecimal')}</option>
              <option value="P1">{t('analyticsFormatPercent')}</option>
              <option value="aed">{t('analyticsFormatAed')}</option>
            </select>
            {measure.aggregation === 'expr' || measure.expression ? (
              <input
                className="analytics-sort-expr"
                value={measure.expression ?? ''}
                aria-label={t('analyticsExpression')}
                onChange={(event) =>
                  updateMeasure(measure.id, {
                    expression: event.target.value,
                    aggregation: 'expr',
                    field: null,
                  })
                }
              />
            ) : (
              <span className="object-name">
                {measure.aggregation.toUpperCase()}
                ({measure.field || '*'})
              </span>
            )}
            <button type="button" className="link danger" onClick={() => removeMeasure(measure.id)}>
              {t('remove')}
            </button>
          </SortableItemRow>
        ))}
      </SortableStack>
      ) : (
        <div className="analytics-empty-hint">{t('analyticsEmptyMeasures')}</div>
      )}
      </DesignerSection>

      <DesignerSection title={t('analyticsDetailTitle')} lede={t('analyticsDetailLede')} count={config.detailColumns.length}>
      <div className="analytics-inline-form">
        <Field label={t('analyticsFieldSource')}>
          <select
            value={activeDetailSourceId}
            onChange={(event) => {
              setDetailSourceId(event.target.value)
              setDetailField('')
            }}
          >
            {config.sources.map((source) => (
              <option key={source.id} value={source.id}>
                {sourceLabel(source, datasets, lang)}
              </option>
            ))}
          </select>
        </Field>
        <Field label={t('analyticsFieldColumn')}>
          <select value={detailField} onChange={(event) => setDetailField(event.target.value)}>
            <option value="">{t('analyticsPickField')}</option>
            {detailFields.map((column) => (
              <option key={column.columnName} value={column.columnName}>
                {column.columnName}
              </option>
            ))}
          </select>
        </Field>
        <button type="button" className="btn" disabled={!detailField} onClick={addDetailColumn}>
          {t('analyticsAddDetailColumn')}
        </button>
      </div>

      {config.detailColumns.length > 0 ? (
      <SortableStack
        items={config.detailColumns}
        onReorder={(activeId, overId) =>
          onConfig({
            ...config,
            detailColumns: reorderById(config.detailColumns, activeId, overId),
          })
        }
      >
        {config.detailColumns.map((column) => (
          <SortableItemRow key={column.id} id={column.id}>
            <input
              value={column.label}
              aria-label={t('analyticsLabel')}
              onChange={(event) => updateDetail(column.id, { label: event.target.value })}
            />
            <input
              value={column.urlTemplate ?? ''}
              placeholder="https://example/{Field}"
              aria-label={t('analyticsRowUrl')}
              onChange={(event) =>
                updateDetail(column.id, { urlTemplate: event.target.value || null })
              }
            />
            <span className="object-name" title={column.field}>{column.field}</span>
            <button type="button" className="link danger" onClick={() => removeDetail(column.id)}>
              {t('remove')}
            </button>
          </SortableItemRow>
        ))}
      </SortableStack>
      ) : (
        <div className="analytics-empty-hint">{t('analyticsEmptyDetail')}</div>
      )}
      </DesignerSection>

      <DesignerSection title={t('analyticsChartsTitle')} lede={t('analyticsChartsLede')} count={(config.charts ?? []).length}>
      <div className="analytics-inline-form">
        <Field label={t('analyticsFieldChartType')}>
          <select
            value={chartType}
            onChange={(event) => setChartType(event.target.value as AnalyticsChartType)}
          >
            <option value="bar">{t('analyticsChartBar')}</option>
            <option value="line">{t('analyticsChartLine')}</option>
            <option value="pie">{t('analyticsChartPie')}</option>
          </select>
        </Field>
        <Field label={t('analyticsFieldDimension')}>
          <select value={activeChartDimensionId} onChange={(event) => setChartDimensionId(event.target.value)}>
            {config.dimensions.map((dimension) => (
              <option key={dimension.id} value={dimension.id}>
                {dimension.label}
              </option>
            ))}
          </select>
        </Field>
        <Field label={t('analyticsFieldMeasure')}>
          <select value={activeChartMeasureId} onChange={(event) => setChartMeasureId(event.target.value)}>
            {config.measures.map((measure) => (
              <option key={measure.id} value={measure.id}>
                {measure.label}
              </option>
            ))}
          </select>
        </Field>
        <Field label={t('analyticsFieldState')}>
          <select
            value={chartState}
            onChange={(event) => setChartState(event.target.value as AnalyticsSelectionState)}
          >
            <option value="default">{t('analyticsStateDefault')}</option>
            <option value="compare">{t('analyticsStateCompare')}</option>
          </select>
        </Field>
        <button
          type="button"
          className="btn"
          disabled={!activeChartDimensionId || !activeChartMeasureId}
          onClick={addChart}
        >
          {t('analyticsAddChart')}
        </button>
      </div>
      <div className="analytics-stack">
        {(config.charts ?? []).map((chart) => (
          <div key={chart.id} className="analytics-item-row analytics-item-row-wide">
            <Field label={t('analyticsLabel')}>
              <input
                value={chart.title}
                onChange={(event) => updateChart(chart.id, { title: event.target.value })}
              />
            </Field>
            <div className="object-name">
              {chart.type.toUpperCase()} · {chart.state === 'compare' ? 'B' : 'A'}
            </div>
            <button type="button" className="btn danger" onClick={() => removeChart(chart.id)}>
              {t('remove')}
            </button>
          </div>
        ))}
      </div>
      {(config.charts ?? []).length === 0 && (
        <div className="analytics-empty-hint">{t('analyticsEmptyCharts')}</div>
      )}
      </DesignerSection>

      <DesignerSection title={t('analyticsMapTitle')} lede={t('analyticsMapLede')} count={config.map?.enabled ? 1 : 0}>
      <div className="analytics-inline-form">
        <Field label={t('analyticsFieldSource')}>
          <select
            value={activeMapSourceId}
            onChange={(event) => {
              setMapSourceId(event.target.value)
              setMapLat('')
              setMapLng('')
              setMapLabel('')
            }}
          >
            {config.sources.map((source) => (
              <option key={source.id} value={source.id}>
                {sourceLabel(source, datasets, lang)}
              </option>
            ))}
          </select>
        </Field>
        <Field label={t('analyticsFieldLat')}>
          <select value={mapLat} onChange={(event) => setMapLat(event.target.value)}>
            <option value="">{t('analyticsMapLat')}</option>
            {mapFields.map((column) => (
              <option key={column.columnName} value={column.columnName}>
                {column.columnName}
              </option>
            ))}
          </select>
        </Field>
        <Field label={t('analyticsFieldLng')}>
          <select value={mapLng} onChange={(event) => setMapLng(event.target.value)}>
            <option value="">{t('analyticsMapLng')}</option>
            {mapFields.map((column) => (
              <option key={column.columnName} value={column.columnName}>
                {column.columnName}
              </option>
            ))}
          </select>
        </Field>
        <Field label={t('analyticsLabel')}>
          <select value={mapLabel} onChange={(event) => setMapLabel(event.target.value)}>
            <option value="">{t('analyticsMapLabel')}</option>
            {mapFields.map((column) => (
              <option key={column.columnName} value={column.columnName}>
                {column.columnName}
              </option>
            ))}
          </select>
        </Field>
        <button type="button" className="btn" disabled={!mapLat || !mapLng} onClick={applyMap}>
          {t('analyticsSaveMap')}
        </button>
        {config.map?.enabled && (
          <button type="button" className="btn danger" onClick={() => onConfig({ ...config, map: null })}>
            {t('remove')}
          </button>
        )}
      </div>
      {config.map?.enabled && (
        <p className="muted">
          {config.map.latField}, {config.map.lngField}
          {config.map.labelField ? ` · ${config.map.labelField}` : ''}
        </p>
      )}
      </DesignerSection>

      <div className="analytics-step-actions">
        <button type="button" className="btn" onClick={onBack}>
          {t('analyticsBack')}
        </button>
      </div>
    </div>
  )
}

function DesignerSection({
  title,
  lede,
  count,
  children,
}: {
  title: string
  lede?: string
  count?: number
  children: ReactNode
}) {
  return (
    <section className="analytics-section-card">
      <header className="analytics-section-card-head">
        <div>
          <h2>{title}</h2>
          {lede && <p className="lede">{lede}</p>}
        </div>
        {count != null && <span className="analytics-count-pill">{count}</span>}
      </header>
      <div className="analytics-section-card-body">{children}</div>
    </section>
  )
}
