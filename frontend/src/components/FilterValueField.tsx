import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo } from 'react'
import { api } from '../api/client'
import type { DatasetColumn, LayoutFilter } from '../api/types'
import { cascadeConstraintKey } from '../lib/filters'
import { SearchableSelect } from './SearchableSelect'
import { useLocale } from '../i18n/LocaleProvider'

const DATE_TYPES = new Set([
  'date',
  'datetime',
  'datetime2',
  'smalldatetime',
  'datetimeoffset',
])

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

type Props = {
  datasetId: string
  reportId?: string
  column?: DatasetColumn
  operator: string
  value: string
  contextFilters?: LayoutFilter[]
  onChange: (value: string) => void
}

function toDateInput(value: string) {
  if (!value) {
    return ''
  }
  const iso = value.match(/^(\d{4})-(\d{2})-(\d{2})/)
  if (iso) {
    return `${iso[1]}-${iso[2]}-${iso[3]}`
  }
  const dotted = value.match(/^(\d{4})[/.](\d{2})[/.](\d{2})/)
  if (dotted) {
    return `${dotted[1]}-${dotted[2]}-${dotted[3]}`
  }
  const parsed = Date.parse(value)
  if (!Number.isNaN(parsed)) {
    const date = new Date(parsed)
    const year = date.getFullYear()
    const month = String(date.getMonth() + 1).padStart(2, '0')
    const day = String(date.getDate()).padStart(2, '0')
    return `${year}-${month}-${day}`
  }
  return ''
}

function splitListValues(raw: string) {
  return raw
    .split(/[,;\n]/)
    .map((part) => part.trim())
    .filter(Boolean)
}

function matchesOption(value: string, options: string[]) {
  const lower = value.toLowerCase()
  return options.some((option) => option.toLowerCase() === lower)
}

export function FilterValueField({
  datasetId,
  reportId,
  column,
  operator,
  value,
  contextFilters,
  onChange,
}: Props) {
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const sqlType = column?.sqlType.toLowerCase() ?? ''
  const isDate = DATE_TYPES.has(sqlType)
  const isNumeric = NUMERIC_TYPES.has(sqlType)
  const isBit = sqlType === 'bit'
  const isTime = sqlType === 'time'
  const wantsList =
    !isDate &&
    !isNumeric &&
    !isBit &&
    !isTime &&
    (operator === 'eq' || operator === 'neq' || operator === 'in' || operator === 'contains')

  const constraintKey = cascadeConstraintKey(column?.columnName, contextFilters)
  const scopedFilters = constraintKey ? contextFilters : undefined

  const valuesQuery = useQuery({
    queryKey: ['column-values', reportId ?? datasetId, column?.columnName, constraintKey, lang],
    queryFn: () =>
      reportId
        ? api.listReportFilterValues(reportId, column!.columnName, scopedFilters, lang)
        : api.listColumnValues(datasetId, column!.columnName, scopedFilters, lang),
    enabled: Boolean((reportId || datasetId) && column && wantsList),
    staleTime: 5 * 60 * 1000,
    placeholderData: keepPreviousData,
  })

  useEffect(() => {
    if (valuesQuery.isError) {
      void queryClient.invalidateQueries({ queryKey: ['datasets'] })
    }
  }, [queryClient, valuesQuery.isError])

  useEffect(() => {
    if (!wantsList || operator === 'contains') {
      return
    }
    if (!valuesQuery.isSuccess || valuesQuery.isFetching) {
      return
    }
    const options = valuesQuery.data.values
    if (valuesQuery.data.truncated) {
      return
    }
    const current = value.trim()
    if (!current) {
      return
    }
    if (operator === 'in') {
      const parts = splitListValues(current)
      const kept = parts.filter((part) => matchesOption(part, options))
      if (kept.length !== parts.length) {
        onChange(kept.join(', '))
      }
      return
    }
    if (!matchesOption(current, options)) {
      onChange('')
    }
  }, [
    wantsList,
    operator,
    value,
    column,
    valuesQuery.isSuccess,
    valuesQuery.isFetching,
    valuesQuery.data,
    onChange,
  ])

  const labels = useMemo(() => {
    const map: Record<string, string> = {}
    for (const choice of valuesQuery.data?.choices ?? []) {
      if (choice.label && choice.label !== choice.value) {
        map[choice.value] = choice.label
      }
    }
    return map
  }, [valuesQuery.data?.choices])

  if (isDate) {
    return (
      <input
        type="date"
        value={toDateInput(value)}
        onChange={(event) => onChange(event.target.value)}
      />
    )
  }

  if (isTime) {
    return (
      <input
        type="time"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    )
  }

  if (isNumeric) {
    return (
      <input
        type="number"
        step="any"
        value={value}
        placeholder="0"
        onChange={(event) => onChange(event.target.value)}
      />
    )
  }

  if (isBit) {
    return (
      <SearchableSelect
        options={['true', 'false']}
        value={value}
        placeholder={t('searchTrueFalse')}
        onChange={onChange}
      />
    )
  }

  const options = valuesQuery.data?.values ?? []
  const truncated = valuesQuery.data?.truncated ?? false

  if (wantsList && (options.length > 0 || valuesQuery.isFetching)) {
    return (
      <SearchableSelect
        options={options}
        value={value}
        multiple={operator === 'in'}
        loading={valuesQuery.isFetching && options.length === 0}
        allowCustom={truncated || operator === 'contains'}
        labels={labels}
        placeholder={
          operator === 'in' ? t('searchAddValues') : t('searchValues')
        }
        onChange={onChange}
      />
    )
  }

  return (
    <input
      value={value}
      placeholder={t('value')}
      onChange={(event) => onChange(event.target.value)}
    />
  )
}
