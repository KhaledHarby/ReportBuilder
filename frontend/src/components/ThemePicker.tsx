import { useLocale } from '../i18n/LocaleProvider'
import { THEMES, type Theme, useTheme } from '../theme/ThemeProvider'

const LABELS: Record<
  Theme,
  'themeLight' | 'themeDark' | 'themeSand' | 'themePalm' | 'themeRuby' | 'themeEllen'
> = {
  light: 'themeLight',
  dark: 'themeDark',
  sand: 'themeSand',
  palm: 'themePalm',
  ruby: 'themeRuby',
  ellen: 'themeEllen',
}

export function ThemePicker({ compact = false }: { compact?: boolean }) {
  const { theme, setTheme } = useTheme()
  const { t } = useLocale()

  return (
    <div className={`theme-picker${compact ? ' compact' : ''}`}>
      {!compact && <div className="muted small">{t('appearance')}</div>}
      <div className="theme-swatches" role="radiogroup" aria-label={t('appearance')}>
        {THEMES.map((id) => {
          const label = t(LABELS[id])
          return (
            <button
              key={id}
              type="button"
              role="radio"
              className={`theme-swatch theme-swatch--${id}${theme === id ? ' is-active' : ''}`}
              aria-checked={theme === id}
              aria-label={label}
              title={label}
              onClick={() => setTheme(id)}
            >
              <span className="theme-swatch-chip" aria-hidden="true" />
              {!compact && <span className="theme-swatch-label">{label}</span>}
            </button>
          )
        })}
      </div>
    </div>
  )
}
