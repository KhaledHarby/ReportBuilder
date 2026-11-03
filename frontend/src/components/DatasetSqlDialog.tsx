import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { useLocale } from '../i18n/LocaleProvider'
import { Spinner } from './Spinner'

type Props = {
  datasetId: string | null
  title: string
  objectName: string
  canModify?: boolean
  query?: boolean
  onClose: () => void
}

export function DatasetSqlDialog({
  datasetId,
  title,
  objectName,
  canModify = false,
  query = false,
  onClose,
}: Props) {
  const { t } = useLocale()
  const queryClient = useQueryClient()
  const [copied, setCopied] = useState(false)
  const [draft, setDraft] = useState('')
  const [applyError, setApplyError] = useState<string | null>(null)
  const [applyOk, setApplyOk] = useState(false)

  const script = useQuery({
    queryKey: ['dataset-sql', datasetId],
    queryFn: () => api.getDatasetSql(datasetId!),
    enabled: Boolean(datasetId),
  })

  useEffect(() => {
    if (!datasetId) {
      return
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [datasetId, onClose])

  useEffect(() => {
    setCopied(false)
    setApplyError(null)
    setApplyOk(false)
    setDraft('')
  }, [datasetId])

  useEffect(() => {
    if (script.data?.script != null) {
      setDraft(script.data.script)
    }
  }, [script.data?.script])

  const apply = useMutation({
    mutationFn: (sql: string) => api.applyDatasetSql(datasetId!, sql),
    onSuccess: (result) => {
      setDraft(result.script)
      setApplyError(null)
      setApplyOk(true)
      queryClient.setQueryData(['dataset-sql', datasetId], result)
      queryClient.invalidateQueries({ queryKey: ['datasets'] })
      window.setTimeout(() => setApplyOk(false), 2500)
    },
    onError: (error) => {
      setApplyOk(false)
      setApplyError((error as Error).message)
    },
  })

  if (!datasetId) {
    return null
  }

  const canApply = canModify && draft.trim().length > 0 && !apply.isPending

  async function copyScript() {
    const text = canModify ? draft : script.data?.script
    if (!text) {
      return
    }
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1600)
    } catch {
      setCopied(false)
    }
  }

  return (
    <div className="grants-overlay" onClick={onClose} role="presentation">
      <div
        className="grants-dialog sql-script-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="dataset-sql-title"
        onClick={(event) => event.stopPropagation()}
      >
        <header className="grants-chrome">
          <div className="grants-head">
            <div>
              <div className="kicker">SQL</div>
              <h2 id="dataset-sql-title">{title}</h2>
              <p className="muted small">{objectName}</p>
            </div>
            <button type="button" className="icon-btn" aria-label={t('close')} onClick={onClose}>
              <span aria-hidden="true">×</span>
            </button>
          </div>
        </header>

        <div className="sql-script-body">
          {script.isLoading ? (
            <Spinner page label={t('loadingApp')} />
          ) : script.isError ? (
            <div className="banner error">{(script.error as Error).message}</div>
          ) : (
            <>
              {canModify ? (
                <p className="muted small sql-script-hint">
                  {query ? t('datasetQueryHint') : t('sqlModifyHint')}
                </p>
              ) : null}
              {applyError ? <div className="banner error">{applyError}</div> : null}
              {applyOk ? (
                <div className="banner ok">{query ? t('datasetQuerySaved') : t('sqlApplied')}</div>
              ) : null}
              {canModify ? (
                <textarea
                  className="sql-script-editor"
                  value={draft}
                  spellCheck={false}
                  aria-label={t('viewSql')}
                  onChange={(event) => {
                    setDraft(event.target.value)
                    setApplyOk(false)
                    setApplyError(null)
                  }}
                />
              ) : (
                <pre className="sql-script-code">{script.data?.script}</pre>
              )}
            </>
          )}
        </div>

        <footer className="sql-script-foot">
          <button
            type="button"
            className="btn"
            disabled={!(canModify ? draft : script.data?.script)}
            onClick={() => void copyScript()}
          >
            {copied ? t('sqlCopied') : t('copySql')}
          </button>
          {canModify ? (
            <button
              type="button"
              className="btn primary"
              disabled={!canApply}
              onClick={() => {
                if (
                  !confirm(
                    t(query ? 'datasetQuerySaveConfirm' : 'sqlApplyConfirm', {
                      name: query ? title : objectName,
                    }),
                  )
                ) {
                  return
                }
                apply.mutate(draft)
              }}
            >
              {apply.isPending
                ? query
                  ? t('datasetQuerySaving')
                  : t('sqlApplying')
                : query
                  ? t('datasetQuerySave')
                  : t('applySql')}
            </button>
          ) : (
            <button type="button" className="btn primary" onClick={onClose}>
              {t('close')}
            </button>
          )}
          {canModify ? (
            <button type="button" className="btn" disabled={apply.isPending} onClick={onClose}>
              {t('close')}
            </button>
          ) : null}
        </footer>
      </div>
    </div>
  )
}
