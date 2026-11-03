import { useLocale } from './LocaleProvider'

export function LangToggle({ className = 'lang-toggle' }: { className?: string }) {
  const { lang, setLang } = useLocale()

  return (
    <div className={className} role="group" aria-label="Language">
      <button
        type="button"
        className={lang === 'en' ? 'is-active' : ''}
        onClick={() => setLang('en')}
      >
        EN
      </button>
      <button
        type="button"
        className={lang === 'ar' ? 'is-active' : ''}
        onClick={() => setLang('ar')}
      >
        عربي
      </button>
    </div>
  )
}
