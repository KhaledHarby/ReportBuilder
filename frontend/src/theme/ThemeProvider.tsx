import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

export const THEMES = ['light', 'dark', 'sand', 'palm', 'ruby', 'ellen'] as const

export type Theme = (typeof THEMES)[number]

export const THEME_STORAGE_KEY = 'lumen-theme'

type ThemeContextValue = {
  theme: Theme
  setTheme: (theme: Theme) => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

export function isTheme(value: string | null | undefined): value is Theme {
  return THEMES.includes(value as Theme)
}

export function readTheme(): Theme {
  const stored = localStorage.getItem(THEME_STORAGE_KEY)
  if (isTheme(stored)) {
    return stored
  }
  return 'ellen'
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(() =>
    typeof document === 'undefined' ? 'ellen' : readTheme(),
  )

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    localStorage.setItem(THEME_STORAGE_KEY, theme)
  }, [theme])

  const value = useMemo(
    () => ({
      theme,
      setTheme,
    }),
    [theme],
  )

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

export function useTheme() {
  const context = useContext(ThemeContext)
  if (!context) {
    throw new Error('useTheme must be used within ThemeProvider')
  }
  return context
}
