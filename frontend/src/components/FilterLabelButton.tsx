import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { useLocale } from '../i18n/LocaleProvider'
import { Field } from './AdminFields'

export function FilterLabelButton({
  fieldLabel,
  alias,
  aliasArabic,
  onChange,
}: {
  fieldLabel: string
  alias?: string | null
  aliasArabic?: string | null
  onChange: (next: { alias: string; aliasArabic: string }) => void
}) {
  const { t } = useLocale()
  const [open, setOpen] = useState(false)
  const hasLabel = Boolean(alias?.trim() || aliasArabic?.trim())

  useEffect(() => {
    if (!open) {
      return
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open])

  return (
    <>
      <button
        type="button"
        className={`filter-label-btn${hasLabel ? ' is-set' : ''}`}
        title={hasLabel ? t('filterLabelsHint') : t('filterLabels')}
        aria-label={t('filterLabels')}
        aria-expanded={open}
        onClick={() => setOpen(true)}
      >
        {t('filterLabels')}
      </button>
      {open &&
        createPortal(
          <div className="grants-overlay" onClick={() => setOpen(false)} role="presentation">
            <div
              className="grants-dialog filter-label-dialog"
              role="dialog"
              aria-modal="true"
              aria-labelledby="filter-label-title"
              onClick={(event) => event.stopPropagation()}
            >
              <header className="grants-chrome">
                <div className="grants-head">
                  <div>
                    <div className="kicker">{fieldLabel}</div>
                    <h2 id="filter-label-title">{t('filterLabelsTitle')}</h2>
                    <p className="muted small">{t('filterLabelsHint')}</p>
                  </div>
                  <button
                    type="button"
                    className="icon-btn"
                    aria-label={t('close')}
                    onClick={() => setOpen(false)}
                  >
                    <span aria-hidden="true">×</span>
                  </button>
                </div>
              </header>
              <div className="password-dialog-body">
                <Field label={t('filterAliasPlaceholder')}>
                  <input
                    value={alias ?? ''}
                    onChange={(event) =>
                      onChange({ alias: event.target.value, aliasArabic: aliasArabic ?? '' })
                    }
                  />
                </Field>
                <Field label={t('filterAliasArabicPlaceholder')}>
                  <input
                    dir="rtl"
                    lang="ar"
                    value={aliasArabic ?? ''}
                    onChange={(event) =>
                      onChange({ alias: alias ?? '', aliasArabic: event.target.value })
                    }
                  />
                </Field>
              </div>
              <footer className="password-dialog-foot">
                <button type="button" className="btn primary" onClick={() => setOpen(false)}>
                  {t('done')}
                </button>
              </footer>
            </div>
          </div>,
          document.body,
        )}
    </>
  )
}
