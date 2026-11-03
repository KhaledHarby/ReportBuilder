import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { DatasetColumn, LayoutFilter } from '../api/types'
import type { Lang } from '../i18n/locale'
import { useLocale } from '../i18n/LocaleProvider'
import { dependableParents, filterDisplayLabel } from '../lib/filters'
import { IconSliders } from './Icons'

export function FilterOptionsButton({
  filter,
  allFilters,
  lang,
  palette,
  onChange,
}: {
  filter: LayoutFilter
  allFilters: LayoutFilter[]
  lang: Lang
  palette: DatasetColumn[]
  onChange: (filter: LayoutFilter) => void
}) {
  const { t } = useLocale()
  const [open, setOpen] = useState(false)
  const [position, setPosition] = useState({ top: 0, left: 0 })
  const buttonRef = useRef<HTMLButtonElement>(null)
  const popRef = useRef<HTMLDivElement>(null)
  const titleId = useId()
  const active = [
    filter.isRequired ? t('filterRequired') : '',
    filter.isFixed ? t('fixed') : '',
    filter.isHidden ? t('hiddenFilter') : '',
    filter.dependsOn ? t('dependsOn') : '',
  ].filter(Boolean)

  useLayoutEffect(() => {
    if (!open) {
      return
    }
    const update = () => {
      const button = buttonRef.current
      const popover = popRef.current
      if (!button) {
        return
      }
      const rect = button.getBoundingClientRect()
      const width = popover?.offsetWidth ?? 320
      const height = popover?.offsetHeight ?? 280
      const rtl = document.documentElement.dir === 'rtl'
      let left = rtl ? rect.right - width : rect.left
      left = Math.max(8, Math.min(left, window.innerWidth - width - 8))
      let top = rect.bottom + 6
      if (top + height > window.innerHeight - 8) {
        top = Math.max(8, rect.top - height - 6)
      }
      setPosition({ top, left })
    }
    update()
    window.addEventListener('resize', update)
    window.addEventListener('scroll', update, true)
    return () => {
      window.removeEventListener('resize', update)
      window.removeEventListener('scroll', update, true)
    }
  }, [open])

  useEffect(() => {
    if (!open) {
      return
    }
    const onPointer = (event: PointerEvent) => {
      const target = event.target as Node
      if (buttonRef.current?.contains(target) || popRef.current?.contains(target)) {
        return
      }
      setOpen(false)
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
    <>
      <button
        ref={buttonRef}
        type="button"
        className={`filter-options-btn${active.length > 0 ? ' is-set' : ''}`}
        aria-expanded={open}
        aria-haspopup="dialog"
        title={active.length > 0 ? active.join(', ') : t('filterOptions')}
        aria-label={t('filterOptions')}
        onClick={() => setOpen((current) => !current)}
      >
        <IconSliders />
        {active.length > 0 && <span className="filter-options-count">{active.length}</span>}
      </button>
      {open &&
        createPortal(
          <div
            ref={popRef}
            className="filter-options-popover"
            role="dialog"
            aria-labelledby={titleId}
            style={{ top: position.top, left: position.left }}
          >
            <div className="filter-options-head">
              <strong id={titleId}>{t('filterOptionsTitle')}</strong>
              <button
                type="button"
                className="filter-options-close"
                aria-label={t('close')}
                onClick={() => setOpen(false)}
              >
                <span aria-hidden="true">×</span>
              </button>
            </div>
            <label className={`filter-option${filter.isRequired ? ' is-on' : ''}`}>
              <input
                type="checkbox"
                checked={Boolean(filter.isRequired)}
                onChange={(event) =>
                  onChange({
                    ...filter,
                    isRequired: event.target.checked || undefined,
                    isHidden: event.target.checked ? false : filter.isHidden,
                  })
                }
              />
              <span>
                <strong>{t('filterRequired')}</strong>
                <small>{t('filterRequiredHint')}</small>
              </span>
            </label>
            <label className={`filter-option${filter.isFixed ? ' is-on' : ''}`}>
              <input
                type="checkbox"
                checked={Boolean(filter.isFixed)}
                onChange={(event) => onChange({ ...filter, isFixed: event.target.checked })}
              />
              <span>
                <strong>{t('fixed')}</strong>
                <small>{t('fixedHint')}</small>
              </span>
            </label>
            <label className={`filter-option${filter.isHidden ? ' is-on' : ''}`}>
              <input
                type="checkbox"
                checked={Boolean(filter.isHidden)}
                onChange={(event) =>
                  onChange({
                    ...filter,
                    isHidden: event.target.checked || undefined,
                    isRequired: event.target.checked ? undefined : filter.isRequired,
                  })
                }
              />
              <span>
                <strong>{t('hiddenFilter')}</strong>
                <small>{t('hiddenFilterHint')}</small>
              </span>
            </label>
            <label className="filter-option-depends">
              <span>{t('dependsOn')}</span>
              <select
                value={filter.dependsOn ?? ''}
                aria-label={t('dependsOn')}
                onChange={(event) =>
                  onChange({
                    ...filter,
                    dependsOn: event.target.value || null,
                  })
                }
              >
                <option value="">{t('dependsOnNone')}</option>
                {dependableParents(allFilters, filter.field).map((item) => (
                  <option key={item.field} value={item.field}>
                    {filterDisplayLabel(
                      lang,
                      item,
                      palette.find((column) => column.columnName === item.field),
                    )}
                  </option>
                ))}
              </select>
              <small>{t('dependsOnHint')}</small>
            </label>
          </div>,
          document.body,
        )}
    </>
  )
}
