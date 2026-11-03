import { useEffect, useLayoutEffect, useMemo, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import type { AnalyticsMapPoint } from '../api/types'
import { useLocale } from '../i18n/LocaleProvider'
import { registerMapCapture } from '../lib/analyticsCapture'
import { extentKey, fitZoom, project, tileUrl, unproject, type GeoPoint } from '../lib/geo'
import { useTheme } from '../theme/ThemeProvider'

const TILE = 256
const MIN_ZOOM = 3
const MAX_ZOOM = 17

type Cluster = {
  id: string
  lat: number
  lng: number
  count: number
  label: string
  points: AnalyticsMapPoint[]
}

type Props = {
  points: AnalyticsMapPoint[]
  emptyLabel: string
  title: string
  loading?: boolean
  onSelect?: (label: string) => void
}

export function AnalyticsMapView({ points, emptyLabel, title, loading, onSelect }: Props) {
  const { t } = useLocale()
  const { theme } = useTheme()
  const hostRef = useRef<HTMLDivElement>(null)
  const [size, setSize] = useState<{ width: number; height: number } | null>(null)
  const dataKey = useMemo(() => extentKey(points), [points])
  const fitted = useMemo(
    () => fitZoom(points, size?.width ?? 0, size?.height ?? 0),
    [points, size],
  )
  const [userView, setUserView] = useState<{ zoom: number; center: GeoPoint } | null>(null)
  const zoom = (userView ?? fitted).zoom
  const center = (userView ?? fitted).center
  const [hover, setHover] = useState<{ x: number; y: number; cluster: Cluster } | null>(null)
  const drag = useRef<{ x: number; y: number; cx: number; cy: number } | null>(null)

  useLayoutEffect(() => {
    const host = hostRef.current
    if (!host) {
      return
    }
    const apply = () => {
      const rect = host.getBoundingClientRect()
      if (rect.width > 0 && rect.height > 0) {
        setSize((prev) =>
          prev && Math.abs(prev.width - rect.width) < 0.5 && Math.abs(prev.height - rect.height) < 0.5
            ? prev
            : { width: rect.width, height: rect.height },
        )
      }
    }
    apply()
    const observer = new ResizeObserver(apply)
    observer.observe(host)
    return () => observer.disconnect()
  }, [points.length])

  useEffect(() => {
    setUserView(null)
  }, [dataKey])

  const mapWidth = size?.width ?? 0
  const mapHeight = size?.height ?? 0

  const origin = useMemo(() => {
    const px = project(center.lat, center.lng, zoom)
    return { x: px.x - mapWidth / 2, y: px.y - mapHeight / 2 }
  }, [center.lat, center.lng, mapHeight, mapWidth, zoom])

  const tiles = useMemo(() => {
    if (mapWidth <= 0 || mapHeight <= 0) {
      return []
    }
    const minX = Math.floor(origin.x / TILE)
    const minY = Math.floor(origin.y / TILE)
    const maxX = Math.floor((origin.x + mapWidth) / TILE)
    const maxY = Math.floor((origin.y + mapHeight) / TILE)
    const list: { key: string; left: number; top: number; src: string }[] = []
    for (let x = minX; x <= maxX; x++) {
      for (let y = minY; y <= maxY; y++) {
        const src = tileUrl(theme, zoom, x, y)
        if (!src) continue
        list.push({
          key: `${zoom}:${x}:${y}`,
          left: x * TILE - origin.x,
          top: y * TILE - origin.y,
          src,
        })
      }
    }
    return list
  }, [mapHeight, mapWidth, origin.x, origin.y, theme, zoom])

  const clusters = useMemo(() => {
    const bucket = 42
    const groups = new Map<string, AnalyticsMapPoint[]>()
    for (const point of points) {
      const px = project(point.lat, point.lng, zoom)
      const gx = Math.round((px.x - origin.x) / bucket)
      const gy = Math.round((px.y - origin.y) / bucket)
      const key = `${gx}:${gy}`
      const current = groups.get(key) ?? []
      current.push(point)
      groups.set(key, current)
    }
    const result: Cluster[] = []
    for (const [id, members] of groups) {
      const lat = members.reduce((sum, item) => sum + item.lat, 0) / members.length
      const lng = members.reduce((sum, item) => sum + item.lng, 0) / members.length
      result.push({
        id,
        lat,
        lng,
        count: members.length,
        label: members.length === 1 ? members[0].label : `${members.length}`,
        points: members,
      })
    }
    return result
  }, [origin.x, origin.y, points, zoom])

  function screenOf(lat: number, lng: number) {
    const px = project(lat, lng, zoom)
    return { x: px.x - origin.x, y: px.y - origin.y }
  }

  function zoomBy(delta: number, around?: { x: number; y: number }) {
    const next = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, zoom + delta))
    if (next === zoom) {
      return
    }
    const focus = around ?? { x: mapWidth / 2, y: mapHeight / 2 }
    const geo = unproject(origin.x + focus.x, origin.y + focus.y, zoom)
    const nextOrigin = project(geo.lat, geo.lng, next)
    const shifted = unproject(
      nextOrigin.x - focus.x + mapWidth / 2,
      nextOrigin.y - focus.y + mapHeight / 2,
      next,
    )
    setUserView({ zoom: next, center: shifted })
  }

  const zoomByRef = useRef(zoomBy)
  zoomByRef.current = zoomBy

  useEffect(() => {
    const host = hostRef.current
    if (!host) {
      return
    }
    const onWheel = (event: WheelEvent) => {
      event.preventDefault()
      const rect = host.getBoundingClientRect()
      zoomByRef.current(event.deltaY > 0 ? -1 : 1, {
        x: event.clientX - rect.left,
        y: event.clientY - rect.top,
      })
    }
    host.addEventListener('wheel', onWheel, { passive: false })
    return () => host.removeEventListener('wheel', onWheel)
  }, [points.length])

  function onPointerDown(event: ReactPointerEvent<HTMLDivElement>) {
    if (event.button !== 0) {
      return
    }
    if ((event.target as HTMLElement).closest('button')) {
      return
    }
    drag.current = {
      x: event.clientX,
      y: event.clientY,
      cx: origin.x + mapWidth / 2,
      cy: origin.y + mapHeight / 2,
    }
    event.currentTarget.setPointerCapture(event.pointerId)
  }

  function onPointerMove(event: ReactPointerEvent<HTMLDivElement>) {
    if (!drag.current) {
      return
    }
    const dx = event.clientX - drag.current.x
    const dy = event.clientY - drag.current.y
    setUserView({ zoom, center: unproject(drag.current.cx - dx, drag.current.cy - dy, zoom) })
  }

  function onPointerUp(event: ReactPointerEvent<HTMLDivElement>) {
    if (!drag.current) {
      return
    }
    drag.current = null
    event.currentTarget.releasePointerCapture(event.pointerId)
  }

  function activate(cluster: Cluster) {
    if (cluster.count > 1 && zoom < MAX_ZOOM - 1) {
      zoomBy(2, screenOf(cluster.lat, cluster.lng))
      return
    }
    const label = cluster.points[0]?.label?.trim()
    if (label && onSelect) {
      onSelect(label)
    }
  }

  useEffect(() => {
    if (!size || points.length === 0) {
      return
    }
    return registerMapCapture(() => {
      const host = hostRef.current
      if (!host) {
        return null
      }
      const canvas = document.createElement('canvas')
      canvas.width = Math.max(1, Math.round(size.width))
      canvas.height = Math.max(1, Math.round(size.height))
      const ctx = canvas.getContext('2d')
      if (!ctx) {
        return null
      }
      ctx.fillStyle = getComputedStyle(host).backgroundColor || '#dce6f0'
      ctx.fillRect(0, 0, canvas.width, canvas.height)
      for (const image of host.querySelectorAll<HTMLImageElement>('img.analytics-map-tile')) {
        if (!image.complete || image.naturalWidth === 0) {
          continue
        }
        const match = /translate\(([-\d.]+)px,\s*([-\d.]+)px\)/.exec(image.style.transform)
        const left = match ? Number(match[1]) : 0
        const top = match ? Number(match[2]) : 0
        ctx.drawImage(image, left, top, TILE, TILE)
      }
      for (const cluster of clusters) {
        const pos = screenOf(cluster.lat, cluster.lng)
        ctx.beginPath()
        ctx.fillStyle = cluster.count > 1 ? '#0f4c81' : '#1d4ed8'
        ctx.arc(pos.x, pos.y, cluster.count > 1 ? 14 : 8, 0, Math.PI * 2)
        ctx.fill()
        if (cluster.count > 1) {
          ctx.fillStyle = '#fff'
          ctx.font = '600 11px sans-serif'
          ctx.textAlign = 'center'
          ctx.textBaseline = 'middle'
          ctx.fillText(String(cluster.count), pos.x, pos.y)
        }
      }
      try {
        return canvas.toDataURL('image/png')
      } catch {
        return null
      }
    })
  }, [clusters, points.length, size])

  return (
    <section className="analytics-map">
      <header>
        <div>
          <h2>{title}</h2>
          <p className="analytics-map-hint">{t('analyticsMapHint')}</p>
        </div>
        <div className="analytics-map-toolbar">
          <span className="analytics-map-count">
            {loading ? '—' : `${points.length.toLocaleString()} ${t('analyticsMapPoints')}`}
          </span>
          <button type="button" className="analytics-icon-btn" title={t('analyticsMapZoomOut')} onClick={() => zoomBy(-1)}>
            −
          </button>
          <button type="button" className="analytics-icon-btn" title={t('analyticsMapZoomIn')} onClick={() => zoomBy(1)}>
            +
          </button>
          <button
            type="button"
            className="analytics-btn ghost"
            onClick={() => setUserView(null)}
          >
            {t('analyticsMapFit')}
          </button>
        </div>
      </header>
      {loading ? (
        <span className="analytics-skeleton analytics-skeleton-map" aria-hidden="true" />
      ) : points.length === 0 ? (
        <div className="analytics-empty-block">{emptyLabel}</div>
      ) : (
        <div
          ref={hostRef}
          className="analytics-map-viewport"
          style={{ visibility: size ? 'visible' : 'hidden' }}
          tabIndex={0}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}
          onKeyDown={(event) => {
            if (event.key === '+' || event.key === '=') zoomBy(1)
            if (event.key === '-' || event.key === '_') zoomBy(-1)
          }}
        >
          {tiles.map((tile) => (
            <img
              key={tile.key}
              className="analytics-map-tile"
              alt=""
              draggable={false}
              referrerPolicy="no-referrer-when-downgrade"
              crossOrigin="anonymous"
              src={tile.src}
              style={{ transform: `translate(${tile.left}px, ${tile.top}px)` }}
            />
          ))}
          {clusters.map((cluster) => {
            const pos = screenOf(cluster.lat, cluster.lng)
            const large = cluster.count > 1
            return (
              <button
                key={cluster.id}
                type="button"
                className={`analytics-map-marker${large ? ' is-cluster' : ''}`}
                style={{ transform: `translate(${pos.x}px, ${pos.y}px)` }}
                title={
                  large
                    ? t('analyticsMapCluster', { n: cluster.count })
                    : cluster.label || t('analyticsMapUntitled')
                }
                aria-label={
                  large
                    ? t('analyticsMapCluster', { n: cluster.count })
                    : cluster.label || t('analyticsMapUntitled')
                }
                onMouseEnter={() => setHover({ x: pos.x, y: pos.y, cluster })}
                onMouseLeave={() => setHover(null)}
                onClick={(event) => {
                  event.stopPropagation()
                  activate(cluster)
                }}
              >
                {large ? cluster.count : ''}
              </button>
            )
          })}
          {hover && (
            <div className="analytics-map-tooltip" style={{ left: hover.x, top: hover.y }}>
              <strong>
                {hover.cluster.count === 1
                  ? hover.cluster.label || t('analyticsMapUntitled')
                  : t('analyticsMapCluster', { n: hover.cluster.count })}
              </strong>
              <span>
                {hover.cluster.lat.toFixed(4)}, {hover.cluster.lng.toFixed(4)}
              </span>
            </div>
          )}
        </div>
      )}
      <p className="analytics-map-credit">{t('analyticsMapAttribution')}</p>
    </section>
  )
}
