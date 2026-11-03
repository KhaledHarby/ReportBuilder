import {
  DndContext,
  DragOverlay,
  PointerSensor,
  pointerWithin,
  rectIntersection,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type CollisionDetection,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import {
  SortableContext,
  arrayMove,
  horizontalListSortingStrategy,
  useSortable,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Link, Navigate, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import type {
  AiAssistResult,
  Dataset,
  DatasetColumn,
  ExportFormat,
  LayoutColumn,
  LayoutFilter,
  ReportAccessMode,
  ReportLayout,
  ReportStatus,
} from '../api/types'
import { emptyLayout } from '../api/types'
import { Field } from '../components/AdminFields'
import { AiAssistPanel } from '../components/AiAssistPanel'
import { ChartDesignerCanvas } from '../components/ChartDesignerCanvas'
import { ChartView } from '../components/ChartView'
import { CalcColumnDialog } from '../components/CalcColumnDialog'
import { ColumnPropsPanel } from '../components/ColumnPropsPanel'
import { DatasetPicker } from '../components/DatasetPicker'
import { DesignerResizer } from '../components/DesignerResizer'
import { ExportMenu } from '../components/ExportMenu'
import { EmbedUrlDialog } from '../components/EmbedUrlDialog'
import { PublishHistoryDialog } from '../components/PublishHistoryDialog'
import { FilterLabelButton } from '../components/FilterLabelButton'
import { FilterOptionsButton } from '../components/FilterOptionsButton'
import { FilterLayoutSettings } from '../components/FilterLayoutSettings'
import { FilterValueField } from '../components/FilterValueField'
import { ReportAccessPanel } from '../components/ReportAccessPanel'
import { UserScopePanel } from '../components/UserScopePanel'
import { DetailPanel } from '../components/DetailPanel'
import { DetailGrid } from '../components/DetailGrid'
import { ResultGrid } from '../components/ResultGrid'
import {
  IconColumns,
  IconFilter,
  IconGroup,
  IconSort,
  IconTrash,
  typeTone,
} from '../components/Icons'
import {
  clearDescendantFilterValues,
  filterDisplayLabel,
  FILTER_OPERATORS,
  missingRequiredFilters,
  needsFilterValue,
  operatorMessageKey,
} from '../lib/filters'
import { emptyChartLayout, isChartLayout, normalizeLayout } from '../lib/chart'
import { sortDatasetsForDesigner } from '../lib/datasets'
import { columnForField, defaultSortDirection } from '../lib/columns'
import { isLookupField, withLookupColumns } from '../lib/lookupColumns'
import { usePageTitle } from '../lib/pageTitle'
import {
  allowedDrillthroughFields,
  reportIdFromUrlTemplate,
} from '../lib/reportLinks'
import { buildFormatMap, buildUrlMap, fieldToResultName, formatOptionsForSqlType, urlTokensForResult } from '../lib/columnFormat'
import { buildConditionalFormatMap } from '../lib/conditionalFormat'
import {
  isUserScopeConfigured,
  propertiesForSqlType,
} from '../lib/userScope'
import { isDetailConfigured } from '../lib/detailLayout'
import { useDesignerLayoutSizes } from '../hooks/useDesignerLayoutSizes'
import { useAuth } from '../auth/AuthProvider'
import { useLocale } from '../i18n/LocaleProvider'
import { columnLabel, columnPresentation, layoutForLang, localizeResultColumns, localizedText, resultForLang } from '../i18n/localized'
import { bilingualExportName } from '../lib/download'
import type { Lang } from '../i18n/locale'

const NUMERIC_TYPES = new Set([
  'int',
  'bigint',
  'smallint',
  'tinyint',
  'decimal',
  'numeric',
  'money',
  'smallmoney',
  'float',
  'real',
])

function isNumeric(sqlType: string) {
  return NUMERIC_TYPES.has(sqlType.toLowerCase())
}

function visibleColumns(dataset: Dataset | undefined) {
  return (dataset?.columns ?? [])
    .filter((column) => !column.isHidden)
    .slice()
    .sort((a, b) => a.ordinal - b.ordinal)
}

function fieldLabel(
  lang: Lang,
  palette: DatasetColumn[],
  field: string,
  alias?: string | null,
) {
  const column = palette.find(
    (item) => item.columnName.toLowerCase() === field.toLowerCase(),
  )
  if (alias?.trim()) {
    return alias.trim()
  }
  if (!column) {
    return field
  }
  return columnLabel(lang, column)
}

function dropUnknownFields(
  layout: ReportLayout,
  datasetColumns: DatasetColumn[],
): ReportLayout {
  const allowed = new Set(
    datasetColumns.map((column) => column.columnName.toLowerCase()),
  )
  return {
    ...layout,
    columns: layout.columns.filter(
      (column) =>
        Boolean(column.expression?.trim()) ||
        allowed.has(column.field.toLowerCase()) ||
        isLookupField(column.field),
    ),
    groupBy: layout.groupBy.filter((field) => allowed.has(field.toLowerCase())),
    filters: layout.filters.filter(
      (filter) =>
        allowed.has(filter.field.toLowerCase()) || isLookupField(filter.field),
    ),
    orderBy: layout.orderBy.filter((item) =>
      allowed.has(item.field.toLowerCase()),
    ),
  }
}

function readAiDraft(state: unknown): AiAssistResult | undefined {
  return (state as { aiDraft?: AiAssistResult } | null)?.aiDraft
}

function applyGrouping(
  layout: ReportLayout,
  datasetColumns: DatasetColumn[],
): ReportLayout {
  if (layout.groupBy.length === 0) {
    return {
      ...layout,
      columns: layout.columns.map((column) => ({
        ...column,
        aggregate: undefined,
      })),
    }
  }

  return {
    ...layout,
    columns: layout.columns.map((column) => {
      if (column.expression?.trim()) {
        return column
      }
      const grouped = layout.groupBy.some(
        (field) => field.toLowerCase() === column.field.toLowerCase(),
      )
      if (grouped) {
        return { ...column, aggregate: undefined }
      }
      if (column.aggregate) {
        return column
      }
      const meta = datasetColumns.find(
        (item) => item.columnName.toLowerCase() === column.field.toLowerCase(),
      )
      return {
        ...column,
        aggregate: meta && isNumeric(meta.sqlType) ? 'SUM' : 'COUNT',
      }
    }),
  }
}

const DROP_ZONES = new Set(['columns', 'groupBy', 'filters', 'sort'])

function firstHits(
  hits: ReturnType<typeof pointerWithin>,
): ReturnType<typeof pointerWithin> | null {
  return hits.length > 0 ? hits : null
}

const detectDropTarget: CollisionDetection = (args) => {
  const kind = args.active.data.current?.kind
  const zoneContainers = args.droppableContainers.filter((container) =>
    DROP_ZONES.has(String(container.id)),
  )

  if (kind === 'palette') {
    return (
      firstHits(pointerWithin({ ...args, droppableContainers: zoneContainers })) ??
      firstHits(rectIntersection({ ...args, droppableContainers: zoneContainers })) ??
      []
    )
  }

  if (kind === 'column') {
    const chipContainers = args.droppableContainers.filter(
      (container) =>
        !DROP_ZONES.has(String(container.id)) && container.id !== args.active.id,
    )
    return (
      firstHits(pointerWithin({ ...args, droppableContainers: chipContainers })) ??
      firstHits(pointerWithin({ ...args, droppableContainers: zoneContainers })) ??
      firstHits(rectIntersection({ ...args, droppableContainers: zoneContainers })) ??
      []
    )
  }

  if (kind === 'filter') {
    const filterContainers = args.droppableContainers.filter((container) =>
      String(container.id).startsWith('filter:'),
    )
    return (
      firstHits(pointerWithin({ ...args, droppableContainers: filterContainers })) ??
      firstHits(pointerWithin({ ...args, droppableContainers: zoneContainers })) ??
      firstHits(rectIntersection({ ...args, droppableContainers: zoneContainers })) ??
      []
    )
  }

  return []
}

export function DesignerPage() {
  const { can, isAdmin, user } = useAuth()
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const { id } = useParams()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const location = useLocation()
  const isNew = !id
  const datasetsQuery = useQuery({
    queryKey: ['datasets', 'enabled'],
    queryFn: () => api.listDatasets(true),
  })
  const reportQuery = useQuery({
    queryKey: ['report', id],
    queryFn: () => api.getReport(id!),
    enabled: Boolean(id),
  })
  const peopleQuery = useQuery({
    queryKey: ['report-people'],
    queryFn: api.listReportPeople,
  })
  const rolesQuery = useQuery({
    queryKey: ['report-access-roles'],
    queryFn: api.listReportAccessRoles,
  })
  const categoriesQuery = useQuery({
    queryKey: ['report-categories'],
    queryFn: api.listReportCategories,
  })
  const reportsQuery = useQuery({
    queryKey: ['reports'],
    queryFn: api.listReports,
  })

  const [name, setName] = useState(() => readAiDraft(location.state)?.name ?? '')
  const [nameArabic, setNameArabic] = useState(() => readAiDraft(location.state)?.nameArabic ?? '')
  usePageTitle(localizedText(lang, name, nameArabic) || (isNew ? t('newReport') : null))
  const [description, setDescription] = useState(
    () => readAiDraft(location.state)?.description ?? '',
  )
  const [descriptionArabic, setDescriptionArabic] = useState(
    () => readAiDraft(location.state)?.descriptionArabic ?? '',
  )
  const [datasetId, setDatasetId] = useState(() => readAiDraft(location.state)?.datasetId ?? '')
  const [categoryId, setCategoryId] = useState('')
  const [layout, setLayout] = useState<ReportLayout>(() => {
    const draft = readAiDraft(location.state)
    if (draft) {
      return normalizeLayout(draft.layout)
    }
    return searchParams.get('kind') === 'chart' ? emptyChartLayout() : emptyLayout()
  })
  const [selectedField, setSelectedField] = useState<string | null>(null)
  const [calcLabel, setCalcLabel] = useState('')
  const [calcExpression, setCalcExpression] = useState('')
  const [calcOpen, setCalcOpen] = useState(false)
  const [calcEditing, setCalcEditing] = useState<string | null>(null)
  const linkedReportId = reportIdFromUrlTemplate(
    layout.columns.find((column) => column.field === selectedField)?.urlTemplate,
  )
  const linkedReportQuery = useQuery({
    queryKey: ['report', linkedReportId],
    queryFn: () => api.getReport(linkedReportId),
    enabled: Boolean(linkedReportId),
  })
  const [skip, setSkip] = useState(0)
  const [take] = useState(50)
  const isChart = isChartLayout(layout)
  const previewTake = isChart ? 200 : take
  const [preview, setPreview] = useState<Awaited<
    ReturnType<typeof api.preview>
  > | null>(null)
  const [previewError, setPreviewError] = useState<string | null>(null)
  const [previewLoading, setPreviewLoading] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<{
    name?: string
    nameArabic?: string
    columns?: string
    category?: string
    reportCategory?: string
    measures?: string
    userScopeField?: string
    userScopeProperty?: string
  }>({})
  const [justSaved, setJustSaved] = useState<string | null>(null)
  const [historyOpen, setHistoryOpen] = useState(false)
  const [embedOpen, setEmbedOpen] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [hydrated, setHydrated] = useState(isNew)
  const [status, setStatus] = useState<ReportStatus>('draft')
  const [accessMode, setAccessMode] = useState<ReportAccessMode>('Everyone')
  const [accessUserIds, setAccessUserIds] = useState<string[]>([])
  const [accessRoleIds, setAccessRoleIds] = useState<string[]>([])
  const previousId = useRef(id)
  const [activeDrag, setActiveDrag] = useState<{
    kind: string
    field: string
    label: string
  } | null>(null)

  const stageRef = useRef<HTMLDivElement>(null)
  const { resizeDataset, resizePreview } = useDesignerLayoutSizes(stageRef)

  const designerDatasets = useMemo(
    () => sortDatasetsForDesigner(datasetsQuery.data ?? [], lang),
    [datasetsQuery.data, lang],
  )
  const dataset = designerDatasets.find((item) => item.id === datasetId)
  const palette = withLookupColumns(visibleColumns(dataset))

  useEffect(() => {
    if (!dataset) {
      return
    }
    setLayout((current) => {
      const next = dropUnknownFields(current, withLookupColumns(visibleColumns(dataset)))
      const unchanged =
        next.columns.length === current.columns.length &&
        next.groupBy.length === current.groupBy.length &&
        next.filters.length === current.filters.length &&
        next.orderBy.length === current.orderBy.length
      return unchanged ? current : next
    })
  }, [dataset])

  const applySuggestion = (suggestion: AiAssistResult) => {
    const nextDataset = datasetsQuery.data?.find((item) => item.id === suggestion.datasetId)
    const nextLayout = normalizeLayout(suggestion.layout)
    setName(suggestion.name || t('untitledReport'))
    setNameArabic(suggestion.nameArabic ?? '')
    setDescription(suggestion.description ?? '')
    setDescriptionArabic(suggestion.descriptionArabic ?? '')
    setDatasetId(suggestion.datasetId)
    setLayout(applyGrouping(nextLayout, withLookupColumns(visibleColumns(nextDataset))))
    setSelectedField(null)
  }

  useEffect(() => {
    const wasEditing = Boolean(previousId.current)
    previousId.current = id
    setHydrated(!id)
    setSaveError(null)
    setPreview(null)
    setSelectedField(null)
    if (id) {
      return
    }

    const incoming = readAiDraft(location.state)
    if (incoming) {
      applySuggestion(incoming)
      navigate('.', { replace: true, state: {} })
      return
    }

    if (wasEditing) {
      const chart = searchParams.get('kind') === 'chart'
      setName('')
      setNameArabic('')
      setDescription('')
      setDescriptionArabic('')
      setLayout(chart ? emptyChartLayout() : emptyLayout())
      setDatasetId('')
      setCategoryId('')
      setStatus('draft')
      setAccessMode('Everyone')
      setAccessUserIds([])
      setAccessRoleIds([])
    }
  }, [id])

  useEffect(() => {
    if (!reportQuery.data || hydrated) {
      return
    }
    const source = reportQuery.data.draft ?? reportQuery.data
    setName(source.name)
    setNameArabic(source.nameArabic ?? '')
    setDescription(source.description ?? '')
    setDescriptionArabic(source.descriptionArabic ?? '')
    setDatasetId(source.datasetId)
    setCategoryId(source.categoryId ?? '')
    setLayout(normalizeLayout(source.layout))
    setStatus(reportQuery.data.status === 'published' ? 'published' : 'draft')
    const nextMode =
      source.accessMode ??
      reportQuery.data.accessMode ??
      ((source.isRestricted ?? reportQuery.data.isRestricted) ? 'People' : 'Everyone')
    setAccessMode(nextMode)
    setAccessUserIds(source.accessUserIds ?? reportQuery.data.accessUserIds ?? [])
    setAccessRoleIds(source.accessRoleIds ?? reportQuery.data.accessRoleIds ?? [])
    setHydrated(true)
  }, [reportQuery.data, hydrated])

  useEffect(() => {
    if (!isNew) {
      return
    }
    const wantChart = searchParams.get('kind') === 'chart'
    setLayout((current) => {
      if (isChartLayout(current) === wantChart) {
        return current
      }
      if (!wantChart && isChartLayout(current) && current.columns.length > 0) {
        return current
      }
      return wantChart ? emptyChartLayout() : emptyLayout()
    })
  }, [isNew, searchParams])

  useEffect(() => {
    if (isNew && !datasetId && designerDatasets.length) {
      setDatasetId(designerDatasets[0].id)
    }
  }, [isNew, datasetId, designerDatasets])

  useEffect(() => {
    setSkip(0)
  }, [datasetId, layout])

  useEffect(() => {
    if (
      !datasetId ||
      layout.columns.length === 0 ||
      (isChart && !layout.groupBy[0])
    ) {
      setPreview(null)
      setPreviewError(null)
      setPreviewLoading(false)
      return
    }

    const missing = missingRequiredFilters(layout.filters)
    if (missing.length > 0) {
      const names = missing
        .map((filter) =>
          filterDisplayLabel(
            lang,
            filter,
            palette.find(
              (column) => column.columnName.toLowerCase() === filter.field.toLowerCase(),
            ),
          ),
        )
        .join(', ')
      setPreview(null)
      setPreviewError(t('requiredFiltersBanner', { names }))
      setPreviewLoading(false)
      return
    }

    const handle = window.setTimeout(async () => {
      setPreviewLoading(true)
      try {
        const result = await api.preview({
          datasetId,
          layout,
          skip: isChart ? 0 : skip,
          take: previewTake,
          lang,
        })
        setPreview(result)
        setPreviewError(null)
      } catch (error) {
        setPreview(null)
        const message = (error as Error).message
        setPreviewError(message)
        if (/no longer|Unknown or hidden|Invalid column/i.test(message)) {
          void queryClient.invalidateQueries({ queryKey: ['datasets'] })
        }
      } finally {
        setPreviewLoading(false)
      }
    }, 400)

    return () => window.clearTimeout(handle)
  }, [datasetId, layout, skip, previewTake, isChart, lang, t])

  const nameFieldError = (value: string, requiredKey: 'nameRequired' | 'nameArabicRequired', tooShortKey: 'nameTooShort' | 'nameArabicTooShort') => {
    const trimmed = value.trim()
    if (!trimmed) {
      return t(requiredKey)
    }
    if (trimmed.length < 5) {
      return t(tooShortKey)
    }
    return undefined
  }

  const validateReport = () => {
    const userScopeFieldError =
      layout.userScope?.enabled && !layout.userScope.field?.trim()
        ? t('userScopeFieldRequired')
        : undefined
    const selectedScopeColumn = palette.find(
      (column) =>
        column.columnName.toLowerCase() === layout.userScope?.field?.toLowerCase(),
    )
    const userScopePropertyError =
      layout.userScope?.enabled &&
      selectedScopeColumn &&
      propertiesForSqlType(selectedScopeColumn.sqlType).length === 0
        ? t('userScopeUnsupportedColumn')
        : undefined
    const next = {
      name: nameFieldError(name, 'nameRequired', 'nameTooShort'),
      nameArabic: nameFieldError(nameArabic, 'nameArabicRequired', 'nameArabicTooShort'),
      columns: isChart || layout.columns.length > 0 ? undefined : t('reportColumnsRequired'),
      reportCategory:
        (categoriesQuery.data?.length ?? 0) > 0 && !categoryId
          ? t('reportCategoryRequired')
          : undefined,
      category:
        !isChart || layout.groupBy[0] ? undefined : t('chartCategoryRequired'),
      measures:
        !isChart || layout.columns.some((column) => column.aggregate)
          ? undefined
          : t('chartMeasureRequired'),
      userScopeField: userScopeFieldError,
      userScopeProperty: userScopePropertyError,
    }
    setFieldErrors(next)
    const first =
      next.name ??
      next.nameArabic ??
      next.reportCategory ??
      next.category ??
      next.measures ??
      next.columns ??
      next.userScopeField ??
      next.userScopeProperty
    if (first) {
      setSaveError(first)
      return false
    }
    setSaveError(null)
    return true
  }

  const save = useMutation({
    mutationFn: async (nextStatus: ReportStatus) => {
      if (!datasetId) {
        throw new Error(t('chooseDataset'))
      }
      const nameError = nameFieldError(name, 'nameRequired', 'nameTooShort')
      if (nameError) {
        throw new Error(nameError)
      }
      const nameArabicError = nameFieldError(nameArabic, 'nameArabicRequired', 'nameArabicTooShort')
      if (nameArabicError) {
        throw new Error(nameArabicError)
      }
      if (!isChart && layout.columns.length === 0) {
        throw new Error(t('reportColumnsRequired'))
      }
      if (isChart && !layout.groupBy[0]) {
        throw new Error(t('chartCategoryRequired'))
      }
      if (isChart && !layout.columns.some((column) => column.aggregate)) {
        throw new Error(t('chartMeasureRequired'))
      }
      if ((categoriesQuery.data?.length ?? 0) > 0 && !categoryId) {
        throw new Error(t('reportCategoryRequired'))
      }
      const payload = {
        name: name.trim(),
        nameArabic: nameArabic.trim(),
        description,
        descriptionArabic,
        datasetId,
        layout,
        status: nextStatus,
        categoryId: categoryId || null,
        isRestricted: accessMode !== 'Everyone',
        accessMode,
        accessUserIds,
        accessRoleIds,
      }
      return isNew
        ? api.createReport(payload)
        : api.updateReport(id!, payload)
    },
    onSuccess: (report, nextStatus) => {
      setSaveError(null)
      setStatus(report.status)
      queryClient.setQueryData(['report', report.id], report)
      void queryClient.invalidateQueries({ queryKey: ['reports'] })
      setJustSaved(
        nextStatus === 'published'
          ? t('published')
          : report.hasDraft && report.draftVersion
            ? t('draftVersionSaved', { version: report.draftVersion })
            : t('draftSaved'),
      )
      window.setTimeout(() => setJustSaved(null), 2200)
      if (isNew) {
        navigate(`/reports/${report.id}`, { replace: true })
      }
    },
    onError: (error) => setSaveError((error as Error).message),
  })

  const duplicate = useMutation({
    mutationFn: () => api.duplicateReport(id!),
    onSuccess: (report) => {
      void queryClient.invalidateQueries({ queryKey: ['reports'] })
      navigate(`/reports/${report.id}`)
    },
    onError: (error) => setSaveError((error as Error).message),
  })

  const download = useMutation({
    mutationFn: ({ format, file }: { format: ExportFormat; file?: FileSystemFileHandle }) => {
      if (!datasetId || layout.columns.length === 0) {
        throw new Error(t('chooseDatasetAndColumn'))
      }
      return api.exportPreview(
        { name, nameArabic, datasetId, layout },
        format,
        lang,
        file,
      )
    },
    onMutate: () => setDownloadError(null),
    onError: (error) => setDownloadError((error as Error).message),
  })

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 8 } }),
  )

  const updateLayout = (next: ReportLayout) => {
    const grouped = applyGrouping(next, palette)
    setLayout(grouped)
    if (grouped.columns.length > 0) {
      setFieldErrors((current) =>
        current.columns ? { ...current, columns: undefined } : current,
      )
    }
  }

  const placedColumn = (field: string) => {
    const meta = palette.find((column) => column.columnName === field)
    const locale = meta?.locale === 'en' || meta?.locale === 'ar' ? meta.locale : undefined
    return locale ? { field, locale } : { field }
  }

  const placeField = (field: string, zone: string) => {
    if (isLookupField(field) && (zone === 'groupBy' || zone === 'sort')) {
      return
    }
    if (
      field.toLowerCase().startsWith('calc:') &&
      (zone === 'groupBy' || zone === 'sort' || zone === 'filters')
    ) {
      return
    }
    if (zone === 'columns') {
      if (layout.columns.some((column) => column.field === field)) {
        setSelectedField(field)
        return
      }
      updateLayout({
        ...layout,
        columns: [...layout.columns, placedColumn(field)],
      })
      setSelectedField(field)
      return
    }

    if (zone === 'groupBy') {
      if (layout.groupBy.includes(field)) {
        return
      }
      const columns = layout.columns.some((column) => column.field === field)
        ? layout.columns
        : [...layout.columns, placedColumn(field)]
      updateLayout({
        ...layout,
        columns,
        groupBy: [...layout.groupBy, field],
      })
      return
    }

    if (zone === 'filters') {
      updateLayout({
        ...layout,
        filters: [...layout.filters, { field, operator: 'eq', value: '' }],
      })
      return
    }

    if (zone === 'sort') {
      if (layout.orderBy.some((item) => item.field === field)) {
        return
      }
      const column = columnForField(palette, field)
      updateLayout({
        ...layout,
        orderBy: [
          ...layout.orderBy,
          { field, direction: column ? defaultSortDirection(column) : 'desc' },
        ],
      })
    }
  }

  const onDragStart = (event: DragStartEvent) => {
    const data = event.active.data.current as
      | { kind?: string; field?: string; label?: string }
      | undefined
    if (!data?.field) {
      return
    }
    setActiveDrag({
      kind: data.kind ?? 'palette',
      field: data.field,
      label: data.label ?? data.field,
    })
  }

  const onDragEnd = (event: DragEndEvent) => {
    setActiveDrag(null)
    const over = event.over
    if (!over) {
      return
    }
    const from = event.active.data.current as
      | { kind: string; field?: string }
      | undefined
    const to = over.data.current as { zone?: string } | undefined
    const zone = to?.zone ?? (DROP_ZONES.has(String(over.id)) ? String(over.id) : undefined)
    const field = from?.field
    if (!from || !zone || !field) {
      return
    }

    if (from.kind === 'palette') {
      placeField(field, zone)
      return
    }

    if (from.kind === 'column' && zone === 'columns') {
      const activeField = String(event.active.id).replace('column:', '')
      const overField = String(over.id).replace('column:', '')
      const oldIndex = layout.columns.findIndex(
        (column) => column.field === activeField,
      )
      const newIndex = layout.columns.findIndex(
        (column) => column.field === overField,
      )
      if (oldIndex >= 0 && newIndex >= 0 && oldIndex !== newIndex) {
        updateLayout({
          ...layout,
          columns: arrayMove(layout.columns, oldIndex, newIndex),
        })
      }
      return
    }

    if (from.kind === 'filter' && zone === 'filters') {
      const overId = String(over.id)
      if (!overId.startsWith('filter:')) {
        return
      }
      const oldIndex = Number.parseInt(String(event.active.id).replace('filter:', ''), 10)
      const newIndex = Number.parseInt(overId.replace('filter:', ''), 10)
      if (
        Number.isFinite(oldIndex) &&
        Number.isFinite(newIndex) &&
        oldIndex >= 0 &&
        newIndex >= 0 &&
        oldIndex !== newIndex
      ) {
        updateLayout({
          ...layout,
          filters: arrayMove(layout.filters, oldIndex, newIndex),
        })
      }
      return
    }

    if (from.kind === 'column') {
      placeField(field, zone)
    }
  }

  const selectedColumn = layout.columns.find(
    (column) => column.field === selectedField,
  )
  const selectedMeta = palette.find((column) => column.columnName === selectedField)
  const selectedCalcMeta: DatasetColumn | undefined =
    selectedColumn?.expression != null && !selectedMeta
      ? {
          id: selectedColumn.field,
          columnName: selectedColumn.field,
          sqlType: 'nvarchar',
          displayName: selectedColumn.alias?.trim() || t('calcColumn'),
          isHidden: false,
          isFilterable: false,
          isGroupable: false,
          isIndexed: false,
          defaultSortDirection: 'asc',
          ordinal: 0,
        }
      : undefined
  const labeledPreview = useMemo(
    () =>
      preview && dataset
        ? localizeResultColumns(lang, preview, palette, layout)
        : preview,
    [preview, dataset, lang, layout],
  )
  const localizedPreview = useMemo(
    () => (labeledPreview ? resultForLang(labeledPreview, layout, lang) : labeledPreview),
    [labeledPreview, layout, lang],
  )
  const viewLayout = useMemo(() => layoutForLang(layout, lang), [layout, lang])
  const columnFormats = useMemo(
    () => buildFormatMap(layout, labeledPreview),
    [layout, labeledPreview],
  )
  const columnUrls = useMemo(
    () => buildUrlMap(layout, labeledPreview),
    [layout, labeledPreview],
  )
  const columnConditionalFormats = useMemo(
    () => buildConditionalFormatMap(layout, labeledPreview),
    [layout, labeledPreview],
  )
  const urlTokens = useMemo(
    () => urlTokensForResult(layout, labeledPreview),
    [layout, labeledPreview],
  )
  const presentation = useMemo(
    () => columnPresentation(layout, labeledPreview, lang),
    [layout, labeledPreview, lang],
  )
  const reportLinkOptions = useMemo(() => {
    const reports = reportsQuery.data ?? []
    return [...reports].sort((a, b) =>
      localizedText(lang, a.name, a.nameArabic).localeCompare(
        localizedText(lang, b.name, b.nameArabic),
        lang,
      ),
    )
  }, [reportsQuery.data, lang])
  const linkedReportFields = useMemo(() => {
    const summary = reportsQuery.data?.find((report) => report.id === linkedReportId)
    const dataset =
      datasetsQuery.data?.find((item) => item.id === linkedReportQuery.data?.datasetId) ??
      datasetsQuery.data?.find((item) => item.id === summary?.datasetId)
    return [...allowedDrillthroughFields(linkedReportQuery.data?.layout, dataset?.columns)]
  }, [
    linkedReportId,
    linkedReportQuery.data,
    reportsQuery.data,
    datasetsQuery.data,
  ])
  const linkedDatasetColumns = useMemo(() => {
    const summary = reportsQuery.data?.find((report) => report.id === linkedReportId)
    const dataset =
      datasetsQuery.data?.find((item) => item.id === linkedReportQuery.data?.datasetId) ??
      datasetsQuery.data?.find((item) => item.id === summary?.datasetId)
    return dataset?.columns ?? []
  }, [
    linkedReportId,
    linkedReportQuery.data,
    reportsQuery.data,
    datasetsQuery.data,
  ])
  const selectedFormatOptions = selectedMeta
    ? formatOptionsForSqlType(selectedMeta.sqlType)
    : []

  if (!isNew && reportQuery.data && reportQuery.data.canEdit === false) {
    return <Navigate to={`/reports/${id}/view`} replace />
  }

  const descriptionFields = (
    <>
      <Field label={t('description')}>
        <textarea
          rows={2}
          placeholder={t('descPlaceholder')}
          value={description}
          onChange={(event) => setDescription(event.target.value)}
        />
      </Field>
      <Field label={t('descriptionArabic')}>
        <textarea
          rows={2}
          dir="rtl"
          lang="ar"
          value={descriptionArabic}
          onChange={(event) => setDescriptionArabic(event.target.value)}
        />
      </Field>
    </>
  )

  const accessPanel = (
    <ReportAccessPanel
      isRestricted={accessMode !== 'Everyone'}
      accessMode={accessMode}
      accessUserIds={accessUserIds}
      people={peopleQuery.data ?? []}
      loading={peopleQuery.isLoading}
      currentUserId={user?.id}
      roles={rolesQuery.data ?? []}
      rolesLoading={rolesQuery.isLoading}
      accessRoleIds={accessRoleIds}
      onRestrictedChange={() => undefined}
      onAccessModeChange={setAccessMode}
      onAccessUserIdsChange={setAccessUserIds}
      onAccessRoleIdsChange={setAccessRoleIds}
    />
  )

  const userScopePanel = (
    <UserScopePanel
      layout={layout}
      palette={palette}
      fieldError={fieldErrors.userScopeField}
      propertyError={fieldErrors.userScopeProperty}
      disabled={!dataset}
      onChange={(userScope) => {
        updateLayout({ ...layout, userScope })
        if (userScope?.field) {
          setFieldErrors((current) => ({
            ...current,
            userScopeField: undefined,
            userScopeProperty: undefined,
          }))
        }
      }}
    />
  )

  const detailMasterColumns = layout.columns
    .filter((column) => !column.expression?.trim())
    .map((column) =>
      palette.find((item) => item.columnName.toLowerCase() === column.field.toLowerCase()),
    )
    .filter((column): column is NonNullable<typeof column> => Boolean(column))

  const detailPanel = (
    <DetailPanel
      masterColumns={detailMasterColumns}
      datasets={designerDatasets}
      detail={layout.detail}
      disabled={!dataset}
      onChange={(detail) => updateLayout({ ...layout, detail })}
    />
  )

  return (
    <section className={`page designer${isChart ? ' is-chart' : ''}`}>
      <header className="page-header">
        <div className="title-edit">
          <div className="kicker">
            {isChart ? t('chartDesigner') : t('designer')}
            <span className={`badge ${status === 'published' ? 'published' : 'draft'}`}>
              {status === 'published' ? t('published') : t('draft')}
            </span>
            {reportQuery.data?.hasDraft && reportQuery.data.draftVersion ? (
              <span className="badge draft">
                {t('draftVersionBadge', { version: reportQuery.data.draftVersion })}
              </span>
            ) : null}
            {reportQuery.data?.isAdminCreated && (
              <span className="badge admin">{t('adminReport')}</span>
            )}
          </div>
          <div className="designer-identity">
            <Field label={t('name')} error={fieldErrors.name}>
              <input
                required
                minLength={5}
                aria-invalid={Boolean(fieldErrors.name)}
                value={name}
                placeholder={t('untitledReport')}
                onChange={(event) => {
                  setName(event.target.value)
                  if (fieldErrors.name) {
                    setFieldErrors((current) => ({ ...current, name: undefined }))
                  }
                }}
              />
            </Field>
            <Field label={t('nameArabic')} error={fieldErrors.nameArabic}>
              <input
                required
                minLength={5}
                dir="rtl"
                lang="ar"
                aria-invalid={Boolean(fieldErrors.nameArabic)}
                value={nameArabic}
                onChange={(event) => {
                  setNameArabic(event.target.value)
                  if (fieldErrors.nameArabic) {
                    setFieldErrors((current) => ({
                      ...current,
                      nameArabic: undefined,
                    }))
                  }
                }}
              />
            </Field>
            <div className="designer-identity-category">
            <Field label={t('reportCategory')} error={fieldErrors.reportCategory}>
              <select
                value={categoryId}
                aria-invalid={Boolean(fieldErrors.reportCategory)}
                onChange={(event) => {
                  setCategoryId(event.target.value)
                  if (fieldErrors.reportCategory) {
                    setFieldErrors((current) => ({
                      ...current,
                      reportCategory: undefined,
                    }))
                  }
                }}
              >
                <option value="">{t('reportCategoryChoose')}</option>
                {(categoriesQuery.data ?? []).map((category) => (
                  <option key={category.id} value={category.id}>
                    {localizedText(lang, category.name, category.nameArabic)}
                  </option>
                ))}
              </select>
            </Field>
            </div>
            {isChart ? (
              <details className="designer-fold designer-identity-more">
                <summary>{t('description')}</summary>
                <div className="designer-identity-more-fields">{descriptionFields}</div>
              </details>
            ) : (
              descriptionFields
            )}
          </div>
        </div>
        <div className="header-actions">
          {!isNew && (reportQuery.data?.publishedVersion ?? 0) > 0 && (
            <button type="button" className="btn" onClick={() => setHistoryOpen(true)}>
              {t('publishHistory')}
            </button>
          )}
          {!isNew && can('reports.view') && (
            <Link className="btn" to={`/reports/${id}/view`}>
              {t('openView')}
            </Link>
          )}
          {!isNew && isAdmin && id && (
            <button type="button" className="btn" onClick={() => setEmbedOpen(true)}>
              {t('embedUrl')}
            </button>
          )}
          {!isNew && id && can('reports.create') && (
            <button
              type="button"
              className="btn"
              disabled={duplicate.isPending}
              onClick={() => duplicate.mutate()}
            >
              {duplicate.isPending ? t('duplicating') : t('duplicateReport')}
            </button>
          )}
          <button
            type="button"
            className="btn"
            onClick={() => {
              if (!validateReport()) {
                return
              }
              save.mutate('draft')
            }}
            disabled={
              save.isPending || !datasetId || (Boolean(datasetsQuery.data) && !dataset)
            }
          >
            {save.isPending && save.variables === 'draft'
              ? t('saving')
              : t('saveDraft')}
          </button>
          <button
            type="button"
            className="btn primary"
            onClick={() => {
              if (!validateReport()) {
                return
              }
              save.mutate('published')
            }}
            disabled={
              save.isPending || !datasetId || (Boolean(datasetsQuery.data) && !dataset)
            }
          >
            {save.isPending && save.variables === 'published'
              ? t('publishing')
              : status === 'published'
                ? t('saveAndPublish')
                : t('publish')}
          </button>
        </div>
      </header>

      {(saveError || downloadError || datasetsQuery.isError || reportQuery.isError) && (
        <div className="banner error">
          {saveError ||
            downloadError ||
            (datasetsQuery.error as Error | undefined)?.message ||
            (reportQuery.error as Error | undefined)?.message}
        </div>
      )}

      {!datasetsQuery.data?.length && !datasetsQuery.isLoading && (
        <div className="banner">
          {t('noDatasets')}
        </div>
      )}

      {datasetId &&
        datasetsQuery.data &&
        !dataset &&
        !datasetsQuery.isLoading && (
          <div className="banner error">{t('datasetAccessDenied')}</div>
        )}

      {can('ai.use') && (isChart ? (
        // The chart designer is a fixed-height, no-scroll page; the Copilot card stays folded so
        // it cannot squeeze the designer panel and the live preview into a thin strip.
        <details className="designer-fold ai-fold">
          <summary>
            <span>{t('assistant')}</span>
            <span className="muted small">{t('aiTitle')}</span>
          </summary>
          <AiAssistPanel
            datasetId={datasetId}
            name={name}
            nameArabic={nameArabic}
            description={description}
            descriptionArabic={descriptionArabic}
            layout={layout}
            onApply={applySuggestion}
          />
        </details>
      ) : (
        <AiAssistPanel
          datasetId={datasetId}
          name={name}
          nameArabic={nameArabic}
          description={description}
          descriptionArabic={descriptionArabic}
          layout={layout}
          onApply={applySuggestion}
        />
      ))}

      {isChart ? (
        <>
          <details className="designer-fold access-fold">
            <summary>
              <span>{t('whoCanAccess')}</span>
              <span className="muted small">
                {accessMode === 'Roles'
                  ? t('selectedCount', { n: accessRoleIds.length })
                  : accessMode === 'People'
                    ? t('selectedCount', { n: accessUserIds.length })
                    : t('everyone')}
              </span>
            </summary>
            {accessPanel}
          </details>
          <details className="designer-fold user-scope-fold">
            <summary>
              <span>{t('userScope')}</span>
              <span className="muted small">
                {isUserScopeConfigured(layout.userScope)
                  ? t('userScopeActiveShort')
                  : t('userScopeOff')}
              </span>
            </summary>
            {userScopePanel}
          </details>
        </>
      ) : (
        <>
          {accessPanel}
          {userScopePanel}
          {detailPanel}
        </>
      )}

      <div
        ref={stageRef}
        className={`designer-stage${isChart ? ' is-chart' : ''}`}
      >
        {isChart ? (
          <div className="designer-workspace">
            <ChartDesignerCanvas
              datasetId={datasetId}
              datasets={designerDatasets}
              palette={palette}
              layout={layout}
              categoryError={fieldErrors.category}
              measureError={fieldErrors.measures}
              onDatasetChange={(nextId) => {
                setDatasetId(nextId)
                setLayout(emptyChartLayout(layout.chart?.type))
              }}
              onChange={(next) => {
                setLayout(next)
                if (next.groupBy[0]) {
                  setFieldErrors((current) =>
                    current.category ? { ...current, category: undefined } : current,
                  )
                }
                if (next.columns.some((column) => column.aggregate)) {
                  setFieldErrors((current) =>
                    current.measures ? { ...current, measures: undefined } : current,
                  )
                }
              }}
            />
            <DesignerResizer
              className="between-dataset-report"
              label={t('resizeDatasetSection')}
              onPointerDown={resizeDataset}
            />
          </div>
        ) : (
        <div className="designer-workspace">
        <DndContext
          sensors={sensors}
          collisionDetection={detectDropTarget}
          onDragStart={onDragStart}
          onDragEnd={onDragEnd}
          onDragCancel={() => setActiveDrag(null)}
        >
          <div className="designer-grid">
            <aside className="panel palette-panel">
              <DatasetPicker
                value={datasetId}
                datasets={designerDatasets}
                onChange={(nextId) => {
                  setDatasetId(nextId)
                  setLayout(isChart ? emptyChartLayout(layout.chart?.type) : emptyLayout())
                  setSelectedField(null)
                }}
              />
              <h3>{t('columns')}</h3>
              <p className="muted small">{t('paletteHint')}</p>
              <div className="chip-list">
                {palette.map((column) => (
                  <PaletteChip
                    key={column.id}
                    column={column}
                    lang={lang}
                    onAdd={() => {
                      if (layout.columns.some((item) => item.field === column.columnName)) {
                        setSelectedField(column.columnName)
                        return
                      }
                      updateLayout({
                        ...layout,
                        columns: [...layout.columns, placedColumn(column.columnName)],
                      })
                      setSelectedField(column.columnName)
                    }}
                  />
                ))}
              </div>
              <div className="palette-calc-head">
                <h3>{t('calcColumns')}</h3>
                <button
                  type="button"
                  className="btn sm icon"
                  title={t('calcAdd')}
                  aria-label={t('calcAdd')}
                  onClick={() => {
                    setCalcEditing(null)
                    setCalcLabel('')
                    setCalcExpression('')
                    setCalcOpen(true)
                  }}
                >
                  +
                </button>
              </div>
              <div className="chip-list">
                {layout.columns
                  .filter((column) => column.expression?.trim())
                  .map((column) => (
                    <PaletteChip
                      key={column.field}
                      lang={lang}
                      column={{
                        id: column.field,
                        columnName: column.field,
                        sqlType: column.expression?.trim() || 'calc',
                        displayName: column.alias?.trim() || t('calcColumn'),
                        isHidden: false,
                        isFilterable: false,
                        isGroupable: false,
                        isIndexed: false,
                        defaultSortDirection: 'asc',
                        ordinal: 0,
                      }}
                      onAdd={() => {
                        setCalcEditing(column.field)
                        setCalcLabel(column.alias?.trim() ?? '')
                        setCalcExpression(column.expression ?? '')
                        setCalcOpen(true)
                      }}
                    />
                  ))}
              </div>
            </aside>

            <DesignerResizer
              className="between-dataset-report"
              label={t('resizeDatasetSection')}
              onPointerDown={resizeDataset}
            />

            <div className="designer-report">
            <div className="canvas">
              <DropZone
                id="columns"
                zone="columns"
                title={t('reportColumns')}
                hint={t('reportColumnsHint')}
                icon={<IconColumns />}
                empty={t('dropFields')}
                isEmpty={layout.columns.length === 0}
                error={fieldErrors.columns}
              >
                <SortableContext
                  items={layout.columns.map((column) => `column:${column.field}`)}
                  strategy={horizontalListSortingStrategy}
                >
                  <div className="chip-row">
                    {layout.columns.map((column) => (
                      <SortableColumnChip
                        key={column.field}
                        column={column}
                        lookup={isLookupField(column.field)}
                        calculated={Boolean(column.expression?.trim())}
                        label={fieldLabel(lang, palette, column.field, column.alias)}
                        localeLabel={
                          column.locale === 'en'
                            ? t('columnLocaleEnglishShort')
                            : column.locale === 'ar'
                              ? t('columnLocaleArabicShort')
                              : undefined
                        }
                        bandLabel={localizedText(lang, column.headerBand, column.headerBandArabic).trim()}
                        selected={selectedField === column.field}
                        onSelect={() => setSelectedField(column.field)}
                        onRemove={() => {
                          updateLayout({
                            ...layout,
                            columns: layout.columns.filter(
                              (item) => item.field !== column.field,
                            ),
                          })
                          if (selectedField === column.field) {
                            setSelectedField(null)
                          }
                        }}
                      />
                    ))}
                  </div>
                </SortableContext>
              </DropZone>



              <DropZone
                id="filters"
                zone="filters"
                title={t('filters')}
                hint={t('filtersHint')}
                icon={<IconFilter />}
                empty={t('dropFilter')}
                isEmpty={layout.filters.length === 0}
                actions={
                  <FilterLayoutSettings
                    value={layout.filtersPerRow}
                    onChange={(filtersPerRow) => updateLayout({ ...layout, filtersPerRow })}
                  />
                }
              >
                <SortableContext
                  items={layout.filters.map((_, index) => `filter:${index}`)}
                  strategy={verticalListSortingStrategy}
                >
                  <div className="filter-list">
                    {layout.filters.map((filter, index) => (
                      <SortableFilterRow
                        key={`filter:${index}`}
                        index={index}
                        filter={filter}
                        previous={layout.filters[index - 1]}
                        allFilters={layout.filters}
                        lang={lang}
                        palette={palette}
                        datasetId={datasetId}
                        onChange={(nextFilter) => {
                          const previous = layout.filters[index]
                          const next = layout.filters.slice()
                          next[index] = nextFilter
                          updateLayout({
                            ...layout,
                            filters: clearDescendantFilterValues(next, index, previous),
                          })
                        }}
                        onRemove={() => {
                          const removed = layout.filters[index]?.field.toLowerCase()
                          updateLayout({
                            ...layout,
                            filters: layout.filters
                              .filter((_, i) => i !== index)
                              .map((item) =>
                                item.dependsOn?.toLowerCase() === removed
                                  ? { ...item, dependsOn: null }
                                  : item,
                              ),
                          })
                        }}
                      />
                    ))}
                  </div>
                </SortableContext>
              </DropZone>

              <DropZone
                id="sort"
                zone="sort"
                title={t('sort')}
                hint={t('sortHint')}
                icon={<IconSort />}
                empty={t('dropSort')}
                isEmpty={layout.orderBy.length === 0}
              >
                <div className="sort-list">
                  {layout.orderBy.map((item, index) => (
                    <div key={item.field} className={`sort-row${isLookupField(item.field) ? ' lookup' : ''}`}>
                      <span className="sort-row-index" aria-hidden="true">
                        {index + 1}
                      </span>
                      <strong title={item.field}>
                        {fieldLabel(lang, palette, item.field)}
                      </strong>
                      <select
                        value={item.direction}
                        aria-label={t('sort')}
                        onChange={(event) => {
                          const next = layout.orderBy.slice()
                          next[index] = {
                            ...item,
                            direction: event.target.value as 'asc' | 'desc',
                          }
                          updateLayout({ ...layout, orderBy: next })
                        }}
                      >
                        <option value="asc">{t('ascending')}</option>
                        <option value="desc">{t('descending')}</option>
                      </select>
                      <button
                        type="button"
                        className="link danger"
                        onClick={() =>
                          updateLayout({
                            ...layout,
                            orderBy: layout.orderBy.filter((_, i) => i !== index),
                          })
                        }
                      >
                        {t('remove')}
                      </button>
                    </div>
                  ))}
                </div>
              </DropZone>


              <DropZone
                id="groupBy"
                zone="groupBy"
                title={t('groupBy')}
                hint={t('groupByHint')}
                icon={<IconGroup />}
                empty={t('dropGroup')}
                isEmpty={layout.groupBy.length === 0}
              >
                <div className="chip-row">
                  {layout.groupBy.map((field) => (
                    <span key={field} className="chip">
                      {fieldLabel(lang, palette, field)}
                      <button
                        type="button"
                        onClick={() =>
                          updateLayout({
                            ...layout,
                            groupBy: layout.groupBy.filter((item) => item !== field),
                          })
                        }
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
              </DropZone>
            </div>

            <aside className="panel props-panel">
              {selectedColumn && (selectedMeta ?? selectedCalcMeta) ? (
                <ColumnPropsPanel
                  column={selectedColumn}
                  meta={(selectedMeta ?? selectedCalcMeta)!}
                  grouped={layout.groupBy.some((field) => field === selectedColumn.field)}
                  formatOptions={selectedFormatOptions}
                  palette={palette}
                  reports={reportLinkOptions}
                  datasets={datasetsQuery.data ?? []}
                  linkedReportFields={linkedReportFields}
                  linkedDatasetColumns={linkedDatasetColumns}
                  onChange={(next) =>
                    updateLayout({
                      ...layout,
                      columns: layout.columns.map((column) =>
                        column.field === selectedColumn.field ? next : column,
                      ),
                    })
                  }
                />
              ) : (
                <div className="props">
                  <div className="props-identity">
                    <h3>{t('columnPropsTitle')}</h3>
                  </div>
                  <p className="muted">{t('selectColumnHint')}</p>
                </div>
              )}
            </aside>
            </div>
          </div>
          <DragOverlay dropAnimation={null}>
            {activeDrag ? (
              <div className={`chip drag-overlay${isLookupField(activeDrag.field) ? ' lookup' : ''}${activeDrag.field.startsWith('calc:') ? ' calc' : ''}`}>
                {activeDrag.label}
              </div>
            ) : null}
          </DragOverlay>
        </DndContext>
        </div>
        )}

        <DesignerResizer
          className="between-report-preview"
          label={t('resizePreviewSection')}
          onPointerDown={resizePreview}
        />

        <section className="preview card">
          <div className="preview-header">
            <div>
              <h2>{t('livePreview')}</h2>
              {isUserScopeConfigured(layout.userScope) && (
                <p className="muted small">{t('userScopePreviewNote')}</p>
              )}
            </div>
            <div className="header-actions">
              <span className="live-pill">
                <i /> {t('unsavedLive')}
              </span>
              {can('reports.export') && (
                <ExportMenu
                  label={t('downloadAll')}
                  fileName={bilingualExportName(name, nameArabic)}
                  disabled={!datasetId || layout.columns.length === 0}
                  pending={download.isPending}
                  onExport={(format, file) => download.mutate({ format, file })}
                />
              )}
            </div>
          </div>
          {isChart ? (
            <ChartView
              result={localizedPreview}
              layout={viewLayout}
              loading={previewLoading}
              error={previewError}
              emptyHint={t('chartEmptyHint')}
            />
          ) : (
            <ResultGrid
              result={localizedPreview}
              columnFormats={columnFormats}
              columnUrls={columnUrls}
              columnConditionalFormats={columnConditionalFormats}
              urlTokens={urlTokens}
              loading={previewLoading}
              error={previewError}
              autoFit
              stickyFields={viewLayout.columns.filter((column) => column.sticky).map((column) => column.field)}
              columnAlign={presentation.columnAlign}
              columnBands={presentation.columnBands}
              columnWrap={presentation.columnWrap}
              onPageChange={setSkip}
              detailParentFields={
                isDetailConfigured(layout.detail)
                  ? [
                      fieldToResultName(viewLayout, localizedPreview, layout.detail.parentField ?? ''),
                      layout.detail.parentField ?? '',
                    ]
                  : undefined
              }
              renderDetail={
                isDetailConfigured(layout.detail)
                  ? (parentValue) => (
                      <DetailGrid
                        reportId={id ?? null}
                        layout={layout}
                        parentValue={parentValue}
                        lang={lang}
                        sendLayout={Boolean(id)}
                      />
                    )
                  : undefined
              }
            />
          )}
        </section>
      </div>
      <CalcColumnDialog
        open={calcOpen}
        editing={Boolean(calcEditing)}
        initialLabel={calcLabel}
        initialExpression={calcExpression}
        columns={palette.filter(
          (column) => !isLookupField(column.columnName) && !column.columnName.startsWith('calc:'),
        )}
        datasetId={datasetId}
        allowAggregates={layout.groupBy.length > 0}
        onClose={() => setCalcOpen(false)}
        onSave={(label, expression) => {
          if (calcEditing) {
            updateLayout({
              ...layout,
              columns: layout.columns.map((column) =>
                column.field === calcEditing
                  ? { ...column, alias: label, expression }
                  : column,
              ),
            })
          } else {
            const field = `calc:${crypto.randomUUID()}`
            updateLayout({
              ...layout,
              columns: [...layout.columns, { field, alias: label, expression }],
            })
            setSelectedField(field)
          }
          setCalcOpen(false)
        }}
      />
      {embedOpen && id && (
        <EmbedUrlDialog reportId={id} filters={layout.filters} onClose={() => setEmbedOpen(false)} />
      )}
      <PublishHistoryDialog
        reportId={historyOpen && id ? id : null}
        onClose={() => setHistoryOpen(false)}
        onRestored={(report, version) => {
          queryClient.setQueryData(['report', report.id], report)
          void queryClient.invalidateQueries({ queryKey: ['reports'] })
          setHydrated(false)
          setHistoryOpen(false)
          setJustSaved(
            report.status === 'published'
              ? report.hasDraft
                ? t('publishHistoryRestored', { version })
                : t('publishHistoryBackToPublished')
              : t('publishHistoryRestoredDraft', { version }),
          )
          window.setTimeout(() => setJustSaved(null), 3200)
        }}
      />
      {justSaved && <div className="toast">{justSaved}</div>}
    </section>
  )
}

function PaletteChip({
  column,
  lang,
  onAdd,
}: {
  column: DatasetColumn
  lang: Lang
  onAdd: () => void
}) {
  const { t } = useLocale()
  const label = columnLabel(lang, column)
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: `palette:${column.columnName}`,
    data: {
      kind: 'palette',
      field: column.columnName,
      label,
    },
  })

  return (
    <button
      type="button"
      ref={setNodeRef}
      className={`chip palette ${isLookupField(column.columnName) ? 'lookup ' : ''}${column.columnName.startsWith('calc:') ? 'calc ' : ''}${isDragging ? 'dragging' : ''}`}
      onClick={onAdd}
      {...listeners}
      {...attributes}
    >
      <span>
        <span className={`tone ${typeTone(column.sqlType)}`} />
        {label}
      </span>
      <small>
        {column.columnName.toLowerCase().endsWith(':control')
          ? t('lookupColumn')
          : column.sqlType}
        {column.locale === 'en' ? ' · EN' : column.locale === 'ar' ? ' · AR' : ''}
      </small>
    </button>
  )
}

function SortableFilterRow({
  index,
  filter,
  previous,
  allFilters,
  lang,
  palette,
  datasetId,
  onChange,
  onRemove,
}: {
  index: number
  filter: LayoutFilter
  previous?: LayoutFilter
  allFilters: LayoutFilter[]
  lang: Lang
  palette: DatasetColumn[]
  datasetId: string
  onChange: (filter: LayoutFilter) => void
  onRemove: () => void
}) {
  const { t } = useLocale()
  const label = fieldLabel(lang, palette, filter.field)
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({
      id: `filter:${index}`,
      data: {
        kind: 'filter',
        field: filter.field,
        label,
        zone: 'filters',
        index,
      },
    })
  const style = isDragging
    ? undefined
    : {
        transform: CSS.Transform.toString(transform),
        transition,
      }
  const orJoin =
    previous &&
    previous.field.toLowerCase() === filter.field.toLowerCase() &&
    (previous.operator === 'eq' || previous.operator === 'in') &&
    (filter.operator === 'eq' || filter.operator === 'in')

  return (
    <div ref={setNodeRef} style={style}>
      {orJoin && <div className="filter-join">{t('or')}</div>}
      <div className={`filter-row ${isLookupField(filter.field) ? 'lookup' : ''} ${filter.isFixed ? 'is-fixed' : ''} ${filter.isHidden ? 'is-hidden' : ''} ${filter.isRequired ? 'is-required' : ''} ${isDragging ? 'dragging' : ''}`}>
        <button
          type="button"
          className="filter-handle"
          title={t('reorderFilter')}
          aria-label={t('reorderFilter')}
          {...listeners}
          {...attributes}
        >
          ⋮⋮
        </button>
        <div className="filter-name">
          <strong title={filter.field}>{label}</strong>
          <FilterLabelButton
            fieldLabel={label}
            alias={filter.alias}
            aliasArabic={filter.aliasArabic}
            onChange={(next) => onChange({ ...filter, ...next })}
          />
        </div>
        <select
          value={filter.operator}
          onChange={(event) =>
            onChange({
              ...filter,
              operator: event.target.value,
            })
          }
        >
          {FILTER_OPERATORS.map((op) => (
            <option key={op.value} value={op.value}>
              {t(operatorMessageKey(op.value))}
            </option>
          ))}
        </select>
        {needsFilterValue(filter.operator) ? (
          <FilterValueField
            datasetId={datasetId}
            column={palette.find((item) => item.columnName === filter.field)}
            operator={filter.operator}
            value={filter.value ?? ''}
            contextFilters={allFilters}
            onChange={(nextValue) =>
              onChange({
                ...filter,
                value: nextValue,
              })
            }
          />
        ) : (
          <span className="muted small">{t('noValueNeeded')}</span>
        )}
        <FilterOptionsButton
          filter={filter}
          allFilters={allFilters}
          lang={lang}
          palette={palette}
          onChange={onChange}
        />
        <button
          type="button"
          className="filter-remove"
          title={t('remove')}
          aria-label={t('removeNamed', { name: label })}
          onClick={onRemove}
        >
          <IconTrash />
        </button>
      </div>
    </div>
  )
}

function SortableColumnChip({
  column,
  label,
  localeLabel,
  bandLabel,
  selected,
  lookup,
  calculated,
  onSelect,
  onRemove,
}: {
  column: LayoutColumn
  label: string
  localeLabel?: string
  bandLabel?: string
  selected: boolean
  lookup?: boolean
  calculated?: boolean
  onSelect: () => void
  onRemove: () => void
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } =
    useSortable({
      id: `column:${column.field}`,
      data: {
        kind: 'column',
        field: column.field,
        label,
        zone: 'columns',
      },
    })
  const style = isDragging
    ? undefined
    : {
      transform: CSS.Transform.toString(transform),
      transition,
    }

  return (
    <span
      ref={setNodeRef}
      style={style}
      className={`chip ${lookup ? 'lookup' : ''} ${calculated ? 'calc' : ''} ${selected ? 'selected' : ''} ${isDragging ? 'dragging' : ''}`}
      onClick={onSelect}
    >
      <button type="button" className="chip-handle" {...listeners} {...attributes}>
        {label}
        {column.aggregate ? ` (${column.aggregate})` : ''}
        {localeLabel ? ` · ${localeLabel}` : ''}
        {bandLabel ? <span className="chip-band">{bandLabel}</span> : null}
      </button>
      <button type="button" onClick={onRemove}>
        ×
      </button>
    </span>
  )
}

function DropZone({
  id,
  zone,
  title,
  hint,
  icon,
  empty,
  isEmpty,
  error,
  actions,
  children,
}: {
  id: string
  zone: string
  title: string
  hint: string
  icon: ReactNode
  empty: string
  isEmpty: boolean
  error?: string
  actions?: ReactNode
  children: ReactNode
}) {
  const { setNodeRef, isOver } = useDroppable({
    id,
    data: { zone },
  })

  return (
    <div
      ref={setNodeRef}
      className={`drop-zone${isOver ? ' over' : ''}${error ? ' has-error' : ''}`}
    >
      <div className="drop-zone-head">
        <div className="zone-title">
          {icon}
          <h3>{title}</h3>
          {actions}
        </div>
        <span className="muted small">{hint}</span>
      </div>
      {isEmpty && <div className="zone-empty">{empty}</div>}
      {error ? (
        <div className="drop-zone-error" role="alert">
          {error}
        </div>
      ) : null}
      {children}
    </div>
  )
}
