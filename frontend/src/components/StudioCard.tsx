import { Link } from 'react-router-dom'
import type { ExportFormat, ReportAccessMode, ReportSummary } from '../api/types'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'
import { bilingualExportName } from '../lib/download'
import { relativeTime } from '../lib/format'
import { ExportMenu } from './ExportMenu'
import { IconCopy, IconEdit, IconEye, IconEyeOff, IconLive, IconMail, IconSpark, IconTrash } from './Icons'

export type StudioView = 'card' | 'list'

function accessPhrase(
  mode: ReportAccessMode | undefined,
  restricted: boolean,
  t: (key: 'everyone' | 'selectedPeople' | 'selectedRole') => string,
) {
  if (mode === 'Roles') {
    return t('selectedRole')
  }
  if (mode === 'People' || restricted) {
    return t('selectedPeople')
  }
  return t('everyone')
}

type Props = {
  report: ReportSummary
  variant?: StudioView
  downloading?: boolean
  canExport?: boolean
  onExport: (format: ExportFormat, file?: FileSystemFileHandle) => void
  onDelete: () => void
  onDuplicate?: () => void
  duplicating?: boolean
  onPublish?: () => void
  onUnpublish?: () => void
  onSchedule?: () => void
}

export function StudioCard({
  report,
  variant = 'card',
  downloading,
  canExport = true,
  onExport,
  onDelete,
  onDuplicate,
  duplicating,
  onPublish,
  onUnpublish,
  onSchedule,
}: Props) {
  const { lang, t } = useLocale()
  const isDraft = report.status === 'draft'
  const canEdit = report.canEdit
  const openTo = isDraft && canEdit
    ? `/reports/${report.id}`
    : `/reports/${report.id}/view`
  const title = localizedText(lang, report.name, report.nameArabic)
  const datasetTitle = localizedText(lang, report.datasetName, report.datasetNameArabic)
  const copy = localizedText(lang, report.description, report.descriptionArabic)
  const isList = variant === 'list'

  const flags = (
    <span className="studio-card-flags">
      <span className={`badge ${isDraft ? 'draft' : 'published'}`}>
        {isDraft ? t('draft') : t('published')}
      </span>
      {report.hasDraft && !isDraft ? (
        <span className="badge draft">{t('hasUnpublishedDraft')}</span>
      ) : null}
      {report.isAdminCreated ? (
        <span className="badge admin">{t('admin')}</span>
      ) : (
        <span className="badge">
          {report.createdByName
            ? t('byUser', { name: report.createdByName })
            : t('user')}
        </span>
      )}
      {report.kind === 'chart' ? (
        <span className="badge">{t('chartBadge')}</span>
      ) : null}
    </span>
  )

  const openLabel = isDraft && canEdit ? t('continue') : t('open')

  const actions = (
    <div className="studio-card-actions">
      <Link className="btn sm primary" to={openTo} title={openLabel} aria-label={openLabel}>
        {isDraft && canEdit ? <IconSpark /> : <IconEye />}
        <span className="btn-text">{openLabel}</span>
      </Link>
      {canEdit && !isDraft && (
        <Link
          className="btn sm"
          to={`/reports/${report.id}`}
          title={t('edit')}
          aria-label={t('edit')}
        >
          <IconEdit />
          <span className="btn-text">{t('edit')}</span>
        </Link>
      )}
      {canEdit && isDraft && onPublish && (
        <button
          type="button"
          className="btn sm"
          title={t('publish')}
          aria-label={t('publish')}
          onClick={onPublish}
        >
          <IconLive />
          <span className="btn-text">{t('publish')}</span>
        </button>
      )}
      {canEdit && !isDraft && onUnpublish && (
        <button
          type="button"
          className="btn sm"
          title={t('unpublish')}
          aria-label={t('unpublish')}
          onClick={onUnpublish}
        >
          <IconEyeOff />
          <span className="btn-text">{t('unpublish')}</span>
        </button>
      )}
      {report.kind !== 'chart' && onSchedule && (
        <button
          type="button"
          className="btn sm"
          title={t('scheduleTitle')}
          aria-label={t('scheduleTitle')}
          onClick={onSchedule}
        >
          <IconMail />
          <span className="btn-text">{t('scheduleTitle')}</span>
        </button>
      )}
      {canExport && !isDraft && (
        <ExportMenu
          compact
          fileName={bilingualExportName(report.name, report.nameArabic)}
          disabled={downloading}
          pending={downloading}
          onExport={onExport}
        />
      )}
      {onDuplicate && (
        <button
          type="button"
          className="btn sm"
          title={t('duplicateReport')}
          aria-label={t('duplicateReport')}
          disabled={duplicating}
          onClick={onDuplicate}
        >
          <IconCopy />
          <span className="btn-text">{duplicating ? t('duplicating') : t('duplicateReport')}</span>
        </button>
      )}
      {report.canDelete && (
        <button
          type="button"
          className="btn sm icon danger"
          aria-label={t('deleteNamed', { name: title })}
          title={t('delete')}
          onClick={() => onDelete()}
        >
          <IconTrash />
        </button>
      )}
    </div>
  )

  if (isList) {
    return (
      <article className={`studio-list-row ${isDraft ? 'draft' : 'published'}`}>
        <div className="studio-list-copy">
          <h2>
            <Link to={openTo}>{title}</Link>
          </h2>
          <p>
            {copy || (isDraft ? t('draftLayout') : t('liveLayout'))}
          </p>
        </div>
        <div className="studio-list-meta">
          <span className="badge">{datasetTitle}</span>
          {flags}
          <span className="muted small">
            {accessPhrase(report.accessMode, report.isRestricted, t)} ·{' '}
            {t('updated', { time: relativeTime(report.updatedAt, lang) })}
          </span>
        </div>
        {actions}
      </article>
    )
  }

  return (
    <article className={`studio-card ${isDraft ? 'draft' : 'published'}`}>
      <div className="studio-card-top">
        <span className="badge">{datasetTitle}</span>
        {flags}
      </div>
      <h2>
        <Link to={openTo}>{title}</Link>
      </h2>
      <p className="studio-card-copy">
        {copy || (isDraft ? t('draftLayout') : t('liveLayout'))}
      </p>
      <footer className="studio-card-foot">
        <span className="muted small">
          {accessPhrase(report.accessMode, report.isRestricted, t)} ·{' '}
          {t('updated', { time: relativeTime(report.updatedAt, lang) })}
        </span>
        {actions}
      </footer>
    </article>
  )
}
