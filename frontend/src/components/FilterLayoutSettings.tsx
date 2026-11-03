import { useEffect, useId, useRef, useState } from 'react'
import {
  filtersPerRow,
  MAX_FILTERS_PER_ROW,
  MIN_FILTERS_PER_ROW,
} from '../lib/filters'
import { useLocale } from '../i18n/LocaleProvider'
import { IconSettings } from './Icons'

export function FilterLayoutSettings({
  value,
  onChange,
}: {
  value?: number | null
  onChange: (columns: number) => void
}) {
  const { t } = useLocale()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const inputId = useId()
  const columns = filtersPerRow(value)

  useEffect(() => {
    if (!open) {
      return
    }
    const onPointer = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false)
      }
    }
    document.addEventListener('pointerdown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('pointerdown', onPointer)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  return (
    <div className="filter-layout-settings" ref={rootRef}>
      <button
        type="button"
        className="btn icon filter-layout-button"
        aria-expanded={open}
        aria-controls={open ? inputId : undefined}
        title={t('filterLayoutColumns')}
        aria-label={t('filterLayoutColumns')}
        onClick={() => setOpen((current) => !current)}
      >
        <IconSettings />
      </button>
      {open && (
        <div className="filter-layout-popover" role="dialog" aria-label={t('filterLayoutColumns')}>
          <label htmlFor={inputId}>
            {t('filterLayoutColumns')}
            <input
              id={inputId}
              type="number"
              min={MIN_FILTERS_PER_ROW}
              max={MAX_FILTERS_PER_ROW}
              inputMode="numeric"
              value={columns}
              onChange={(event) => {
                const next = Number.parseInt(event.target.value, 10)
                if (!Number.isFinite(next)) {
                  return
                }
                onChange(
                  Math.min(MAX_FILTERS_PER_ROW, Math.max(MIN_FILTERS_PER_ROW, next)),
                )
              }}
            />
          </label>
          <p className="muted small">{t('filterLayoutColumnsHint', { n: columns })}</p>
        </div>
      )}
    </div>
  )
}
