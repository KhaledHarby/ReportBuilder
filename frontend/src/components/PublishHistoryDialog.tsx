import { useEffect } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { ReportDetail, ReportLayoutDiff, ReportRevision } from '../api/types'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'

type Props = {
  reportId: string | null
  onClose: () => void
  onRestored: (report: ReportDetail, version: number) => void
}

export function PublishHistoryDialog({ reportId, onClose, onRestored }: Props) {
  const { lang, t } = useLocale()
  const history = useQuery({
    queryKey: ['report-history', reportId],
    queryFn: () => api.reportHistory(reportId!),
    enabled: Boolean(reportId),
  })
  const restore = useMutation({
    mutationFn: (version: number) => api.restoreReportRevision(reportId!, version),
    onSuccess: (report, version) => onRestored(report, version),
  })

  useEffect(() => {
    if (!reportId) {
      return
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [reportId, onClose])

  if (!reportId) {
    return null
  }

  return (
    <div className="grants-overlay" onClick={onClose} role="presentation">
      <div
        className="schedule-dialog history-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="publish-history-title"
        onClick={(event) => event.stopPropagation()}
      >
        <header className="schedule-dialog-head">
          <div>
            <h2 id="publish-history-title">{t('publishHistoryTitle')}</h2>
            <p className="muted small">{t('publishHistoryHint')}</p>
          </div>
          <button type="button" className="btn" onClick={onClose}>
            {t('close')}
          </button>
        </header>
        {history.isError && (
          <div className="banner error">{(history.error as Error).message}</div>
        )}
        {restore.isError && (
          <div className="banner error">{(restore.error as Error).message}</div>
        )}
        {history.data?.draftVersusPublished && (
          <section className="history-draft">
            <div className="history-draft-head">
              <h3>{t('publishHistoryDraft')}</h3>
              <button
                type="button"
                className="btn"
                disabled={restore.isPending || history.data.currentVersion <= 0}
                onClick={() => restore.mutate(history.data.currentVersion)}
              >
                {restore.isPending && restore.variables === history.data.currentVersion
                  ? t('publishHistoryRestoring')
                  : t('publishHistoryUsePublished')}
              </button>
            </div>
            <DiffLines diff={history.data.draftVersusPublished} draft t={t} />
          </section>
        )}
        {history.isSuccess && history.data.revisions.length === 0 && (
          <p className="muted">{t('publishHistoryEmpty')}</p>
        )}
        <ul className="history-list">
          {(history.data?.revisions ?? []).map((revision) => (
            <li key={revision.version} className="history-row">
              <div className="history-row-main">
                <div className="history-row-title">
                  <strong>{t('publishHistoryVersion', { version: revision.version })}</strong>
                  {revision.isCurrent && <span className="badge published">{t('publishHistoryCurrent')}</span>}
                  <span>{localizedText(lang, revision.name, revision.nameArabic)}</span>
                </div>
                <p className="muted small">
                  {formatWhen(revision.publishedAt, lang)}
                  {revision.publishedByName ? ` · ${revision.publishedByName}` : ''}
                </p>
                <DiffLines diff={revision} t={t} />
              </div>
              <button
                type="button"
                className="btn"
                disabled={restore.isPending || (revision.isCurrent && !history.data?.draftVersusPublished)}
                onClick={() => restore.mutate(revision.version)}
              >
                {restore.isPending && restore.variables === revision.version
                  ? t('publishHistoryRestoring')
                  : revision.isCurrent && history.data?.draftVersusPublished
                    ? t('publishHistoryUsePublished')
                    : t('publishHistoryRestore')}
              </button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}

function DiffLines({
  diff,
  draft,
  t,
}: {
  diff: ReportLayoutDiff | ReportRevision
  draft?: boolean
  t: (key: 'publishHistoryOnlyHere' | 'publishHistoryOnlyDraft' | 'publishHistoryOnlyPublished' | 'publishHistorySame' | 'publishHistoryFilters' | 'publishHistorySort' | 'publishHistoryName', vars?: Record<string, string | number>) => string
}) {
  const onlyHere = joinColumns(diff.onlyHere)
  const onlyPublished = joinColumns(diff.onlyInPublished)
  const same = !onlyHere && !onlyPublished && !diff.filtersChanged && !diff.sortChanged && !diff.nameChanged
  return (
    <div className="history-diff">
      {same && <p className="muted small">{t('publishHistorySame')}</p>}
      {onlyHere && (
        <p className="small">
          {t(draft ? 'publishHistoryOnlyDraft' : 'publishHistoryOnlyHere', { columns: onlyHere })}
        </p>
      )}
      {onlyPublished && <p className="small">{t('publishHistoryOnlyPublished', { columns: onlyPublished })}</p>}
      <div className="history-flags">
        {diff.nameChanged && <span className="badge draft">{t('publishHistoryName')}</span>}
        {diff.filtersChanged && <span className="badge draft">{t('publishHistoryFilters')}</span>}
        {diff.sortChanged && <span className="badge draft">{t('publishHistorySort')}</span>}
      </div>
    </div>
  )
}

function joinColumns(items: string[]) {
  if (items.length === 0) {
    return ''
  }
  if (items.length <= 8) {
    return items.join(' · ')
  }
  return `${items.slice(0, 8).join(' · ')} +${items.length - 8}`
}

function formatWhen(value: string, lang: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }
  return date.toLocaleString(lang === 'ar' ? 'ar-AE' : 'en-GB', {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
}
