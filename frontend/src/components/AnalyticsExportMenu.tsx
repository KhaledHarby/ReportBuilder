import { useEffect, useRef, useState } from 'react'
import { useLocale } from '../i18n/LocaleProvider'

export type AnalyticsExportKind = 'csv' | 'pdf' | 'pptx'

type Props = {
  pending?: boolean
  onExport: (kind: AnalyticsExportKind) => void
}

export function AnalyticsExportMenu({ pending, onExport }: Props) {
  const { t } = useLocale()
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

  const options: { id: AnalyticsExportKind; title: string; hint: string }[] = [
    { id: 'pdf', title: t('analyticsExportPdf'), hint: t('analyticsExportPdfHint') },
    { id: 'pptx', title: t('analyticsExportPptx'), hint: t('analyticsExportPptxHint') },
    { id: 'csv', title: t('analyticsExportCsv'), hint: t('analyticsExportCsvHint') },
  ]

  return (
    <div className={`export-menu analytics-export-menu${open ? ' open' : ''}`} ref={rootRef}>
      <button
        type="button"
        className="analytics-btn ghost"
        disabled={pending}
        aria-expanded={open}
        aria-haspopup="menu"
        onClick={() => setOpen((value) => !value)}
      >
        {pending ? t('exporting') : t('analyticsExportMenu')}
        <span className="export-caret" aria-hidden>
          ▾
        </span>
      </button>
      {open && (
        <ul className="export-menu-list" role="menu">
          {options.map((option) => (
            <li key={option.id} role="none">
              <button
                type="button"
                role="menuitem"
                className="export-menu-item"
                disabled={pending}
                onClick={() => {
                  setOpen(false)
                  onExport(option.id)
                }}
              >
                <b>{option.title}</b>
                <span>{option.hint}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
