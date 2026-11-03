import type { Lang } from '../i18n/locale'
import { translate, type MessageKey } from '../i18n/messages'
import { localeFor } from '../i18n/locale'

export function relativeTime(value: string, lang: Lang = 'en') {
  const t = (key: MessageKey, vars?: Record<string, string | number>) =>
    translate(lang, key, vars)
  const delta = Date.now() - new Date(value).getTime()
  const minutes = Math.round(delta / 60000)
  if (Math.abs(minutes) < 1) {
    return t('justNow')
  }
  if (Math.abs(minutes) < 60) {
    return t('minutesAgo', { n: minutes })
  }
  const hours = Math.round(minutes / 60)
  if (Math.abs(hours) < 24) {
    return t('hoursAgo', { n: hours })
  }
  const days = Math.round(hours / 24)
  if (Math.abs(days) < 7) {
    return t('daysAgo', { n: days })
  }
  return new Date(value).toLocaleDateString(localeFor(lang), {
    month: 'short',
    day: 'numeric',
  })
}

export function formatStamp(value: string, lang: Lang = 'en') {
  return new Date(value).toLocaleString(localeFor(lang), {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}
