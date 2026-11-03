import type { ConditionalFormatRule, LayoutColumn, ReportLayout, ReportResult } from '../api/types'
import { needsFilterValue } from './filters'
import type { MessageKey } from '../i18n/messages'

export type ConditionalCellStyle = {
  backgroundColor?: string
  color?: string
  fontWeight?: 'bold'
}

export type ConditionalColorPreset = {
  id: string
  backgroundColor: string | null
  color: string | null
  labelKey: MessageKey
}

export const CONDITIONAL_FILL_PRESETS: ConditionalColorPreset[] = [
  { id: 'none', backgroundColor: null, color: null, labelKey: 'conditionalFormatFillNone' },
  { id: 'red', backgroundColor: '#fde8e8', color: '#9b1c1c', labelKey: 'conditionalFormatFillRed' },
  { id: 'orange', backgroundColor: '#feecdc', color: '#9a3412', labelKey: 'conditionalFormatFillOrange' },
  { id: 'yellow', backgroundColor: '#fdf6b2', color: '#723b13', labelKey: 'conditionalFormatFillYellow' },
  { id: 'green', backgroundColor: '#def7ec', color: '#03543f', labelKey: 'conditionalFormatFillGreen' },
  { id: 'blue', backgroundColor: '#e1effe', color: '#1e429f', labelKey: 'conditionalFormatFillBlue' },
  { id: 'gray', backgroundColor: '#f3f4f6', color: '#1f2937', labelKey: 'conditionalFormatFillGray' },
]

export const CONDITIONAL_TEXT_PRESETS: { id: string; color: string | null; labelKey: MessageKey }[] = [
  { id: 'auto', color: null, labelKey: 'conditionalFormatTextAuto' },
  { id: 'red', color: '#9b1c1c', labelKey: 'conditionalFormatFillRed' },
  { id: 'orange', color: '#9a3412', labelKey: 'conditionalFormatFillOrange' },
  { id: 'green', color: '#03543f', labelKey: 'conditionalFormatFillGreen' },
  { id: 'blue', color: '#1e429f', labelKey: 'conditionalFormatFillBlue' },
  { id: 'dark', color: '#111827', labelKey: 'conditionalFormatTextDark' },
  { id: 'white', color: '#ffffff', labelKey: 'conditionalFormatTextWhite' },
]

export const MAX_CONDITIONAL_RULES = 12

export function emptyConditionalRule(): ConditionalFormatRule {
  const preset = CONDITIONAL_FILL_PRESETS.find((item) => item.id === 'green')!
  return {
    operator: 'eq',
    value: '',
    backgroundColor: preset.backgroundColor,
    color: preset.color,
    bold: false,
  }
}

export function normalizeHexColor(color: string | null | undefined): string | null {
  if (!color?.trim()) {
    return null
  }
  let value = color.trim()
  if (value.startsWith('#')) {
    value = value.slice(1)
  }
  if (value.length !== 3 && value.length !== 6) {
    return null
  }
  if (!/^[0-9a-fA-F]+$/.test(value)) {
    return null
  }
  if (value.length === 3) {
    value = `${value[0]}${value[0]}${value[1]}${value[1]}${value[2]}${value[2]}`
  }
  return `#${value.toLowerCase()}`
}

export function colorsEqual(left: string | null | undefined, right: string | null | undefined) {
  return normalizeHexColor(left) === normalizeHexColor(right)
}

function isEmptyCell(value: unknown) {
  return value == null || String(value).trim() === ''
}

function cellText(value: unknown) {
  return value == null ? '' : String(value).trim()
}

function splitList(raw: string | null | undefined) {
  return (raw ?? '')
    .split(/[,;\n]/)
    .map((part) => part.trim())
    .filter(Boolean)
}

function compareCell(value: unknown, target: string): number | null {
  if (isEmptyCell(value) && target.trim() === '') {
    return 0
  }
  if (isEmptyCell(value) || target.trim() === '') {
    return null
  }

  const leftText = cellText(value)
  const rightText = target.trim()
  if (typeof value === 'number' && Number.isFinite(value)) {
    const rightNumber = Number(rightText)
    if (Number.isFinite(rightNumber)) {
      return value - rightNumber
    }
  }

  const leftNumber = Number(leftText)
  const rightNumber = Number(rightText)
  if (
    leftText !== '' &&
    rightText !== '' &&
    Number.isFinite(leftNumber) &&
    Number.isFinite(rightNumber)
  ) {
    return leftNumber - rightNumber
  }

  const leftDate = Date.parse(leftText)
  const rightDate = Date.parse(rightText)
  if (
    Number.isFinite(leftDate) &&
    Number.isFinite(rightDate) &&
    /\d/.test(leftText) &&
    /[-/T.]/.test(leftText)
  ) {
    return leftDate - rightDate
  }

  return leftText.localeCompare(rightText, undefined, { sensitivity: 'accent', numeric: true })
}

function valuesEqual(value: unknown, target: string) {
  const cmp = compareCell(value, target)
  if (cmp === 0) {
    return true
  }
  return cellText(value).toLowerCase() === target.trim().toLowerCase()
}

export function ruleMatchesCell(value: unknown, rule: ConditionalFormatRule) {
  const operator = rule.operator || 'eq'
  if (operator === 'isNull') {
    return isEmptyCell(value)
  }
  if (operator === 'isNotNull') {
    return !isEmptyCell(value)
  }
  if (needsFilterValue(operator) && !rule.value?.trim() && operator !== 'contains') {
    return false
  }

  if (operator === 'contains') {
    const needle = (rule.value ?? '').trim().toLowerCase()
    if (!needle) {
      return false
    }
    return cellText(value).toLowerCase().includes(needle)
  }

  if (operator === 'in') {
    return splitList(rule.value).some((part) => valuesEqual(value, part))
  }

  if (operator === 'eq') {
    return valuesEqual(value, rule.value ?? '')
  }
  if (operator === 'neq') {
    return !valuesEqual(value, rule.value ?? '')
  }

  const cmp = compareCell(value, rule.value ?? '')
  if (cmp == null) {
    return false
  }
  switch (operator) {
    case 'gt':
      return cmp > 0
    case 'gte':
      return cmp >= 0
    case 'lt':
      return cmp < 0
    case 'lte':
      return cmp <= 0
    default:
      return false
  }
}

export function resolveConditionalStyle(
  value: unknown,
  rules: ConditionalFormatRule[] | null | undefined,
): ConditionalCellStyle | undefined {
  if (!rules?.length) {
    return undefined
  }

  for (const rule of rules) {
    if (!ruleMatchesCell(value, rule)) {
      continue
    }
    const backgroundColor = normalizeHexColor(rule.backgroundColor) ?? undefined
    const color = normalizeHexColor(rule.color) ?? undefined
    const fontWeight = rule.bold ? ('bold' as const) : undefined
    if (!backgroundColor && !color && !fontWeight) {
      continue
    }
    return { backgroundColor, color, fontWeight }
  }

  return undefined
}

export function buildConditionalFormatMap(
  layout: ReportLayout | undefined,
  result: ReportResult | null | undefined,
) {
  const map: Record<string, ConditionalFormatRule[]> = {}
  if (!layout?.columns.length) {
    return map
  }

  layout.columns.forEach((column: LayoutColumn, index) => {
    const rules = column.conditionalFormat?.filter(Boolean)
    if (!rules?.length) {
      return
    }

    const resultColumn = result?.columns[index]
    if (resultColumn?.name) {
      map[resultColumn.name] = rules
    }
    map[column.field] = rules
    if (column.alias?.trim()) {
      map[column.alias.trim()] = rules
    }
  })

  return map
}
