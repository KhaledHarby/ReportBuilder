export type Lang = 'en' | 'ar'

export const LANG_KEY = 'rb.lang'
const LEGACY_LANG_KEY = 'rb.loginLang'

export function hasChosenLang(): boolean {
  if (typeof localStorage === 'undefined') {
    return false
  }
  return localStorage.getItem(LANG_KEY) != null || localStorage.getItem(LEGACY_LANG_KEY) != null
}

export function readLang(): Lang {
  if (!hasChosenLang()) {
    return 'en'
  }
  const stored = localStorage.getItem(LANG_KEY) ?? localStorage.getItem(LEGACY_LANG_KEY)
  return stored === 'ar' ? 'ar' : 'en'
}

export function applyLang(lang: Lang, persist = true) {
  const root = document.documentElement
  root.lang = lang === 'ar' ? 'ar' : 'en'
  root.dir = lang === 'ar' ? 'rtl' : 'ltr'
  if (persist) {
    localStorage.setItem(LANG_KEY, lang)
  }
}

export function localeFor(lang: Lang) {
  return lang === 'ar' ? 'ar-AE' : 'en-GB'
}
