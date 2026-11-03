import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { api } from '../api/client'
import type { DatasetColumn } from '../api/types'
import { useLocale } from '../i18n/LocaleProvider'
import { columnLabel } from '../i18n/localized'
import type { MessageKey } from '../i18n/messages'

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

const FUNCTIONS: { title: MessageKey; items: { name: string; template: string }[] }[] = [
  {
    title: 'calcFnNumber',
    items: [
      { name: 'ROUND', template: 'ROUND(|, 2)' },
      { name: 'ABS', template: 'ABS(|)' },
      { name: 'FLOOR', template: 'FLOOR(|)' },
      { name: 'CEILING', template: 'CEILING(|)' },
    ],
  },
  {
    title: 'calcFnText',
    items: [
      { name: 'CONCAT', template: "CONCAT(|, ' ')" },
      { name: 'UPPER', template: 'UPPER(|)' },
      { name: 'LEFT', template: 'LEFT(|, 4)' },
      { name: 'LEN', template: 'LEN(|)' },
      { name: 'TRIM', template: 'TRIM(|)' },
    ],
  },
  {
    title: 'calcFnDate',
    items: [
      { name: 'YEAR', template: 'YEAR(|)' },
      { name: 'MONTH', template: 'MONTH(|)' },
      { name: 'DAY', template: 'DAY(|)' },
      { name: 'DATEPART', template: 'DATEPART(year, |)' },
    ],
  },
  {
    title: 'calcFnEmpty',
    items: [
      { name: 'ISNULL', template: 'ISNULL(|, 0)' },
      { name: 'COALESCE', template: 'COALESCE(|, 0)' },
      { name: 'NULLIF', template: 'NULLIF(|, 0)' },
    ],
  },
]

const AGGREGATES = [
  { name: 'SUM', template: 'SUM(|)' },
  { name: 'COUNT', template: 'COUNT(|)' },
  { name: 'AVG', template: 'AVG(|)' },
  { name: 'MIN', template: 'MIN(|)' },
  { name: 'MAX', template: 'MAX(|)' },
]

type Props = {
  open: boolean
  editing: boolean
  initialLabel: string
  initialExpression: string
  columns: DatasetColumn[]
  datasetId: string
  allowAggregates: boolean
  onClose: () => void
  onSave: (label: string, expression: string) => void
}

function isNumeric(sqlType: string) {
  return NUMERIC_TYPES.has(sqlType.toLowerCase())
}

function isDate(sqlType: string) {
  return /date|time/i.test(sqlType)
}

function bracket(name: string) {
  return `[${name}]`
}

function insertAt(
  value: string,
  start: number,
  end: number,
  snippet: string,
) {
  const before = value.slice(0, start)
  const needsSpace = before.length > 0 && !/[\s([+\-*/=,]$/.test(before)
  const marker = snippet.indexOf('|')
  const text = (needsSpace ? ' ' : '') + snippet.replace('|', '')
  const caretOffset = needsSpace ? 1 : 0
  const caret =
    start + caretOffset + (marker >= 0 ? marker : snippet.replace('|', '').length)
  return { next: before + text + value.slice(end), caret }
}

export function CalcColumnDialog({
  open,
  editing,
  initialLabel,
  initialExpression,
  columns,
  datasetId,
  allowAggregates,
  onClose,
  onSave,
}: Props) {
  const { lang, t } = useLocale()
  const areaRef = useRef<HTMLTextAreaElement>(null)
  const [label, setLabel] = useState(initialLabel)
  const [expression, setExpression] = useState(initialExpression)
  const [query, setQuery] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [checking, setChecking] = useState(false)

  useEffect(() => {
    if (!open) {
      return
    }
    setLabel(initialLabel)
    setExpression(initialExpression)
    setQuery('')
    setError(null)
    setChecking(false)
  }, [open, initialLabel, initialExpression])

  useEffect(() => {
    if (!open) {
      return
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase()
    return columns.filter((column) => {
      if (!needle) {
        return true
      }
      const name = columnLabel(lang, column).toLowerCase()
      return name.includes(needle) || column.columnName.toLowerCase().includes(needle)
    })
  }, [columns, lang, query])

  const examples = useMemo(() => {
    const numbers = columns.filter((column) => isNumeric(column.sqlType))
    const dates = columns.filter((column) => isDate(column.sqlType))
    const texts = columns.filter(
      (column) => !isNumeric(column.sqlType) && !isDate(column.sqlType),
    )
    const items: { key: string; title: string; name: string; expression: string }[] = []
    if (numbers.length >= 2) {
      items.push({
        key: 'diff',
        title: t('calcExDiff'),
        name: t('calcExDiff'),
        expression: `${bracket(numbers[0].columnName)} - ${bracket(numbers[1].columnName)}`,
      })
    }
    if (numbers[0]) {
      items.push({
        key: 'round',
        title: t('calcExRound'),
        name: t('calcExRound'),
        expression: `ROUND(${bracket(numbers[0].columnName)}, 2)`,
      })
    }
    if (dates[0]) {
      items.push({
        key: 'year',
        title: t('calcExYear'),
        name: t('calcExYear'),
        expression: `YEAR(${bracket(dates[0].columnName)})`,
      })
    }
    if (texts.length >= 2) {
      items.push({
        key: 'concat',
        title: t('calcExConcat'),
        name: t('calcExConcat'),
        expression: `CONCAT(${bracket(texts[0].columnName)}, ' ', ${bracket(texts[1].columnName)})`,
      })
    }
    return items
  }, [columns, t])

  const place = (snippet: string) => {
    const area = areaRef.current
    const start = area?.selectionStart ?? expression.length
    const end = area?.selectionEnd ?? expression.length
    const { next, caret } = insertAt(expression, start, end, snippet)
    setExpression(next)
    setError(null)
    requestAnimationFrame(() => {
      area?.focus()
      area?.setSelectionRange(caret, caret)
    })
  }

  const save = async () => {
    const nextLabel = label.trim()
    const nextExpression = expression.trim()
    if (!nextLabel || !nextExpression || checking) {
      return
    }
    if (!datasetId) {
      setError(t('calcNeedDataset'))
      return
    }
    setChecking(true)
    setError(null)
    try {
      await api.validateExpression({
        datasetId,
        expression: nextExpression,
        allowAggregates,
      })
      onSave(nextLabel, nextExpression)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setChecking(false)
    }
  }

  if (!open) {
    return null
  }

  return createPortal(
    <div className="grants-overlay" role="presentation" onClick={onClose}>
      <div
        className="delete-report-dialog calc-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="calc-dialog-title"
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id="calc-dialog-title">{editing ? t('calcEditTitle') : t('calcAddTitle')}</h2>
        <p className="muted small">{t('calcExprHint')}</p>
        <div className="calc-dialog-body">
          <div className="calc-dialog-main">
            <label>
              {t('calcLabel')}
              <input
                value={label}
                autoFocus
                onChange={(event) => setLabel(event.target.value)}
              />
            </label>
            <label>
              {t('calcExpression')}
              <textarea
                ref={areaRef}
                value={expression}
                placeholder={t('calcExpressionPlaceholder')}
                onChange={(event) => {
                  setExpression(event.target.value)
                  setError(null)
                }}
              />
            </label>
            {examples.length > 0 && (
              <div className="calc-tool">
                <span className="calc-tool-title">{t('calcExamples')}</span>
                <div className="calc-chip-row">
                  {examples.map((example) => (
                    <button
                      key={example.key}
                      type="button"
                      className="calc-insert"
                      title={example.expression}
                      onClick={() => {
                        setExpression(example.expression)
                        setError(null)
                        if (!label.trim()) {
                          setLabel(example.name)
                        }
                      }}
                    >
                      {example.title}
                    </button>
                  ))}
                </div>
              </div>
            )}
            {error && (
              <p className="calc-expr-error" role="alert">
                {error}
              </p>
            )}
          </div>
          <aside className="calc-tools">
            <div className="calc-tool">
              <span className="calc-tool-title">{t('calcInsertColumn')}</span>
              <input
                value={query}
                placeholder={t('calcSearchColumns')}
                onChange={(event) => setQuery(event.target.value)}
              />
              <div className="calc-column-list">
                {filtered.map((column) => (
                  <button
                    key={column.id || column.columnName}
                    type="button"
                    className="calc-column"
                    onClick={() => place(bracket(column.columnName))}
                  >
                    <span>{columnLabel(lang, column)}</span>
                    <span className="muted">{column.sqlType}</span>
                  </button>
                ))}
              </div>
            </div>
            <div className="calc-tool">
              <span className="calc-tool-title">{t('calcInsertFunction')}</span>
              {FUNCTIONS.map((group) => (
                <div key={group.title} className="calc-fn-group">
                  <span className="calc-fn-label">{t(group.title)}</span>
                  <div className="calc-chip-row">
                    {group.items.map((item) => (
                      <button
                        key={item.name}
                        type="button"
                        className="calc-insert"
                        title={item.template.replace('|', '')}
                        onClick={() => place(item.template)}
                      >
                        {item.name}
                      </button>
                    ))}
                  </div>
                </div>
              ))}
              {allowAggregates && (
                <div className="calc-fn-group">
                  <span className="calc-fn-label">{t('calcFnAggregate')}</span>
                  <div className="calc-chip-row">
                    {AGGREGATES.map((item) => (
                      <button
                        key={item.name}
                        type="button"
                        className="calc-insert"
                        title={item.template.replace('|', '')}
                        onClick={() => place(item.template)}
                      >
                        {item.name}
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </aside>
        </div>
        <div className="delete-report-dialog-foot">
          <button type="button" className="btn" onClick={onClose} disabled={checking}>
            {t('cancel')}
          </button>
          <button
            type="button"
            className="btn primary"
            disabled={!label.trim() || !expression.trim() || checking}
            onClick={() => void save()}
          >
            {checking ? t('calcChecking') : editing ? t('save') : t('calcSave')}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
