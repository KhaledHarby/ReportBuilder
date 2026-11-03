import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type PointerEvent, type ReactNode, Fragment } from 'react'
import type { ConditionalFormatRule, ReportResult } from '../api/types'
import { interpolateUrl } from '../lib/analyticsFilters'
import { cellLinkProps } from '../lib/reportLinks'
import { formatColumnValue } from '../lib/columnFormat'
import { resolveConditionalStyle } from '../lib/conditionalFormat'
import { IconColumns } from './Icons'
import { Spinner } from './Spinner'
import { useLocale } from '../i18n/LocaleProvider'

type SortDir = 'asc' | 'desc'

const MIN_COLUMN_WIDTH = 72
const MAX_COLUMN_WIDTH = 420
const WRAP_COLUMN_WIDTH = 240
const MANUAL_MAX_COLUMN_WIDTH = 1600
type Props = {
  result: ReportResult | null
  loading?: boolean
  error?: string | null
  emptyHint?: string
  emptyLabel?: string
  /** Result column names that hold the parent key, in the order to try. */
  detailParentFields?: string[]
  renderDetail?: (parentValue: string) => ReactNode
  allowSort?: boolean
  align?: 'start' | 'center'
  fillWidth?: boolean
  /** Measure header/cell content and size columns on load (and when data changes). */
  autoFit?: boolean
  columnFormats?: Record<string, string | null | undefined>
  columnUrls?: Record<string, string | undefined>
  columnConditionalFormats?: Record<string, ConditionalFormatRule[] | undefined>
  urlTokens?: {
    id: string
    field?: string
    label: string
    displayName?: string
    displayNameArabic?: string
  }[]
  /** Column names that stay visible while the grid scrolls sideways. */
  stickyFields?: string[]
  columnAlign?: Record<string, string | null | undefined>
  columnBands?: Record<string, string | null | undefined>
  columnWrap?: Record<string, true | null | undefined>
  /** Controlled sort, used with onSortChange for server-side ordering. */
  sort?: { column: string; dir: SortDir } | null
  onSortChange?: (sort: { column: string; dir: SortDir } | null) => void
  onPageChange: (skip: number) => void
}

function formatCell(
  value: unknown,
  locale: string,
  format?: string | null,
  column?: { name: string; label: string },
) {
  return formatColumnValue(value, format, locale, column)
}

function isEmpty(value: unknown) {
  return value == null || value === ''
}

function compareValues(left: unknown, right: unknown) {
  if (isEmpty(left) && isEmpty(right)) {
    return 0
  }
  if (isEmpty(left)) {
    return 1
  }
  if (isEmpty(right)) {
    return -1
  }

  if (typeof left === 'number' && typeof right === 'number') {
    return left - right
  }

  const leftText = String(left)
  const rightText = String(right)
  const leftNumber = Number(leftText)
  const rightNumber = Number(rightText)
  if (
    leftText.trim() !== '' &&
    rightText.trim() !== '' &&
    Number.isFinite(leftNumber) &&
    Number.isFinite(rightNumber)
  ) {
    return leftNumber - rightNumber
  }

  const leftDate = Date.parse(leftText)
  const rightDate = Date.parse(rightText)
  if (
    Number.isFinite(leftDate) &&
    Number.isFinite(rightDate) &&
    /\d/.test(leftText) &&
    /[-/T]/.test(leftText)
  ) {
    return leftDate - rightDate
  }

  return leftText.localeCompare(rightText, undefined, {
    numeric: true,
    sensitivity: 'base',
  })
}

function spreadColumnWidths(
  columns: ReportResult['columns'],
  containerWidth: number,
  source: Record<string, number> | null,
) {
  if (columns.length === 0 || containerWidth <= 0) {
    return {}
  }

  const next: Record<string, number> = {}

  if (!source || Object.keys(source).length === 0) {
    let used = 0
    columns.forEach((column, index) => {
      const width =
        index === columns.length - 1
          ? Math.max(MIN_COLUMN_WIDTH, containerWidth - used)
          : Math.max(MIN_COLUMN_WIDTH, Math.floor(containerWidth / columns.length))
      next[column.name] = width
      used += width
    })
    return next
  }

  const minTotal = columns.reduce((sum, column) => sum + source[column.name], 0)
  if (minTotal >= containerWidth) {
    return source
  }

  let used = 0
  const scale = containerWidth / minTotal
  columns.forEach((column, index) => {
    const width =
      index === columns.length - 1
        ? Math.max(MIN_COLUMN_WIDTH, containerWidth - used)
        : Math.max(MIN_COLUMN_WIDTH, Math.ceil(source[column.name] * scale))
    next[column.name] = width
    used += width
  })
  return next
}

function widthsForContainer(
  columns: ReportResult['columns'],
  containerWidth: number,
  content: Record<string, number>,
  userWidths: Record<string, number>,
) {
  const next: Record<string, number> = {}
  for (const column of columns) {
    next[column.name] =
      userWidths[column.name] ?? content[column.name] ?? MIN_COLUMN_WIDTH
  }
  if (containerWidth <= 0) {
    return next
  }

  const used = columns.reduce((sum, column) => sum + next[column.name], 0)
  if (used >= containerWidth) {
    return next
  }

  const flexible = columns.filter((column) => userWidths[column.name] == null)
  if (flexible.length === 0) {
    return next
  }

  const flexibleSum = flexible.reduce((sum, column) => sum + next[column.name], 0)
  const locked = used - flexibleSum
  const target = Math.max(0, containerWidth - locked)
  const scale = flexibleSum > 0 ? target / flexibleSum : 1
  let assigned = locked
  flexible.forEach((column, index) => {
    const width =
      index === flexible.length - 1
        ? Math.max(MIN_COLUMN_WIDTH, containerWidth - assigned)
        : Math.max(MIN_COLUMN_WIDTH, Math.round(next[column.name] * scale))
    next[column.name] = width
    assigned += width
  })
  return next
}

function sameWidths(
  columns: ReportResult['columns'],
  current: Record<string, number>,
  next: Record<string, number>,
) {
  return columns.every(
    (column) => Math.abs((current[column.name] ?? 0) - (next[column.name] ?? 0)) < 2,
  )
}

function detailKey(value: unknown) {
  if (value === null || value === undefined) {
    return null
  }
  if (typeof value === 'string') {
    const trimmed = value.trim()
    return trimmed.length > 0 ? trimmed : null
  }
  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value)
  }
  return null
}

function rawCell(row: Record<string, unknown>, name: string) {
  if (Object.prototype.hasOwnProperty.call(row, name)) {
    return row[name]
  }
  const match = Object.keys(row).find((key) => key.toLowerCase() === name.toLowerCase())
  return match ? row[match] : undefined
}

export function ResultGrid({
  result,
  loading,
  error,
  emptyHint,
  emptyLabel,
  detailParentFields,
  renderDetail,
  allowSort = false,
  align = 'start',
  fillWidth = false,
  autoFit = false,
  columnFormats,
  columnUrls,
  columnConditionalFormats,
  urlTokens,
  stickyFields,
  columnAlign,
  columnBands,
  columnWrap,
  sort: sortProp,
  onSortChange,
  onPageChange,
}: Props) {
  const { locale, t } = useLocale()
  const [clientSort, setClientSort] = useState<{ column: string; dir: SortDir } | null>(null)
  const serverSort = Boolean(onSortChange)
  const sortEnabled = allowSort || serverSort
  const sort = serverSort ? (sortProp ?? null) : clientSort
  const [columnWidths, setColumnWidths] = useState<Record<string, number>>({})
  const [userResized, setUserResized] = useState(false)
  const tableRef = useRef<HTMLTableElement>(null)
  const tableScrollRef = useRef<HTMLDivElement>(null)
  const fitWidthsRef = useRef<Record<string, number> | null>(null)
  const userWidthsRef = useRef<Record<string, number>>({})
  const resizeDragRef = useRef<{
    name: string
    startX: number
    startWidth: number
  } | null>(null)

  const displayColumns = useMemo(
    () => (result?.columns ?? []).filter((column) => !column.hidden),
    [result],
  )
  const columnKey = displayColumns.map((column) => column.name).join('\0')

  const resolveFormat = useCallback(
    (columnName: string) => columnFormats?.[columnName] ?? null,
    [columnFormats],
  )

  const resolveRules = useCallback(
    (columnName: string) =>
      columnConditionalFormats?.[columnName] ??
      Object.entries(columnConditionalFormats ?? {}).find(
        ([name]) => name.toLowerCase() === columnName.toLowerCase(),
      )?.[1],
    [columnConditionalFormats],
  )

  useEffect(() => {
    fitWidthsRef.current = null
    userWidthsRef.current = {}
    setUserResized(false)
    setColumnWidths({})
  }, [columnKey])

  const [openDetails, setOpenDetails] = useState<string[]>([])
  const detailFields = detailParentFields ?? []
  useEffect(() => {
    setOpenDetails([])
  }, [result?.skip, columnKey])

  function parentKey(row: Record<string, unknown>) {
    for (const field of detailFields) {
      const value = rawCell(row, field)
      const key = detailKey(value)
      if (key) {
        return key
      }
    }
    return null
  }

  const rows = useMemo(() => {
    const source = result?.rows ?? []
    if (serverSort || !allowSort || !sort) {
      return source
    }

    const next = source.slice()
    next.sort((a, b) => {
      const rank = compareValues(a[sort.column], b[sort.column])
      return sort.dir === 'asc' ? rank : -rank
    })
    return next
  }, [allowSort, result?.rows, serverSort, sort])

  const fillToContainer = useCallback(() => {
    if (!fillWidth || !tableScrollRef.current) {
      return
    }
    const containerWidth = tableScrollRef.current.clientWidth
    const content = fitWidthsRef.current
    if (autoFit && !content) {
      return
    }
    setColumnWidths((current) => {
      const next = content
        ? widthsForContainer(
            displayColumns,
            containerWidth,
            content,
            userWidthsRef.current,
          )
        : Object.keys(userWidthsRef.current).length > 0
          ? current
          : spreadColumnWidths(displayColumns, containerWidth, null)
      return sameWidths(displayColumns, current, next) ? current : next
    })
  }, [autoFit, displayColumns, fillWidth])

  useEffect(() => {
    if (!fillWidth || !result) {
      return
    }

    fillToContainer()
    const node = tableScrollRef.current
    if (!node) {
      return
    }

    const observer = new ResizeObserver(() => fillToContainer())
    observer.observe(node)
    return () => observer.disconnect()
  }, [fillWidth, result, columnKey, fillToContainer])

  const bestFitColumns = useCallback(() => {
    const table = tableRef.current
    if (!result || !table || displayColumns.length === 0) {
      return
    }

    const previousWidth = table.style.width
    const previousMinWidth = table.style.minWidth
    const cols = [...table.querySelectorAll('col')]
    const previousColWidths = cols.map((col) => col.style.width)
    const bands = [...table.querySelectorAll<HTMLElement>('thead th.band-header')]
    const previousBands = bands.map((cell) => ({
      width: cell.style.width,
      maxWidth: cell.style.maxWidth,
    }))
    table.classList.remove('sized')
    table.style.width = 'max-content'
    table.style.minWidth = '0'
    cols.forEach((col) => {
      col.style.width = 'auto'
    })
    bands.forEach((cell) => {
      cell.style.width = 'auto'
      cell.style.maxWidth = 'none'
    })

    try {
      const next: Record<string, number> = {}
      const body = table.tBodies[0]
      displayColumns.forEach((column, index) => {
        const header = table.querySelector(
          `thead th[data-col="${CSS.escape(column.name)}"]`,
        )
        let width = header?.getBoundingClientRect().width ?? MIN_COLUMN_WIDTH
        if (body) {
          for (const row of body.rows) {
            if (row.cells.length !== displayColumns.length) {
              continue
            }
            const cell = row.cells[index]
            if (cell) {
              width = Math.max(width, cell.getBoundingClientRect().width)
            }
          }
        }
        const wrapped = Boolean(
          columnWrap?.[column.name] ??
            Object.entries(columnWrap ?? {}).find(
              ([key]) => key.toLowerCase() === column.name.toLowerCase(),
            )?.[1],
        )
        next[column.name] = Math.min(
          wrapped ? WRAP_COLUMN_WIDTH : MAX_COLUMN_WIDTH,
          Math.max(MIN_COLUMN_WIDTH, Math.ceil(width)),
        )
      })

      fitWidthsRef.current = next
      const container = fillWidth ? (tableScrollRef.current?.clientWidth ?? 0) : 0
      setColumnWidths(
        widthsForContainer(displayColumns, container, next, userWidthsRef.current),
      )
    } finally {
      cols.forEach((col, index) => {
        col.style.width = previousColWidths[index] ?? ''
      })
      bands.forEach((cell, index) => {
        cell.style.width = previousBands[index]?.width ?? ''
        cell.style.maxWidth = previousBands[index]?.maxWidth ?? ''
      })
      table.style.width = previousWidth
      table.style.minWidth = previousMinWidth
    }
  }, [columnWrap, displayColumns, fillWidth, result])

  useEffect(() => {
    if (!autoFit || !result) {
      return
    }

    let cancel = false
    const run = () => {
      if (!cancel) {
        bestFitColumns()
      }
    }
    const frame = requestAnimationFrame(run)
    const fontsReady = document.fonts?.ready.then(() => {
      if (!cancel) {
        requestAnimationFrame(run)
      }
    })
    return () => {
      cancel = true
      cancelAnimationFrame(frame)
      void fontsReady
    }
  }, [autoFit, bestFitColumns, columnKey, result, rows])

  const hasColumnWidths = Object.keys(columnWidths).length > 0
  const stickyLayout = useMemo(() => {
    const flagged = new Set((stickyFields ?? []).map((field) => field.toLowerCase()))
    const offsets = new Map<string, number>()
    let start = 0
    let edge = ''
    if (!hasColumnWidths) {
      return { offsets, edge }
    }
    for (const column of displayColumns) {
      const key = column.name.toLowerCase()
      if (!flagged.has(key)) {
        continue
      }
      offsets.set(key, start)
      start += columnWidths[column.name] ?? 0
      edge = key
    }
    return { offsets, edge }
  }, [columnWidths, displayColumns, hasColumnWidths, stickyFields])

  const stickyStyle = (name: string) => {
    const offset = stickyLayout.offsets.get(name.toLowerCase())
    if (offset == null) {
      return undefined
    }
    return { insetInlineStart: offset }
  }

  const stickyClass = (name: string) => {
    const key = name.toLowerCase()
    if (!stickyLayout.offsets.has(key)) {
      return ''
    }
    return key === stickyLayout.edge ? 'sticky-col sticky-edge' : 'sticky-col'
  }

  const wraps = (name: string) => {
    if (columnWrap?.[name]) {
      return true
    }
    const key = name.toLowerCase()
    return Object.keys(columnWrap ?? {}).some((item) => item.toLowerCase() === key)
  }

  const alignOf = (name: string) => {
    const direct = columnAlign?.[name]
    const value =
      direct ??
      Object.entries(columnAlign ?? {}).find(
        ([key]) => key.toLowerCase() === name.toLowerCase(),
      )?.[1]
    return value === 'left' || value === 'center' || value === 'right' ? value : undefined
  }

  const bandOf = (name: string) => {
    const direct = columnBands?.[name]?.trim()
    if (direct) {
      return direct
    }
    return (
      Object.entries(columnBands ?? {}).find(
        ([key]) => key.toLowerCase() === name.toLowerCase(),
      )?.[1]?.trim() ?? ''
    )
  }

  const bandGroups = useMemo(() => {
    const groups: { key: string; label: string; names: string[] }[] = []
    for (const column of displayColumns) {
      const label = bandOf(column.name)
      const previous = groups[groups.length - 1]
      if (label && previous?.label === label) {
        previous.names.push(column.name)
        continue
      }
      groups.push({ key: column.name, label, names: [column.name] })
    }
    return groups.some((group) => group.label) ? groups : null
  }, [columnBands, displayColumns])

  const bandRowRef = useRef<HTMLTableRowElement>(null)
  const [bandRowHeight, setBandRowHeight] = useState(0)
  useLayoutEffect(() => {
    if (!bandGroups) {
      setBandRowHeight(0)
      return
    }
    const height = bandRowRef.current?.getBoundingClientRect().height ?? 0
    setBandRowHeight((current) => (Math.abs(current - height) < 0.5 ? current : height))
  }, [bandGroups, displayColumns, columnWidths])

  if (error) {
    return <div className="banner error">{error}</div>
  }

  if (!result && loading) {
    return <Spinner page label={t('loadingApp')} />
  }

  if (!result) {
    return (
      <div className="empty">
        <IconColumns />
        <div className="kicker">{t('preview')}</div>
        <h2>{t('gridWaiting')}</h2>
        <p>{emptyHint ?? t('dropToQuery')}</p>
      </div>
    )
  }

  const from = result.totalCount === 0 ? 0 : result.skip + 1
  const to = Math.min(result.skip + result.rows.length, result.totalCount)
  const paging = loading && Boolean(result)

  const toggleSort = (column: string) => {
    const next =
      sort?.column !== column
        ? { column, dir: 'asc' as const }
        : sort.dir === 'asc'
          ? { column, dir: 'desc' as const }
          : { column, dir: 'asc' as const }
    if (onSortChange) {
      onSortChange(next)
      return
    }
    setClientSort(next)
  }

  const onResizePointerDown = (
    name: string,
    event: PointerEvent<HTMLSpanElement>,
  ) => {
    event.preventDefault()
    event.stopPropagation()
    event.currentTarget.setPointerCapture(event.pointerId)
    const current = columnWidths[name]
    const measured = event.currentTarget.parentElement?.getBoundingClientRect().width
    resizeDragRef.current = {
      name,
      startX: event.clientX,
      startWidth: current ?? measured ?? MIN_COLUMN_WIDTH,
    }
  }

  const onResizePointerMove = (event: PointerEvent<HTMLSpanElement>) => {
    const drag = resizeDragRef.current
    if (!drag) {
      return
    }
    const rtl = document.documentElement.dir === 'rtl'
    const delta = event.clientX - drag.startX
    const width = Math.min(
      MANUAL_MAX_COLUMN_WIDTH,
      Math.max(MIN_COLUMN_WIDTH, Math.round(drag.startWidth + (rtl ? -delta : delta))),
    )
    userWidthsRef.current = { ...userWidthsRef.current, [drag.name]: width }
    setUserResized(true)
    setColumnWidths((current) => ({ ...current, [drag.name]: width }))
  }

  const onResizePointerUp = () => {
    if (!resizeDragRef.current) {
      return
    }
    resizeDragRef.current = null
    fillToContainer()
  }

  const widthSum = displayColumns.reduce(
    (sum, column) => sum + (columnWidths[column.name] ?? 0),
    0,
  )
  const tableWidth = hasColumnWidths ? widthSum : undefined

  const renderColumnHeader = (column: ReportResult['columns'][number], rowSpan?: number) => {
    const active = sort?.column === column.name
    const ariaSort = !sortEnabled
      ? undefined
      : active
        ? sort?.dir === 'asc'
          ? 'ascending'
          : 'descending'
        : 'none'
    const align = alignOf(column.name)
    const pinned = stickyStyle(column.name)
    const headerStyle =
      pinned && bandGroups && !rowSpan ? { ...pinned, top: bandRowHeight } : pinned
    return (
      <th
        key={column.name}
        data-col={column.name}
        rowSpan={rowSpan}
        aria-sort={ariaSort}
        title={column.label}
        className={[
          stickyClass(column.name),
          align ? `align-${align}` : '',
          wraps(column.name) ? 'wrap-col' : '',
        ]
          .filter(Boolean)
          .join(' ') || undefined}
        style={headerStyle}
      >
        {sortEnabled ? (
          <button
            type="button"
            className={`th-sort ${active ? 'active' : ''}`}
            onClick={() => toggleSort(column.name)}
          >
            <span>{column.label}</span>
            <span className="th-sort-icon" aria-hidden="true">
              {active ? (sort?.dir === 'asc' ? '↑' : '↓') : '↕'}
            </span>
          </button>
        ) : (
          <span className="th-label">{column.label}</span>
        )}
        {hasColumnWidths && (
          <span
            className={`col-resize${resizeDragRef.current?.name === column.name ? ' is-dragging' : ''}`}
            role="separator"
            aria-orientation="vertical"
            aria-label={t('resizeColumn')}
            onPointerDown={(event) => onResizePointerDown(column.name, event)}
            onPointerMove={onResizePointerMove}
            onPointerUp={onResizePointerUp}
            onPointerCancel={onResizePointerUp}
          />
        )}
      </th>
    )
  }

  return (
    <div className="grid-wrap">
      <div className={`table-scroll${paging ? ' is-paging' : ''}`} ref={tableScrollRef}>
        {paging ? <Spinner overlay label={t('loadingApp')} /> : null}
        <table
          ref={tableRef}
          className={`data-table${hasColumnWidths ? ' sized' : ''}${align === 'center' ? ' centered' : ''}${fillWidth ? ' fill-width' : ''}${userResized ? ' user-resized' : ''}`}
          style={tableWidth ? { width: tableWidth, minWidth: tableWidth } : undefined}
        >
          {hasColumnWidths && (
            <colgroup>
              {displayColumns.map((column) => (
                <col
                  key={column.name}
                  style={{ width: columnWidths[column.name] }}
                />
              ))}
            </colgroup>
          )}
          <thead>
            {bandGroups && (
              <tr ref={bandRowRef}>
                {bandGroups.map((group) => {
                  if (!group.label) {
                    return group.names.map((name) => {
                      const column = displayColumns.find((item) => item.name === name)
                      return column ? renderColumnHeader(column, 2) : null
                    })
                  }
                  const pinned = group.names.every((name) =>
                    stickyLayout.offsets.has(name.toLowerCase()),
                  )
                  const spanWidth = group.names.reduce(
                    (sum, name) => sum + (columnWidths[name] ?? 0),
                    0,
                  )
                  return (
                    <th
                      key={`band:${group.key}`}
                      colSpan={group.names.length}
                      className={['band-header', pinned ? stickyClass(group.names[group.names.length - 1]) : '']
                        .filter(Boolean)
                        .join(' ') || undefined}
                      style={{
                        ...(pinned ? stickyStyle(group.names[0]) : {}),
                        width: spanWidth || undefined,
                        maxWidth: spanWidth || undefined,
                      }}
                      title={group.label}
                    >
                      {group.label}
                    </th>
                  )
                })}
              </tr>
            )}
            <tr>
              {displayColumns.map((column) => {
                if (bandGroups && !bandOf(column.name)) {
                  return null
                }
                return renderColumnHeader(column)
              })}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={Math.max(displayColumns.length, 1)} className="muted">
                  {emptyLabel ?? t('noRows')}
                </td>
              </tr>
            ) : (
              rows.map((row, rowIndex) => {
                const key = renderDetail ? parentKey(row) : null
                const open = key !== null && openDetails.includes(key)
                return (
                <Fragment key={rowIndex}>
                <tr className={open ? 'is-detail-open' : undefined}>
                  {displayColumns.map((column, columnIndex) => {
                    const text = formatCell(
                      row[column.name],
                      locale,
                      resolveFormat(column.name),
                      column,
                    )
                    const template =
                      columnUrls?.[column.name] ??
                      Object.entries(columnUrls ?? {}).find(
                        ([name]) => name.toLowerCase() === column.name.toLowerCase(),
                      )?.[1]
                    const href = template
                      ? interpolateUrl(
                          template,
                          row,
                          urlTokens ??
                            result.columns.map((item) => ({
                              id: item.name,
                              field: item.name,
                              label: item.label,
                              displayName: item.displayName?.trim() || undefined,
                              displayNameArabic: item.displayNameArabic?.trim() || undefined,
                            })),
                          column.name,
                        )
                      : null
                    const cf = resolveConditionalStyle(
                      row[column.name],
                      resolveRules(column.name),
                    )
                    const className = [
                      href ? 'has-cell-link' : '',
                      cf?.backgroundColor ? 'cf-cell' : '',
                      stickyClass(column.name),
                      alignOf(column.name) ? `align-${alignOf(column.name)}` : '',
                      wraps(column.name) ? 'wrap-col' : '',
                    ]
                      .filter(Boolean)
                      .join(' ')
                    const pinned = stickyStyle(column.name)
                    const content = href ? (
                      <a
                        className="cell-link"
                        title={text || undefined}
                        {...cellLinkProps(href)}
                        style={
                          cf?.color || cf?.fontWeight
                            ? { color: cf.color, fontWeight: cf.fontWeight }
                            : undefined
                        }
                      >
                        {text}
                      </a>
                    ) : (
                      text
                    )
                    return (
                      <td
                        key={column.name}
                        className={className || undefined}
                        title={text || undefined}
                        style={
                          cf || pinned
                            ? {
                                ...pinned,
                                backgroundColor: cf?.backgroundColor,
                                color: cf?.color,
                                fontWeight: cf?.fontWeight,
                                ...(cf?.backgroundColor
                                  ? { ['--cf-fill']: cf.backgroundColor }
                                  : {}),
                              }
                            : undefined
                        }
                      >
                        {columnIndex === 0 && renderDetail ? (
                          <span className="cell-with-detail">
                            <button
                              type="button"
                              className={`row-expand${open ? ' is-open' : ''}`}
                              aria-expanded={open}
                              disabled={!key}
                              aria-label={open ? t('detailCollapse') : t('detailExpand')}
                              onClick={() => {
                                if (!key) {
                                  return
                                }
                                setOpenDetails((current) =>
                                  current.includes(key)
                                    ? current.filter((item) => item !== key)
                                    : [...current, key],
                                )
                              }}
                            >
                              <svg viewBox="0 0 24 24" aria-hidden="true">
                                <path
                                  d="M9 6.5 15.5 12 9 17.5"
                                  fill="none"
                                  stroke="currentColor"
                                  strokeWidth="2.25"
                                  strokeLinecap="round"
                                  strokeLinejoin="round"
                                />
                              </svg>
                            </button>
                            <span className="cell-detail-value">{content}</span>
                          </span>
                        ) : (
                          content
                        )}
                      </td>
                    )
                  })}
                </tr>
                {open && key && renderDetail && (
                  <tr className="detail-row">
                    <td colSpan={Math.max(displayColumns.length, 1)}>
                      <div className="detail-slot">{renderDetail(key)}</div>
                    </td>
                  </tr>
                )}
                </Fragment>
                )
              })
            )}
          </tbody>
        </table>
      </div>
      <div className="pager">
        <span className="muted">
          {t('pagerRange', {
            from,
            to,
            total: result.totalCount.toLocaleString(locale),
          })}
          {loading ? t('refreshing') : ''}
          {allowSort && !serverSort && sort ? t('sortedPage') : ''}
        </span>
        <div className="pager-actions">
          {paging ? <Spinner size="sm" label={t('loadingApp')} /> : null}
          <button
            type="button"
            className="btn"
            disabled={result.skip === 0 || paging}
            onClick={() => onPageChange(Math.max(0, result.skip - result.take))}
          >
            {t('previous')}
          </button>
          <button
            type="button"
            className="btn"
            disabled={result.skip + result.take >= result.totalCount || paging}
            onClick={() => onPageChange(result.skip + result.take)}
          >
            {t('next')}
          </button>
        </div>
      </div>
    </div>
  )
}
