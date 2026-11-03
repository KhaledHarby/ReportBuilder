import { useEffect } from 'react'
import type { ReportSummary } from '../api/types'
import { useLocale } from '../i18n/LocaleProvider'

type Props = {
  report: ReportSummary | null
  pending?: boolean
  error?: string | null
  onClose: () => void
  onConfirm: () => void
}

export function DeleteReportDialog({
  report,
  pending = false,
  error = null,
  onClose,
  onConfirm,
}: Props) {
  const { t } = useLocale()

  useEffect(() => {
    if (!report) {
      return
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !pending) {
        onClose()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [report, pending, onClose])

  if (!report) {
    return null
  }

  return (
    <div className="grants-overlay" onClick={pending ? undefined : onClose} role="presentation">
      <div
        className="delete-report-dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="delete-report-title"
        aria-describedby="delete-report-desc"
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id="delete-report-title">{t('deleteReportTitle')}</h2>
        <p id="delete-report-desc" className="muted">
          {t('deleteReportSimple')}
        </p>

        {error ? <div className="banner error">{error}</div> : null}

        <footer className="delete-report-dialog-foot">
          <button type="button" className="btn" disabled={pending} onClick={onClose}>
            {t('cancel')}
          </button>
          <button
            type="button"
            className="btn danger"
            disabled={pending}
            onClick={onConfirm}
          >
            {pending ? t('deleting') : t('deleteReportConfirm')}
          </button>
        </footer>
      </div>
    </div>
  )
}
