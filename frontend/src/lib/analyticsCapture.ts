type ChartCapture = {
  id: string
  title: string
  dataUrl: () => string | undefined
}

const charts = new Map<string, ChartCapture>()
let mapCapture: (() => string | null) | null = null

export function registerChartCapture(capture: ChartCapture) {
  charts.set(capture.id, capture)
  return () => {
    charts.delete(capture.id)
  }
}

export function registerMapCapture(capture: (() => string | null) | null) {
  mapCapture = capture
  return () => {
    if (mapCapture === capture) {
      mapCapture = null
    }
  }
}

export function captureCharts() {
  return [...charts.values()]
    .map((chart) => {
      const dataUrl = chart.dataUrl()
      return dataUrl ? { title: chart.title, dataUrl } : null
    })
    .filter((item): item is { title: string; dataUrl: string } => Boolean(item))
}

export function captureMapImage() {
  return mapCapture?.() ?? null
}
