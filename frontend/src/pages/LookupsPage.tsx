import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { createPortal } from 'react-dom'
import { api } from '../api/client'
import type { LookupColumn, LookupDefinition, LookupSourceKind, SortDirection } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { IconBookmark } from '../components/Icons'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'

function readSourceKind(value: string): LookupSourceKind {
  if (value === 'Query' || value === 'query') return 'Query'
  if (value === 'Table' || value === 'table') return 'Table'
  return 'View'
}

function wireSourceKind(kind: LookupSourceKind): 'view' | 'query' | 'table' {
  if (kind === 'Query') return 'query'
  if (kind === 'Table') return 'table'
  return 'view'
}

const emptyForm = {
  id: '' as string | undefined,
  name: '',
  nameArabic: '',
  sourceKind: 'View' as LookupSourceKind,
  schemaName: 'dbo',
  objectName: '',
  sqlText: '',
  keyColumn: '',
  labelColumn: '',
  sortColumn: '',
  sortDirection: 'asc' as SortDirection,
}

type LookupForm = typeof emptyForm

function formFromLookup(lookup: LookupDefinition): LookupForm {
  return {
    id: lookup.id,
    name: lookup.name,
    nameArabic: lookup.nameArabic ?? '',
    sourceKind: readSourceKind(lookup.sourceKind),
    schemaName: lookup.schemaName || 'dbo',
    objectName: lookup.objectName,
    sqlText: lookup.sqlText ?? '',
    keyColumn: lookup.keyColumn,
    labelColumn: lookup.labelColumn,
    sortColumn: lookup.sortColumn ?? lookup.labelColumn,
    sortDirection: lookup.sortDirection === 'desc' ? 'desc' : 'asc',
  }
}

type ColumnLocale = 'default' | 'en' | 'ar'

function columnLocale(value: string | null | undefined): ColumnLocale {
  return value === 'en' || value === 'ar' ? value : 'default'
}

function localesFrom(columns: LookupColumn[]) {
  const locales: Record<string, ColumnLocale> = {}
  for (const column of columns) {
    locales[column.name] = columnLocale(column.locale)
  }
  return locales
}

type ColumnAlias = { displayName: string; displayNameArabic: string }

function aliasesFrom(columns: LookupColumn[]) {
  const aliases: Record<string, ColumnAlias> = {}
  for (const column of columns) {
    aliases[column.name] = {
      displayName: column.displayName ?? '',
      displayNameArabic: column.displayNameArabic ?? '',
    }
  }
  return aliases
}

type ColumnChoice = { included: boolean; showInDesigner: boolean }

function sameName(left: string, right: string) {
  return left.trim().localeCompare(right.trim(), undefined, { sensitivity: 'accent' }) === 0
}

function choicesFrom(columns: LookupColumn[]) {
  const choices: Record<string, ColumnChoice> = {}
  for (const column of columns) {
    choices[column.name] = {
      included: column.included !== false,
      showInDesigner: column.showInDesigner === true,
    }
  }
  return choices
}

function mergeColumns(preview: LookupColumn[] | undefined, saved: LookupColumn[] | undefined) {
  if (!preview?.length) return saved ?? []
  const prior = new Map((saved ?? []).map((column) => [column.name.toLowerCase(), column]))
  return preview.map((column) => {
    const stored = prior.get(column.name.toLowerCase())
    if (!stored) return column
    return {
      ...column,
      displayName: stored.displayName,
      displayNameArabic: stored.displayNameArabic,
      locale: stored.locale,
      included: stored.included,
      showInDesigner: stored.showInDesigner,
    }
  })
}

function lookupInUse(message: string) {
  const marker = 'LOOKUP_IN_USE|'
  const index = message.indexOf(marker)
  if (index < 0) return null
  return message
    .slice(index + marker.length)
    .split(';')
    .map((item) => item.trim())
    .filter(Boolean)
}

function sourceLabel(
  lookup: Pick<LookupDefinition, 'sourceKind' | 'schemaName' | 'objectName'>,
  queryLabel: string,
) {
  return readSourceKind(lookup.sourceKind) === 'Query'
    ? queryLabel
    : `${lookup.schemaName}.${lookup.objectName}`
}

export function LookupsPage() {
  const { can } = useAuth()
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const canUpdate = can('lookups.update')
  const [form, setForm] = useState<LookupForm>(emptyForm)
  const [creating, setCreating] = useState(false)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [filter, setFilter] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [columnLocales, setColumnLocales] = useState<Record<string, ColumnLocale>>({})
  const [columnAliases, setColumnAliases] = useState<Record<string, ColumnAlias>>({})
  const [columnChoices, setColumnChoices] = useState<Record<string, ColumnChoice>>({})
  const [objectFilter, setObjectFilter] = useState('')
  const [sqlQuery, setSqlQuery] = useState('')
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [deleteBlocked, setDeleteBlocked] = useState<string[] | null>(null)
  const schemaQuery = form.schemaName.trim()

  const lookups = useQuery({
    queryKey: ['lookups'],
    queryFn: api.listLookups,
  })

  const objects = useQuery({
    queryKey: ['lookup-objects', schemaQuery],
    queryFn: () => api.listLookupObjects(schemaQuery),
    enabled: form.sourceKind !== 'Query' && schemaQuery.length > 0,
    staleTime: 60_000,
  })

  const save = useMutation({
    mutationFn: () =>
      api.saveLookup({
        id: form.id || undefined,
        name: form.name.trim(),
        nameArabic: form.nameArabic.trim() || null,
        sourceKind: wireSourceKind(form.sourceKind),
        schemaName: form.schemaName.trim(),
        objectName: form.objectName.trim(),
        sqlText: form.sqlText,
        keyColumn: form.keyColumn,
        labelColumn: form.labelColumn,
        sortColumn: form.sortColumn || form.labelColumn,
        sortDirection: form.sortDirection,
        columnLocales: columns.map((column) => {
          const forced = sameName(column.name, form.keyColumn) || sameName(column.name, sortName)
          const choice = columnChoices[column.name]
          const included = forced || (choice ? choice.included : column.included !== false)
          return {
            name: column.name,
            locale: columnLocales[column.name] ?? 'default',
            displayName: columnAliases[column.name]?.displayName ?? '',
            displayNameArabic: columnAliases[column.name]?.displayNameArabic ?? '',
            included,
            showInDesigner: included && (choice ? choice.showInDesigner : column.showInDesigner === true),
          }
        }),
      }),
    onSuccess: async (saved) => {
      setError(null)
      setCreating(false)
      setSelectedId(saved.id)
      setForm(formFromLookup(saved))
      setColumnLocales(localesFrom(saved.columns))
      setColumnAliases(aliasesFrom(saved.columns))
      setColumnChoices(choicesFrom(saved.columns))
      queryClient.setQueryData<LookupDefinition[]>(['lookups'], (current) => {
        const list = current ?? []
        const index = list.findIndex((item) => item.id === saved.id)
        if (index < 0) return [saved, ...list]
        const next = list.slice()
        next[index] = saved
        return next
      })
      await queryClient.invalidateQueries({ queryKey: ['datasets'] })
      await queryClient.invalidateQueries({ queryKey: ['dataset'] })
    },
    onError: (reason) => setError((reason as Error).message),
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteLookup(id),
    onSuccess: async () => {
      setError(null)
      setDeleteOpen(false)
      setDeleteBlocked(null)
      setCreating(false)
      setSelectedId(null)
      setForm(emptyForm)
      setColumnLocales({})
      setColumnAliases({})
      setColumnChoices({})
      await queryClient.invalidateQueries({ queryKey: ['lookups'] })
      await queryClient.invalidateQueries({ queryKey: ['dataset'] })
      await queryClient.invalidateQueries({ queryKey: ['datasets'] })
    },
    onError: (reason) => {
      const blocked = lookupInUse((reason as Error).message)
      if (blocked) {
        setError(null)
        setDeleteBlocked(blocked)
        setDeleteOpen(true)
        return
      }
      setDeleteOpen(false)
      setError((reason as Error).message)
    },
  })

  const visible = useMemo(() => {
    const list = lookups.data ?? []
    const q = filter.trim().toLowerCase()
    if (!q) return list
    return list.filter((lookup) =>
      `${lookup.name} ${lookup.nameArabic ?? ''} ${lookup.schemaName} ${lookup.objectName} ${lookup.keyColumn} ${lookup.labelColumn}`
        .toLowerCase()
        .includes(q),
    )
  }, [lookups.data, filter])

  const selected = lookups.data?.find((lookup) => lookup.id === selectedId) ?? null
  const sourceMatchesSaved =
    Boolean(selected) &&
    !creating &&
    readSourceKind(selected!.sourceKind) === form.sourceKind &&
    (form.sourceKind === 'Query'
      ? (selected!.sqlText ?? '').trim() === form.sqlText.trim()
      : selected!.schemaName.localeCompare(form.schemaName.trim(), undefined, { sensitivity: 'accent' }) === 0 &&
        selected!.objectName.localeCompare(form.objectName.trim(), undefined, { sensitivity: 'accent' }) === 0)
  const columnPreview = useQuery({
    queryKey:
      form.sourceKind === 'Query'
        ? ['lookup-columns', 'query', sqlQuery]
        : ['lookup-columns', form.schemaName.trim(), form.objectName.trim()],
    queryFn: () =>
      form.sourceKind === 'Query'
        ? api.previewLookupQueryColumns(sqlQuery)
        : api.previewLookupColumns(form.schemaName.trim(), form.objectName.trim()),
    enabled:
      form.sourceKind === 'Query'
        ? sqlQuery.length > 0
        : form.schemaName.trim().length > 0 && form.objectName.trim().length > 0,
  })
  const columns = sourceMatchesSaved
    ? mergeColumns(columnPreview.data, selected?.columns)
    : (columnPreview.data ?? [])
  const sortName = form.sortColumn.trim() || form.labelColumn.trim()
  const validCount = lookups.data?.filter((lookup) => lookup.isValid).length ?? 0
  const showEditor = creating || selectedId !== null

  useEffect(() => {
    const handle = window.setTimeout(() => setSqlQuery(form.sqlText.trim()), 400)
    return () => window.clearTimeout(handle)
  }, [form.sqlText])

  useEffect(() => {
    if (!deleteOpen) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !remove.isPending) {
        setDeleteOpen(false)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [deleteOpen, remove.isPending])

  const schemaObjects = useMemo(() => {
    const kind = form.sourceKind
    const q = objectFilter.trim().toLowerCase()
    return (objects.data ?? []).filter(
      (item) =>
        readSourceKind(item.objectType) === kind &&
        (!q || item.objectName.toLowerCase().includes(q)),
    )
  }, [objects.data, form.sourceKind, objectFilter])

  function openLookup(lookup: LookupDefinition) {
    setError(null)
    setCreating(false)
    setSelectedId(lookup.id)
    setForm(formFromLookup(lookup))
    setColumnLocales(localesFrom(lookup.columns))
    setColumnAliases(aliasesFrom(lookup.columns))
    setColumnChoices(choicesFrom(lookup.columns))
    setObjectFilter('')
  }

  function startCreate() {
    setError(null)
    setCreating(true)
    setSelectedId(null)
    setForm(emptyForm)
    setColumnLocales({})
    setColumnAliases({})
    setColumnChoices({})
    setObjectFilter('')
  }

  return (
    <section className="page catalog lookups">
      <header className="page-header">
        <div>
          <div className="kicker">{t('sourcesKicker')}</div>
          <h1>{t('lookupTitle')}</h1>
          <p className="lede">{t('lookupHint')}</p>
        </div>
        {canUpdate && (
          <button type="button" className="btn primary" onClick={startCreate}>
            {t('lookupNew')}
          </button>
        )}
      </header>

      <div className="stats">
        <div className="stat">
          <span className="muted small">{t('lookupTitle')}</span>
          <b>{lookups.data?.length ?? 0}</b>
        </div>
        <div className="stat">
          <span className="muted small">{t('lookupValid')}</span>
          <b>{validCount}</b>
        </div>
        <div className="stat">
          <span className="muted small">{t('lookupInvalid')}</span>
          <b>{(lookups.data?.length ?? 0) - validCount}</b>
        </div>
      </div>

      {error && <div className="banner error">{error}</div>}
      {lookups.isError && <div className="banner error">{(lookups.error as Error).message}</div>}

      {lookups.isLoading ? (
        <Spinner page label={t('loadingApp')} />
      ) : !lookups.data?.length && !creating ? (
        <div className="empty">
          <IconBookmark />
          <h2>{t('lookupEmptyTitle')}</h2>
          <p>{t('lookupEmptyHint')}</p>
          {canUpdate && (
            <p>
              <button type="button" className="btn primary" onClick={startCreate}>
                {t('lookupNew')}
              </button>
            </p>
          )}
        </div>
      ) : (
        <div className="lookup-workspace">
          <div className="lookup-list-pane">
            <input
              className="search"
              placeholder={t('lookupSearch')}
              value={filter}
              onChange={(event) => setFilter(event.target.value)}
            />
            <div className="lookup-list">
              {visible.map((lookup) => (
                <button
                  key={lookup.id}
                  type="button"
                  className={`lookup-item${selectedId === lookup.id && !creating ? ' is-selected' : ''}`}
                  onClick={() => openLookup(lookup)}
                >
                  <span className="lookup-item-title">
                    <strong>{localizedText(lang, lookup.name, lookup.nameArabic)}</strong>
                    <span className={`badge ${lookup.isValid ? 'published' : 'draft'}`}>
                      {lookup.isValid ? t('lookupValid') : t('lookupInvalid')}
                    </span>
                  </span>
                  <small>{sourceLabel(lookup, t('lookupQuery'))}</small>
                  <small>
                    {lookup.keyColumn} · {lookup.labelColumn}
                  </small>
                </button>
              ))}
              {!visible.length && <p className="muted small">{t('noMatchingAccess')}</p>}
            </div>
          </div>

          {showEditor ? (
            <form
              className="lookup-editor catalog-card"
              onSubmit={(event) => {
                event.preventDefault()
                if (canUpdate) save.mutate()
              }}
            >
              <div className="lookup-editor-head">
                <h2>{creating ? t('lookupNew') : localizedText(lang, form.name, form.nameArabic)}</h2>
                {selected && !creating && (
                  <span className={`badge ${selected.isValid ? 'published' : 'draft'}`}>
                    {selected.isValid ? t('lookupValid') : t('lookupInvalid')}
                  </span>
                )}
              </div>
              {selected && !creating && selected.validationError && (
                <div className="banner error">{selected.validationError}</div>
              )}

              <div className="lookup-section">
                <h3>{t('lookupName')}</h3>
                <div className="lookup-fields">
                  <label>
                    {t('lookupName')}
                    <input
                      value={form.name}
                      required
                      disabled={!canUpdate}
                      onChange={(event) => setForm({ ...form, name: event.target.value })}
                    />
                  </label>
                  <label>
                    {t('labelArabic')}
                    <input
                      dir="rtl"
                      lang="ar"
                      value={form.nameArabic}
                      disabled={!canUpdate}
                      onChange={(event) => setForm({ ...form, nameArabic: event.target.value })}
                    />
                  </label>
                </div>
              </div>

              <div className="lookup-section">
                <h3>{t('lookupSourceSection')}</h3>
                <div className="lookup-kind" role="group" aria-label={t('lookupSource')}>
                  {(['View', 'Table', 'Query'] as const).map((kind) => (
                    <button
                      key={kind}
                      type="button"
                      className={form.sourceKind === kind ? 'is-on' : ''}
                      aria-pressed={form.sourceKind === kind}
                      disabled={!canUpdate}
                      onClick={() => {
                        setObjectFilter('')
                        setColumnChoices({})
                        setForm((current) =>
                          current.sourceKind === kind
                            ? current
                            : {
                                ...current,
                                sourceKind: kind,
                                objectName: kind === 'Query' ? current.objectName : '',
                                keyColumn: '',
                                labelColumn: '',
                                sortColumn: '',
                              },
                        )
                      }}
                    >
                      {kind === 'View'
                        ? t('lookupView')
                        : kind === 'Table'
                          ? t('lookupTable')
                          : t('lookupQuery')}
                    </button>
                  ))}
                </div>
                {form.sourceKind === 'Query' ? (
                  <label>
                    {t('lookupSql')}
                    <textarea
                      className="lookup-sql"
                      rows={8}
                      value={form.sqlText}
                      required
                      disabled={!canUpdate}
                      spellCheck={false}
                      onChange={(event) => setForm({ ...form, sqlText: event.target.value })}
                    />
                    {columnPreview.isError && (
                      <span className="field-error">{(columnPreview.error as Error).message}</span>
                    )}
                  </label>
                ) : (
                  <div className="lookup-fields">
                    <label>
                      {t('lookupSchema')}
                      <input
                        value={form.schemaName}
                        required
                        disabled={!canUpdate}
                        spellCheck={false}
                        onChange={(event) => {
                          setObjectFilter('')
                          setColumnChoices({})
                          setForm({
                            ...form,
                            schemaName: event.target.value,
                            objectName: '',
                            keyColumn: '',
                            labelColumn: '',
                            sortColumn: '',
                          })
                        }}
                      />
                    </label>
                    <label>
                      {form.sourceKind === 'Table' ? t('lookupTableName') : t('lookupObject')}
                      <input
                        value={objectFilter}
                        disabled={!canUpdate || !form.schemaName}
                        placeholder={t('lookupObjectFilter')}
                        onChange={(event) => setObjectFilter(event.target.value)}
                      />
                      <select
                        value={form.objectName}
                        required
                        disabled={!canUpdate || !schemaQuery || objects.isFetching}
                        onChange={(event) => {
                          setColumnChoices({})
                          setForm({
                            ...form,
                            objectName: event.target.value,
                            keyColumn: '',
                            labelColumn: '',
                            sortColumn: '',
                          })
                        }}
                      >
                        <option value="">{t('lookupSelectObject')}</option>
                        {schemaObjects.map((item) => (
                          <option key={item.objectName} value={item.objectName}>
                            {item.objectName}
                          </option>
                        ))}
                      </select>
                      {objects.isError && (
                        <span className="field-error">{(objects.error as Error).message}</span>
                      )}
                      {!objects.isFetching && schemaQuery && schemaObjects.length === 0 && (
                        <span className="muted small">{t('lookupNoObjects')}</span>
                      )}
                    </label>
                  </div>
                )}
              </div>

              <div className="lookup-section">
                <h3>{t('lookupMapping')}</h3>
                <div className="lookup-fields">
                  <ColumnField
                    label={t('lookupKey')}
                    value={form.keyColumn}
                    columns={columns}
                    required
                    disabled={!canUpdate}
                    onChange={(keyColumn) => setForm({ ...form, keyColumn })}
                  />
                  <ColumnField
                    label={t('lookupLabel')}
                    value={form.labelColumn}
                    columns={columns}
                    required
                    disabled={!canUpdate}
                    onChange={(labelColumn) =>
                      setForm({
                        ...form,
                        labelColumn,
                        sortColumn: form.sortColumn.trim() ? form.sortColumn : labelColumn,
                      })
                    }
                  />
                  <ColumnField
                    label={t('lookupSort')}
                    value={form.sortColumn}
                    columns={columns}
                    disabled={!canUpdate}
                    onChange={(sortColumn) => setForm({ ...form, sortColumn })}
                  />
                  <label>
                    {t('columnSort')}
                    <select
                      value={form.sortDirection}
                      disabled={!canUpdate}
                      onChange={(event) =>
                        setForm({ ...form, sortDirection: event.target.value as SortDirection })
                      }
                    >
                      <option value="asc">{t('ascending')}</option>
                      <option value="desc">{t('descending')}</option>
                    </select>
                  </label>
                </div>
                {columnPreview.isFetching && columns.length === 0 && (
                  <p className="muted small">{t('lookupColumnsLoading')}</p>
                )}
                {columns.length > 0 && (
                  <div className="lookup-columns">
                    <span className="muted small">{t('lookupColumnLocaleHint')}</span>
                    <table className="lookup-column-table">
                      <thead>
                        <tr>
                          <th className="check-cell">{t('lookupColumnUse')}</th>
                          <th>{t('column')}</th>
                          <th>{t('type')}</th>
                          <th>{t('label')}</th>
                          <th>{t('labelArabic')}</th>
                          <th>{t('columnLocale')}</th>
                          <th className="check-cell">{t('lookupColumnDesigner')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {columns.map((column) => {
                          const forced =
                            sameName(column.name, form.keyColumn) || sameName(column.name, sortName)
                          const choice = columnChoices[column.name]
                          const included = forced || (choice ? choice.included : column.included !== false)
                          const showInDesigner =
                            included && (choice ? choice.showInDesigner : column.showInDesigner === true)
                          return (
                          <tr key={column.name}>
                            <td className="check-cell">
                              <input
                                type="checkbox"
                                checked={included}
                                disabled={!canUpdate || forced}
                                aria-label={`${column.name} ${t('lookupColumnUse')}`}
                                onChange={(event) =>
                                  setColumnChoices((current) => ({
                                    ...current,
                                    [column.name]: {
                                      included: event.target.checked,
                                      showInDesigner: event.target.checked
                                        ? (current[column.name]?.showInDesigner ?? column.showInDesigner === true)
                                        : false,
                                    },
                                  }))
                                }
                              />
                            </td>
                            <td>{column.name}</td>
                            <td className="muted">{column.sqlType}</td>
                            <td>
                              <input
                                value={columnAliases[column.name]?.displayName ?? ''}
                                disabled={!canUpdate || !included}
                                placeholder={column.name}
                                aria-label={`${column.name} ${t('label')}`}
                                onChange={(event) =>
                                  setColumnAliases((current) => ({
                                    ...current,
                                    [column.name]: {
                                      displayName: event.target.value,
                                      displayNameArabic:
                                        current[column.name]?.displayNameArabic ?? '',
                                    },
                                  }))
                                }
                              />
                            </td>
                            <td>
                              <input
                                dir="rtl"
                                lang="ar"
                                value={columnAliases[column.name]?.displayNameArabic ?? ''}
                                disabled={!canUpdate || !included}
                                aria-label={`${column.name} ${t('labelArabic')}`}
                                onChange={(event) =>
                                  setColumnAliases((current) => ({
                                    ...current,
                                    [column.name]: {
                                      displayName: current[column.name]?.displayName ?? '',
                                      displayNameArabic: event.target.value,
                                    },
                                  }))
                                }
                              />
                            </td>
                            <td>
                              <select
                                value={columnLocales[column.name] ?? 'default'}
                                disabled={!canUpdate || !included}
                                aria-label={`${column.name} ${t('columnLocale')}`}
                                onChange={(event) =>
                                  setColumnLocales((current) => ({
                                    ...current,
                                    [column.name]: columnLocale(event.target.value),
                                  }))
                                }
                              >
                                <option value="default">{t('columnLocaleDefault')}</option>
                                <option value="en">{t('columnLocaleEnglish')}</option>
                                <option value="ar">{t('columnLocaleArabic')}</option>
                              </select>
                            </td>
                            <td className="check-cell">
                              <input
                                type="checkbox"
                                checked={showInDesigner}
                                disabled={!canUpdate || !included}
                                aria-label={`${column.name} ${t('lookupColumnDesigner')}`}
                                onChange={(event) =>
                                  setColumnChoices((current) => ({
                                    ...current,
                                    [column.name]: {
                                      included: true,
                                      showInDesigner: event.target.checked,
                                    },
                                  }))
                                }
                              />
                            </td>
                          </tr>
                          )
                        })}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>

              {canUpdate && (
                <div className="lookup-actions">
                  <button type="submit" className="btn primary" disabled={save.isPending}>
                    {save.isPending ? t('saving') : t('lookupSave')}
                  </button>
                  <button
                    type="button"
                    className="btn"
                    onClick={() => {
                      setError(null)
                      if (selected && !creating) {
                        setForm(formFromLookup(selected))
                        setColumnLocales(localesFrom(selected.columns))
                        setColumnAliases(aliasesFrom(selected.columns))
                        setColumnChoices(choicesFrom(selected.columns))
                        setObjectFilter('')
                        return
                      }
                      setCreating(false)
                      setSelectedId(null)
                      setForm(emptyForm)
                      setColumnLocales({})
                      setColumnAliases({})
                      setColumnChoices({})
                      setObjectFilter('')
                    }}
                  >
                    {t('cancel')}
                  </button>
                  {selected && !creating && (
                    <button
                      type="button"
                      className="btn danger"
                      disabled={remove.isPending}
                      onClick={() => {
                        setDeleteBlocked(null)
                        setDeleteOpen(true)
                      }}
                    >
                      {t('lookupDelete')}
                    </button>
                  )}
                </div>
              )}
            </form>
          ) : (
            <div className="lookup-editor catalog-card lookup-placeholder">
              <IconBookmark />
              <h2>{t('lookupPick')}</h2>
            </div>
          )}
        </div>
      )}
      {deleteOpen &&
        selected &&
        createPortal(
          <div
            className="grants-overlay"
            role="presentation"
            onClick={() => {
              if (!remove.isPending) setDeleteOpen(false)
            }}
          >
            <div
              className="grants-dialog confirm-dialog"
              role="dialog"
              aria-modal="true"
              aria-labelledby="lookup-delete-title"
              onClick={(event) => event.stopPropagation()}
            >
              <header className="grants-chrome">
                <div className="grants-head">
                  <div>
                    <h2 id="lookup-delete-title">
                      {deleteBlocked ? t('lookupDeleteBlocked') : t('lookupDeleteTitle')}
                    </h2>
                  </div>
                </div>
              </header>
              <div className="confirm-dialog-body">
                {deleteBlocked ? (
                  <ul className="lookup-delete-list">
                    {deleteBlocked.map((item, index) => (
                      <li key={`${item}-${index}`}>{item}</li>
                    ))}
                  </ul>
                ) : (
                  <p>{t('lookupDeleteConfirm')}</p>
                )}
              </div>
              <div className="confirm-dialog-foot">
                <button
                  type="button"
                  className="btn"
                  disabled={remove.isPending}
                  onClick={() => setDeleteOpen(false)}
                >
                  {deleteBlocked ? t('close') : t('cancel')}
                </button>
                {!deleteBlocked && (
                  <button
                    type="button"
                    className="btn danger"
                    disabled={remove.isPending}
                    onClick={() => remove.mutate(selected.id)}
                  >
                    {remove.isPending ? t('saving') : t('lookupDelete')}
                  </button>
                )}
              </div>
            </div>
          </div>,
          document.body,
        )}
    </section>
  )
}

function ColumnField({
  label,
  value,
  columns,
  required,
  disabled,
  onChange,
}: {
  label: string
  value: string
  columns: LookupColumn[]
  required?: boolean
  disabled?: boolean
  onChange: (value: string) => void
}) {
  const known = columns.some(
    (column) => column.name.localeCompare(value, undefined, { sensitivity: 'accent' }) === 0,
  )
  const options = !value || known ? columns : [{ name: value, sqlType: '' }, ...columns]
  return (
    <label>
      {label}
      <select
        value={value}
        required={required}
        disabled={disabled || options.length === 0}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="" />
        {options.map((column) => (
          <option key={column.name} value={column.name}>
            {column.sqlType ? `${column.name} · ${column.sqlType}` : column.name}
          </option>
        ))}
      </select>
    </label>
  )
}
