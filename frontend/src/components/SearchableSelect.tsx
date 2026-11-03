import { useEffect, useMemo, useRef, useState } from 'react'
import { useLocale } from '../i18n/LocaleProvider'

type Props = {
  options: string[]
  value: string
  multiple?: boolean
  placeholder?: string
  loading?: boolean
  allowCustom?: boolean
  labels?: Record<string, string>
  onChange: (value: string) => void
}

function splitValues(raw: string) {
  return raw
    .split(/[,;\n]/)
    .map((part) => part.trim())
    .filter(Boolean)
}

function matches(option: string, query: string, labels?: Record<string, string>) {
  const needle = query.trim().toLowerCase()
  return (
    option.toLowerCase().includes(needle) ||
    (labels?.[option] ?? '').toLowerCase().includes(needle)
  )
}

export function SearchableSelect({
  options,
  value,
  multiple = false,
  placeholder,
  loading = false,
  allowCustom = false,
  labels,
  onChange,
}: Props) {
  const { t } = useLocale()
  const resolvedPlaceholder = placeholder ?? t('search')
  const rootRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const selected = useMemo(
    () => (multiple ? splitValues(value) : value ? [value] : []),
    [multiple, value],
  )
  const selectedSet = useMemo(
    () => new Set(selected.map((item) => item.toLowerCase())),
    [selected],
  )

  const filtered = useMemo(() => {
    const list = query.trim()
      ? options.filter((option) => matches(option, query, labels))
      : options
    return list.slice(0, 200)
  }, [options, query, labels])

  const custom =
    allowCustom &&
    query.trim() &&
    !options.some((option) => option.toLowerCase() === query.trim().toLowerCase())
      ? query.trim()
      : null

  const items = custom ? [custom, ...filtered] : filtered

  useEffect(() => {
    if (!open) {
      return
    }
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false)
        setQuery('')
      }
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [open])

  useEffect(() => {
    setActive(0)
  }, [query, open])

  const pick = (option: string) => {
    if (multiple) {
      const next = new Set(selected)
      const match = [...next].find((item) => item.toLowerCase() === option.toLowerCase())
      if (match) {
        next.delete(match)
      } else {
        next.add(option)
      }
      onChange([...next].join(', '))
      setQuery('')
      inputRef.current?.focus()
      return
    }

    onChange(option)
    setQuery('')
    setOpen(false)
  }

  const clear = () => {
    onChange('')
    setQuery('')
    inputRef.current?.focus()
  }

  const shown = (option: string) => labels?.[option] || option
  const inputValue = open || multiple ? query : shown(value)

  return (
    <div ref={rootRef} className={`search-select ${open ? 'open' : ''}`}>
      {multiple && selected.length > 0 && (
        <div className="search-select-chips">
          {selected.map((item) => (
            <button
              key={item}
              type="button"
              className="search-select-chip"
              onClick={() => pick(item)}
            >
              {shown(item)}
              <span aria-hidden="true">×</span>
            </button>
          ))}
        </div>
      )}
      <div className="search-select-control">
        <input
          ref={inputRef}
          role="combobox"
          aria-expanded={open}
          aria-autocomplete="list"
          autoComplete="off"
          placeholder={
            loading
              ? t('loadingValues')
              : selected.length && multiple
                ? t('addAnother')
                : resolvedPlaceholder
          }
          value={inputValue}
          onFocus={() => setOpen(true)}
          onChange={(event) => {
            setQuery(event.target.value)
            setOpen(true)
            if (!multiple && allowCustom) {
              onChange(event.target.value)
            }
          }}
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown') {
              event.preventDefault()
              setOpen(true)
              setActive((index) => Math.min(index + 1, Math.max(items.length - 1, 0)))
            } else if (event.key === 'ArrowUp') {
              event.preventDefault()
              setActive((index) => Math.max(index - 1, 0))
            } else if (event.key === 'Enter') {
              event.preventDefault()
              if (items[active]) {
                pick(items[active])
              } else if (allowCustom && query.trim()) {
                pick(query.trim())
              }
            } else if (event.key === 'Escape') {
              setOpen(false)
              setQuery('')
            } else if (event.key === 'Backspace' && multiple && !query && selected.length) {
              pick(selected[selected.length - 1])
            }
          }}
        />
        {value && !multiple && (
          <button type="button" className="search-select-clear" onClick={clear}>
            ×
          </button>
        )}
      </div>
      {open && (
        <ul className="search-select-menu" role="listbox">
          {loading && items.length === 0 && (
            <li className="search-select-empty">{t('loadingValues')}</li>
          )}
          {!loading && items.length === 0 && (
            <li className="search-select-empty">{t('noMatchingValues')}</li>
          )}
          {items.map((option, index) => {
            const isSelected = selectedSet.has(option.toLowerCase())
            return (
              <li key={`${option}-${index}`}>
                <button
                  type="button"
                  role="option"
                  aria-selected={isSelected}
                  className={`search-select-option ${index === active ? 'active' : ''} ${isSelected ? 'picked' : ''}`}
                  onMouseEnter={() => setActive(index)}
                  onMouseDown={(event) => event.preventDefault()}
                  onClick={() => pick(option)}
                >
                  <span>{shown(option)}</span>
                  {isSelected && <span aria-hidden="true">✓</span>}
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
