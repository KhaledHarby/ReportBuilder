import { useEffect, useMemo, useRef, useState } from 'react'
import type { AnalyticsDimensionResult, AnalyticsSearchHit } from '../api/types'
import { ANALYTICS_NULL } from '../lib/analyticsFilters'
import { useLocale } from '../i18n/LocaleProvider'

const MAX_HITS = 24
const SEARCH_DEBOUNCE_MS = 250
const MIN_SERVER_QUERY = 2

type Hit = {
  dimensionId: string
  dimensionLabel: string
  value: string
  display: string
  state: string
  count: number
}

type Props = {
  dimensions: AnalyticsDimensionResult[]
  nullLabel: string
  onSelect: (dimensionId: string, value: string) => void
  onSearch?: (query: string, signal: AbortSignal) => Promise<AnalyticsSearchHit[]>
}

function toHit(hit: AnalyticsSearchHit, nullLabel: string): Hit {
  return {
    dimensionId: hit.dimensionId,
    dimensionLabel: hit.dimensionLabel,
    value: hit.value,
    display: hit.value === ANALYTICS_NULL ? nullLabel : hit.value,
    state: hit.state,
    count: hit.count,
  }
}

function localHits(
  dimensions: AnalyticsDimensionResult[],
  query: string,
  nullLabel: string,
): Hit[] {
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean)
  if (terms.length === 0) {
    return []
  }
  const list: Hit[] = []
  for (const dimension of dimensions) {
    for (const item of dimension.values) {
      const display = item.value === ANALYTICS_NULL ? nullLabel : item.value
      const haystack = `${dimension.label} ${display}`.toLowerCase()
      if (!terms.every((term) => haystack.includes(term))) {
        continue
      }
      list.push({
        dimensionId: dimension.dimensionId,
        dimensionLabel: dimension.label,
        value: item.value,
        display,
        state: item.state,
        count: item.count,
      })
      if (list.length >= MAX_HITS) {
        return list
      }
    }
  }
  return list
}

export function AnalyticsSmartSearch({ dimensions, nullLabel, onSelect, onSearch }: Props) {
  const { t } = useLocale()
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const [remoteHits, setRemoteHits] = useState<Hit[] | null>(null)
  const [searching, setSearching] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)

  const immediateHits = useMemo(
    () => localHits(dimensions, query, nullLabel),
    [dimensions, nullLabel, query],
  )
  const hits = remoteHits ?? immediateHits

  useEffect(() => {
    setActive(0)
  }, [query])

  useEffect(() => {
    if (!onSearch) {
      setRemoteHits(null)
      setSearching(false)
      return
    }

    const trimmed = query.trim()
    if (trimmed.length < MIN_SERVER_QUERY) {
      setRemoteHits(null)
      setSearching(false)
      return
    }

    const controller = new AbortController()
    setRemoteHits(null)
    setSearching(true)
    const timer = window.setTimeout(() => {
      void onSearch(trimmed, controller.signal)
        .then((result) => {
          if (!controller.signal.aborted) {
            setRemoteHits(result.map((hit) => toHit(hit, nullLabel)))
          }
        })
        .catch((error: unknown) => {
          if (controller.signal.aborted) {
            return
          }
          if (error instanceof DOMException && error.name === 'AbortError') {
            return
          }
          setRemoteHits(null)
        })
        .finally(() => {
          if (!controller.signal.aborted) {
            setSearching(false)
          }
        })
    }, SEARCH_DEBOUNCE_MS)

    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [nullLabel, onSearch, query])

  useEffect(() => {
    if (!open) {
      return
    }
    const onPointer = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('pointerdown', onPointer)
    return () => document.removeEventListener('pointerdown', onPointer)
  }, [open])

  function apply(hit: Hit) {
    onSelect(hit.dimensionId, hit.value)
    setQuery('')
    setRemoteHits(null)
    setOpen(false)
  }

  return (
    <div className="analytics-smart-search" ref={rootRef}>
      <input
        value={query}
        placeholder={t('analyticsSmartSearch')}
        aria-label={t('analyticsSmartSearch')}
        aria-expanded={open && (hits.length > 0 || searching)}
        aria-autocomplete="list"
        onFocus={() => setOpen(true)}
        onChange={(event) => {
          setQuery(event.target.value)
          setOpen(true)
        }}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            setOpen(false)
            return
          }
          if (event.key === 'ArrowDown') {
            event.preventDefault()
            setActive((value) => Math.min(hits.length - 1, value + 1))
            return
          }
          if (event.key === 'ArrowUp') {
            event.preventDefault()
            setActive((value) => Math.max(0, value - 1))
            return
          }
          if (event.key === 'Enter' && hits[active]) {
            event.preventDefault()
            apply(hits[active])
          }
        }}
      />
      {open && query.trim() && (
        <div className="analytics-smart-menu" role="listbox">
          {hits.length === 0 ? (
            <div className="analytics-smart-empty">
              {searching ? t('loading') : t('analyticsSmartSearchEmpty')}
            </div>
          ) : (
            <>
              {hits.map((hit, index) => (
                <button
                  key={`${hit.dimensionId}:${hit.value}`}
                  type="button"
                  role="option"
                  aria-selected={index === active}
                  className={`analytics-smart-hit ${hit.state}${index === active ? ' is-active' : ''}`}
                  onMouseEnter={() => setActive(index)}
                  onClick={() => apply(hit)}
                >
                  <span>
                    <small>{hit.dimensionLabel}</small>
                    <strong>{hit.display}</strong>
                  </span>
                  <em>{hit.count.toLocaleString()}</em>
                </button>
              ))}
              {searching && (
                <div className="analytics-smart-empty">{t('loading')}</div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  )
}
