import { useEffect, useState } from 'react'
import type { LayoutFilter } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { useLocale } from '../i18n/LocaleProvider'
import { buildEmbedUrl, type EmbedParameter } from '../lib/embedUrl'

type Props = {
  reportId: string
  filters: LayoutFilter[]
  onClose: () => void
}

export function EmbedUrlDialog({ reportId, filters, onClose }: Props) {
  const { user } = useAuth()
  const { lang, t } = useLocale()
  const details = buildEmbedUrl(reportId, filters, lang, user)
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  async function copyUrl() {
    try {
      await navigator.clipboard.writeText(details.url)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1600)
    } catch {
      setCopied(false)
    }
  }

  return (
    <div className="grants-overlay" onClick={onClose} role="presentation">
      <div
        className="schedule-dialog embed-url-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="embed-url-title"
        onClick={(event) => event.stopPropagation()}
      >
        <header className="schedule-dialog-head">
          <div>
            <h2 id="embed-url-title">{t('embedUrlTitle')}</h2>
            <p className="muted small">{t('embedUrlHint')}</p>
          </div>
          <button type="button" className="btn" onClick={onClose}>
            {t('close')}
          </button>
        </header>
        <p className="embed-url-box">{details.url.replace(/([?&])/g, '$1\u200b')}</p>
        <p className="muted small">{t('embedCodeNote')}</p>
        <ParameterTable title={t('embedParameters')} rows={details.query} nameLabel={t('embedParameter')} valueLabel={t('embedParameterValue')} />
        <div>
          <div className="schedule-people-title">{t('embedLaunchParameters')}</div>
          <p className="muted small">{t('embedLaunchHint')}</p>
        </div>
        <ParameterTable title={t('embedLaunchParameters')} rows={details.launch} nameLabel={t('embedParameter')} valueLabel={t('embedParameterValue')} hideTitle />
        <div className="delete-report-dialog-foot">
          <button type="button" className="btn primary" onClick={() => void copyUrl()}>
            {copied ? t('embedCopied') : t('embedCopyUrl')}
          </button>
        </div>
      </div>
    </div>
  )
}

function ParameterTable({
  title,
  rows,
  nameLabel,
  valueLabel,
  hideTitle = false,
}: {
  title: string
  rows: EmbedParameter[]
  nameLabel: string
  valueLabel: string
  hideTitle?: boolean
}) {
  return (
    <div className="embed-param-block">
      {!hideTitle && <div className="schedule-people-title">{title}</div>}
      <table className="embed-param-table">
        <thead>
          <tr>
            <th>{nameLabel}</th>
            <th>{valueLabel}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={`${row.name}-${index}`}>
              <td>{row.name}</td>
              <td>{row.value || '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
