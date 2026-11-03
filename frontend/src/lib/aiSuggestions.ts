import type { Dataset } from '../api/types'

const DEFAULT_SUGGESTIONS = [
  'Orders grouped by region',
  'Bar chart of order totals by status',
  'Customers in the East, with an English name and an Arabic name',
]

/** Prompts tuned to dbo.RPT_FIRM_DETAILS (MECS trade-license offices). */
const FIRM_DETAILS_SUGGESTIONS = [
  'Count firms by license status',
  'Bar chart of firms by license status',
  'Issued Consultant and Contractor firms with English name, type, and expiry date',
  'Valid classified firms with English name and annual evaluation, highest first',
]

export function suggestionsFor(dataset: Dataset | undefined): string[] {
  const objectName = dataset?.objectName?.toUpperCase() ?? ''
  if (objectName === 'RPT_FIRM_DETAILS' || objectName.includes('FIRM_DETAIL')) {
    return FIRM_DETAILS_SUGGESTIONS
  }

  return DEFAULT_SUGGESTIONS
}

export function promptPlaceholder(dataset: Dataset | undefined): string {
  const objectName = dataset?.objectName?.toUpperCase() ?? ''
  if (objectName === 'RPT_FIRM_DETAILS' || objectName.includes('FIRM_DETAIL')) {
    return 'e.g. Count firms by classification category'
  }

  return 'e.g. Show order counts by region, newest first'
}
