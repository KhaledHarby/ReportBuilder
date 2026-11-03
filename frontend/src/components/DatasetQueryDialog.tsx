import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { Dataset } from '../api/types'
import { useLocale } from '../i18n/LocaleProvider'

type Props = {
  open: boolean
  onClose: () => void
  onCreated?: (id: string) => void
}

export function DatasetQueryDialog({ open, onClose, onCreated }: Props) {
  const { t } = useLocale()
  const queryClient = useQueryClient()
  const [displayName, setDisplayName] = useState('')
  const [displayNameArabic, setDisplayNameArabic] = useState('')
  const [sqlText, setSqlText] = useState('')
  const [sqlQuery, setSqlQuery] = useState('')
  const [saveError, setSaveError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) {
      return
    }
    setDisplayName('')
    setDisplayNameArabic('')
    setSqlText('')
    setSqlQuery('')
    setSaveError(null)
  }, [open])

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

  useEffect(() => {
    const handle = window.setTimeout(() => setSqlQuery(sqlText.trim()), 400)
    return () => window.clearTimeout(handle)
  }, [sqlText])

  const preview = useQuery({
    queryKey: ['dataset-query-preview', sqlQuery],
    queryFn: () => api.previewDatasetQuery(sqlQuery),
    enabled: open && sqlQuery.length > 0,
    retry: false,
  })

  const save = useMutation({
    mutationFn: api.createDatasetQuery,
    onSuccess: (dataset) => {
      queryClient.setQueryData<Dataset[]>(['datasets'], (current) =>
        current ? [dataset, ...current.filter((item) => item.id !== dataset.id)] : current,
      )
      void queryClient.invalidateQueries({ queryKey: ['datasets'] })
      onCreated?.(dataset.id)
      onClose()
    },
    onError: (error) => setSaveError((error as Error).message),
  })

  if (!open) {
    return null
  }

  const previewMatches = sqlText.trim() === sqlQuery
  const previewFailed = previewMatches && preview.isError
  const canSave =
    displayName.trim().length > 0 &&
    sqlText.trim().length > 0 &&
    !previewFailed &&
    !preview.isFetching &&
    !save.isPending

  return (
    <div className="grants-overlay" onClick={onClose} role="presentation">
      <div
        className="schedule-dialog dataset-query-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="dataset-query-title"
        onClick={(event) => event.stopPropagation()}
      >
        <header className="schedule-dialog-head">
          <div>
            <h2 id="dataset-query-title">{t('datasetQueryTitle')}</h2>
            <p>{t('datasetQueryHint')}</p>
          </div>
          <button type="button" className="icon-btn" aria-label={t('close')} onClick={onClose}>
            <span aria-hidden="true">×</span>
          </button>
        </header>

        <label>
          {t('displayName')}
          <input
            value={displayName}
            maxLength={256}
            required
            onChange={(event) => setDisplayName(event.target.value)}
          />
        </label>
        <label>
          {t('displayNameArabic')}
          <input
            value={displayNameArabic}
            maxLength={256}
            onChange={(event) => setDisplayNameArabic(event.target.value)}
          />
        </label>
        <label>
          SQL
          <textarea
            className="lookup-sql"
            rows={8}
            value={sqlText}
            spellCheck={false}
            onChange={(event) => {
              setSqlText(event.target.value)
              setSaveError(null)
            }}
          />
        </label>

        {saveError ? <div className="banner error">{saveError}</div> : null}
        {previewFailed ? (
          <div className="banner error">{(preview.error as Error).message}</div>
        ) : null}

        {preview.data && previewMatches && !preview.isError ? (
          <div>
            <div className="muted small">{t('datasetQueryColumns')}</div>
            <table className="lookup-column-table">
              <tbody>
                {preview.data.map((column) => (
                  <tr key={column.columnName}>
                    <td>{column.columnName}</td>
                    <td className="muted">{column.sqlType}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}

        <div className="header-actions dataset-query-actions">
          <button type="button" className="btn" disabled={save.isPending} onClick={onClose}>
            {t('close')}
          </button>
          <button
            type="button"
            className="btn primary"
            disabled={!canSave}
            onClick={() =>
              save.mutate({
                displayName: displayName.trim(),
                displayNameArabic: displayNameArabic.trim() || undefined,
                sqlText: sqlText.trim(),
              })
            }
          >
            {save.isPending ? t('datasetQuerySaving') : t('datasetQuerySave')}
          </button>
        </div>
      </div>
    </div>
  )
}
