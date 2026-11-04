import type { AnalyticsChartPoint, AnalyticsChartResult, AnalyticsDimensionResult } from '../api/types'
import {
  clearDimension,
  type AnalyticsDrillStep,
  type AnalyticsSelections,
  type AnalyticsViewState,
} from './analyticsFilters'

/** Chart id reserved for the ad-hoc breakdown requested by the drill view. */
export const DRILL_CHART_ID = '__drill__'

function sameValue(left: string, right: string) {
  return left.toLowerCase() === right.toLowerCase()
}

function selectionIs(selections: AnalyticsSelections, dimensionId: string, value: string) {
  const current = selections[dimensionId] ?? []
  return current.length === 1 && sameValue(current[0], value)
}

/** Drop drill steps whose exclusive selection was cleared or widened. */
export function reconcileDrill(view: AnalyticsViewState): AnalyticsViewState {
  const drill = view.drill ?? []
  const kept: AnalyticsDrillStep[] = []
  for (const step of drill) {
    if (!selectionIs(view.default, step.dimensionId, step.value)) {
      break
    }
    kept.push(step)
  }
  if (kept.length === drill.length && view.drill) {
    return view
  }
  return { ...view, drill: kept }
}

/** Keep the first `depth` steps and release the selections that belonged to the rest. */
export function drillToLevel(view: AnalyticsViewState, depth: number): AnalyticsViewState {
  const drill = view.drill ?? []
  const nextDepth = Math.max(0, Math.min(depth, drill.length))
  let selections = view.default
  let compare = view.compare
  for (const step of drill.slice(nextDepth)) {
    if (view.locked.includes(step.dimensionId)) {
      continue
    }
    if (selectionIs(selections, step.dimensionId, step.value)) {
      selections = clearDimension(selections, step.dimensionId)
    }
    if (selectionIs(compare, step.dimensionId, step.value)) {
      compare = clearDimension(compare, step.dimensionId)
    }
  }
  return reconcileDrill({
    ...view,
    default: selections,
    compare,
    drill: drill.slice(0, nextDepth),
  })
}

/**
 * Focus a single value and append it to the path.
 * Clicking the current last step again steps back up.
 * Drilling a dimension already on the path replaces that step and everything after it.
 */
export function drillInto(
  view: AnalyticsViewState,
  dimensionId: string,
  value: string,
): AnalyticsViewState {
  if (!dimensionId || value.length === 0 || view.locked.includes(dimensionId)) {
    return view
  }
  const drill = view.drill ?? []
  const last = drill[drill.length - 1]
  if (last && last.dimensionId === dimensionId && sameValue(last.value, value)) {
    return drillToLevel(view, drill.length - 1)
  }

  const existing = drill.findIndex((step) => step.dimensionId === dimensionId)
  const kept = existing >= 0 ? drill.slice(0, existing) : drill
  let selections = view.default
  let compare = view.compare
  const dimStates = { ...view.dimStates }
  for (const step of existing >= 0 ? drill.slice(existing) : []) {
    if (view.locked.includes(step.dimensionId)) {
      continue
    }
    if (selectionIs(selections, step.dimensionId, step.value)) {
      selections = clearDimension(selections, step.dimensionId)
    }
    compare = clearDimension(compare, step.dimensionId)
    delete dimStates[step.dimensionId]
  }
  // Drill-down always focuses state A, so the path, the chart, and the list agree.
  compare = clearDimension(compare, dimensionId)
  delete dimStates[dimensionId]

  return {
    ...view,
    default: { ...selections, [dimensionId]: [value] },
    compare,
    dimStates,
    drill: [...kept, { dimensionId, value }],
  }
}

/** Next dimension to break down by: the first one not already on the path, else the finest. */
export function nextBreakdownDimension(
  dimensionIds: string[],
  drill: AnalyticsDrillStep[],
): string | null {
  if (dimensionIds.length === 0) {
    return null
  }
  const used = new Set(drill.map((step) => step.dimensionId))
  return dimensionIds.find((id) => !used.has(id)) ?? dimensionIds[dimensionIds.length - 1]
}

export function drillAtLeaf(dimensionIds: string[], drill: AnalyticsDrillStep[]): boolean {
  return dimensionIds.length > 0 && dimensionIds.every((id) => drill.some((step) => step.dimensionId === id))
}

export function resolveDrillSeries(options: {
  dimensionId: string
  measureId: string | null
  dimensions: AnalyticsDimensionResult[]
  charts: AnalyticsChartResult[] | undefined
  serverDrill: boolean
}): { points: AnalyticsChartPoint[]; usesCount: boolean } {
  const charts = options.charts ?? []
  if (options.serverDrill && options.measureId) {
    const drill = charts.find(
      (chart) =>
        chart.chartId === DRILL_CHART_ID &&
        chart.dimensionId === options.dimensionId &&
        chart.measureId === options.measureId,
    )
    if (drill) {
      return { points: drill.points, usesCount: false }
    }
  }

  if (options.measureId) {
    const match = charts.find(
      (chart) =>
        chart.chartId !== DRILL_CHART_ID &&
        chart.dimensionId === options.dimensionId &&
        chart.measureId === options.measureId &&
        chart.state !== 'compare',
    )
    if (match) {
      return { points: match.points, usesCount: false }
    }
  }

  const dimension = options.dimensions.find((item) => item.dimensionId === options.dimensionId)
  return {
    points: (dimension?.values ?? [])
      .filter((item) => item.state !== 'excluded')
      .map((item) => ({ value: item.value, metric: item.count })),
    usesCount: true,
  }
}
