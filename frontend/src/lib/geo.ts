export type GeoPoint = {
  lat: number
  lng: number
}

const TILE = 256

export function clampLat(lat: number) {
  return Math.max(-85.05112878, Math.min(85.05112878, lat))
}

export function project(lat: number, lng: number, zoom: number) {
  const scale = TILE * 2 ** zoom
  const x = ((lng + 180) / 360) * scale
  const sin = Math.sin((clampLat(lat) * Math.PI) / 180)
  const y = (0.5 - Math.log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * scale
  return { x, y }
}

export function unproject(x: number, y: number, zoom: number): GeoPoint {
  const scale = TILE * 2 ** zoom
  const lng = (x / scale) * 360 - 180
  const n = Math.PI - (2 * Math.PI * y) / scale
  const lat = (180 / Math.PI) * Math.atan(Math.sinh(n))
  return { lat: clampLat(lat), lng: ((lng + 540) % 360) - 180 }
}

export function boundsOf(points: GeoPoint[]) {
  let minLat = 90
  let maxLat = -90
  let minLng = 180
  let maxLng = -180
  for (const point of points) {
    minLat = Math.min(minLat, point.lat)
    maxLat = Math.max(maxLat, point.lat)
    minLng = Math.min(minLng, point.lng)
    maxLng = Math.max(maxLng, point.lng)
  }
  return { minLat, maxLat, minLng, maxLng }
}

export function extentKey(points: GeoPoint[]) {
  if (points.length === 0) {
    return 'empty'
  }
  const box = boundsOf(points)
  return `${points.length}:${box.minLat.toFixed(5)}:${box.maxLat.toFixed(5)}:${box.minLng.toFixed(5)}:${box.maxLng.toFixed(5)}`
}

export function fitZoom(
  points: GeoPoint[],
  width: number,
  height: number,
  minZoom = 3,
  maxZoom = 17,
) {
  if (points.length === 0 || width <= 0 || height <= 0) {
    return { zoom: 5, center: { lat: 24.45, lng: 54.38 } }
  }
  const box = boundsOf(points)
  const center = {
    lat: (box.minLat + box.maxLat) / 2,
    lng: (box.minLng + box.maxLng) / 2,
  }
  const pad = 48
  const innerW = Math.max(64, width - pad * 2)
  const innerH = Math.max(64, height - pad * 2)
  for (let zoom = maxZoom; zoom >= minZoom; zoom--) {
    const nw = project(box.maxLat, box.minLng, zoom)
    const se = project(box.minLat, box.maxLng, zoom)
    const spanX = Math.max(Math.abs(se.x - nw.x), 1)
    const spanY = Math.max(Math.abs(se.y - nw.y), 1)
    if (spanX <= innerW && spanY <= innerH) {
      return { zoom, center }
    }
  }
  return { zoom: minZoom, center }
}

export function tileUrl(theme: string, z: number, x: number, y: number) {
  const n = 2 ** z
  const wrappedX = ((x % n) + n) % n
  if (y < 0 || y >= n) {
    return null
  }
  // Esri public raster tiles (Web Mercator, 256px). CARTO's CDN now
  // watermarks requests without an API key.
  const service =
    theme === 'dark' ? 'Canvas/World_Dark_Gray_Base' : 'World_Street_Map'
  return `https://server.arcgisonline.com/ArcGIS/rest/services/${service}/MapServer/tile/${z}/${y}/${wrappedX}`
}
