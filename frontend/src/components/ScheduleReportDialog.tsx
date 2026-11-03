import { useEffect } from 'react'
import type { ReportSummary } from '../api/types'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'
import { ReportScheduleMail } from './ReportScheduleMail'

type Props = {
  report: ReportSummary | null
  onClose: () => void
}

export function ScheduleReportDialog({ report, onClose }: Props) {
  const { lang, t } = useLocale()

  useEffect(() => {
    if (!report) {
      return
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [report, onClose])

  if (!report) {
    return null
  }

  const title = localizedText(lang, report.name, report.nameArabic)

  return (
    <div className="grants-overlay" onClick={onClose} role="presentation">
      <div
        className="schedule-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="schedule-report-title"
        onClick={(event) => event.stopPropagation()}
      >
        <header className="schedule-dialog-head">
          <div>
            <h2 id="schedule-report-title">{t('scheduleTitle')}</h2>
            <p className="muted small">{title}</p>
          </div>
          <button type="button" className="btn" onClick={onClose}>
            {t('close')}
          </button>
        </header>
        <ReportScheduleMail reportId={report.id} />
      </div>
    </div>
  )
}
