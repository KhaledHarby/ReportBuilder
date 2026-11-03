import { useEffect, useRef, useState } from 'react'
import { EXPORT_FORMATS, type ExportFormat } from '../api/types'
import { exportFileName, pickCsvFile } from '../lib/download'
import { useLocale } from '../i18n/LocaleProvider'
import { IconDownload } from './Icons'

type Props = {
  disabled?: boolean
  pending?: boolean
  primary?: boolean
  compact?: boolean
  label?: string
  fileName?: string
  onExport: (format: ExportFormat, file?: FileSystemFileHandle) => void
}

export function ExportMenu({
  disabled,
  pending,
  primary,
  compact,
  label,
  fileName,
  onExport,
}: Props) {
  const { t } = useLocale()
  const resolvedLabel = label ?? t('download')
  const rootRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)

  useEffect(() => {
    if (!open) {
      return
    }
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false)
      }
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  return (
    <div className={`export-menu ${open ? 'open' : ''}`} ref={rootRef}>
      <button
        type="button"
        className={`btn ${primary ? 'primary' : ''} ${compact ? 'icon' : ''}`}
        disabled={disabled || pending}
        aria-expanded={open}
        aria-haspopup="menu"
        aria-label={compact ? (pending ? t('downloadingShort') : resolvedLabel) : undefined}
        title={compact ? resolvedLabel : undefined}
        onClick={() => setOpen((value) => !value)}
      >
        <IconDownload />
        {compact ? null : pending ? t('downloading') : resolvedLabel}
        {compact ? null : (
          <span className="export-caret" aria-hidden>
            ▾
          </span>
        )}
      </button>
      {open && (
        <ul className="export-menu-list" role="menu">
          {EXPORT_FORMATS.map((option) => (
            <li key={option.id} role="none">
              <button
                type="button"
                role="menuitem"
                className="export-menu-item"
                disabled={disabled || pending}
                onClick={() => {
                  setOpen(false)
                  if (option.id !== 'csv') {
                    onExport(option.id)
                    return
                  }
                  void pickCsvFile(exportFileName(fileName, 'csv'))
                    .then((file) => onExport('csv', file))
                    .catch((error: unknown) => {
                      if (error instanceof DOMException && error.name === 'AbortError') {
                        return
                      }
                      onExport('csv')
                    })
                }}
              >
                <b>
                  {option.id === 'csv'
                    ? t('exportCsv')
                    : option.id === 'xlsx'
                      ? t('exportXlsx')
                      : option.id === 'pdf'
                        ? t('exportPdf')
                        : t('exportDocx')}
                </b>
                <span>
                  {option.id === 'csv'
                    ? t('exportCsvHint')
                    : option.id === 'xlsx'
                      ? t('exportXlsxHint')
                      : option.id === 'pdf'
                        ? t('exportPdfHint')
                        : t('exportDocxHint')}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
