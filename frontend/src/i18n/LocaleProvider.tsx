import { useQuery } from '@tanstack/react-query'
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { api } from '../api/client'
import { applyLang, hasChosenLang, localeFor, readLang, type Lang } from './locale'
import { translate, type MessageKey } from './messages'

type Vars = Record<string, string | number>

type LocaleContextValue = {
  lang: Lang
  dir: 'ltr' | 'rtl'
  locale: string
  setLang: (lang: Lang) => void
  t: (key: MessageKey, vars?: Vars) => string
}

const LocaleContext = createContext<LocaleContextValue | null>(null)

export function LocaleProvider({ children }: { children: ReactNode }) {
  const chosen = useRef(typeof document !== 'undefined' && hasChosenLang())
  const [lang, setLangState] = useState<Lang>(() =>
    typeof document === 'undefined' ? 'en' : readLang(),
  )
  const branding = useQuery({
    queryKey: ['branding'],
    queryFn: api.getBranding,
    staleTime: 60_000,
    enabled: !chosen.current,
  })

  useEffect(() => {
    if (chosen.current) {
      return
    }
    const next = branding.data?.defaultLanguage === 'ar' ? 'ar' : 'en'
    setLangState(next)
  }, [branding.data?.defaultLanguage])

  useEffect(() => {
    applyLang(lang, chosen.current)
  }, [lang])

  const setLang = useCallback((next: Lang) => {
    chosen.current = true
    setLangState(next)
    applyLang(next, true)
  }, [])

  const t = useCallback(
    (key: MessageKey, vars?: Vars) => translate(lang, key, vars),
    [lang],
  )

  const value = useMemo(
    () => ({
      lang,
      dir: (lang === 'ar' ? 'rtl' : 'ltr') as 'ltr' | 'rtl',
      locale: localeFor(lang),
      setLang,
      t,
    }),
    [lang, setLang, t],
  )

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>
}

export function useLocale() {
  const context = useContext(LocaleContext)
  if (!context) {
    throw new Error('useLocale must be used within LocaleProvider')
  }
  return context
}
