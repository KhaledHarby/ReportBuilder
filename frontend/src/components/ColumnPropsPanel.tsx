import { useState } from 'react'
import type {
  Dataset,
  DatasetColumn,
  LayoutColumn,
  ReportSummary,
} from '../api/types'
import { ConditionalFormatEditor } from './ConditionalFormatEditor'
import { isClickableUrlTemplate } from '../lib/analyticsFilters'
import type { ColumnFormatOption } from '../lib/columnFormat'
import {
  allowedDrillthroughFields,
  buildReportDrillthroughUrl,
  drillthroughTargetField,
  reportIdFromUrlTemplate,
  resolveDrillthroughField,
} from '../lib/reportLinks'
import { useLocale } from '../i18n/LocaleProvider'
import { columnLabel, localizedText } from '../i18n/localized'
import type { Lang } from '../i18n/locale'
import type { MessageKey } from '../i18n/messages'

type TabId = 'appearance' | 'actions' | 'conditional'

const TABS: { id: TabId; labelKey: MessageKey }[] = [
  { id: 'appearance', labelKey: 'columnPropsAppearance' },
  { id: 'actions', labelKey: 'columnPropsActions' },
  { id: 'conditional', labelKey: 'columnPropsConditional' },
]

type Props = {
  column: LayoutColumn
  meta: DatasetColumn
  grouped: boolean
  formatOptions: ColumnFormatOption[]
  palette: DatasetColumn[]
  reports: ReportSummary[]
  datasets: Dataset[]
  linkedReportFields: string[]
  linkedDatasetColumns: DatasetColumn[]
  onChange: (column: LayoutColumn) => void
}

function fieldChoiceLabel(lang: Lang, field: string, columns: DatasetColumn[]) {
  const match = columns.find(
    (item) => item.columnName.toLowerCase() === field.toLowerCase(),
  )
  return match ? columnLabel(lang, match) : field
}

function nextUrlTemplate(current: string | null | undefined, token: string) {
  const existing = current?.trim() ?? ''
  if (!existing) {
    return `https://example/${token}`
  }
  if (!isClickableUrlTemplate(existing) && /^\{[^}]+\}$/.test(existing)) {
    return `https://example/${token}`
  }
  return `${current ?? ''}${token}`
}

export function ColumnPropsPanel({
  column,
  meta,
  grouped,
  formatOptions,
  palette,
  reports,
  datasets,
  linkedReportFields,
  linkedDatasetColumns,
  onChange,
}: Props) {
  const { lang, t } = useLocale()
  const [tab, setTab] = useState<TabId>('appearance')
  const linkedReportId = reportIdFromUrlTemplate(column.urlTemplate)
  const targetField = drillthroughTargetField(column.urlTemplate)
  const resolvedTarget = resolveDrillthroughField(new Set(linkedReportFields), targetField)
  const hasLink = Boolean(column.urlTemplate?.trim())
  const ruleCount = column.conditionalFormat?.length ?? 0

  const patch = (next: Partial<LayoutColumn>) => onChange({ ...column, ...next })

  return (
    <div className="props">
      <div className="props-identity">
        <h3>{t('columnPropsTitle')}</h3>
        <p className="muted small">
          {columnLabel(lang, meta)} · {meta.sqlType}
        </p>
      </div>

      <div className="props-tabs" role="tablist" aria-label={t('columnPropsTitle')}>
        {TABS.map((item) => {
          const selected = tab === item.id
          const mark =
            item.id === 'actions' && hasLink
              ? 'has-value'
              : item.id === 'conditional' && ruleCount > 0
                ? 'has-value'
                : ''
          return (
            <button
              key={item.id}
              type="button"
              role="tab"
              id={`column-tab-${item.id}`}
              aria-selected={selected}
              aria-controls={`column-pane-${item.id}`}
              className={`${selected ? 'active' : ''} ${mark}`.trim()}
              onClick={() => setTab(item.id)}
            >
              {t(item.labelKey)}
            </button>
          )
        })}
      </div>

      {tab === 'appearance' && (
        <div
          className="props-pane"
          role="tabpanel"
          id="column-pane-appearance"
          aria-labelledby="column-tab-appearance"
        >
          <div className="props-pane-head">
            <h4>{t('columnPropsAppearance')}</h4>
            <p className="muted small">{t('columnPropsAppearanceHint')}</p>
          </div>
          <label>
            {t('alias')}
            <input
              value={column.alias ?? ''}
              onChange={(event) => patch({ alias: event.target.value })}
            />
          </label>
          {column.expression != null && (
            <label>
              {t('calcExpression')}
              <textarea
                rows={4}
                value={column.expression}
                placeholder={t('calcExpressionPlaceholder')}
                onChange={(event) => patch({ expression: event.target.value })}
              />
            </label>
          )}
          <label>
            {t('columnLocale')}
            <select
              value={column.locale === 'en' || column.locale === 'ar' ? column.locale : 'default'}
              onChange={(event) => {
                const locale = event.target.value
                patch({ locale: locale === 'default' ? undefined : locale })
              }}
            >
              <option value="default">{t('columnLocaleDefault')}</option>
              <option value="en">{t('columnLocaleEnglish')}</option>
              <option value="ar">{t('columnLocaleArabic')}</option>
            </select>
          </label>
          <p className="muted small">{t('columnLocaleHint')}</p>
          <label>
            {t('columnAlign')}
            <select
              value={column.align === 'left' || column.align === 'center' || column.align === 'right' ? column.align : ''}
              onChange={(event) => {
                const align = event.target.value
                patch({
                  align: align === 'left' || align === 'center' || align === 'right' ? align : undefined,
                })
              }}
            >
              <option value="">{t('columnAlignDefault')}</option>
              <option value="left">{t('columnAlignLeft')}</option>
              <option value="center">{t('columnAlignCenter')}</option>
              <option value="right">{t('columnAlignRight')}</option>
            </select>
          </label>
          <label>
            {t('columnHeaderBand')}
            <input
              value={column.headerBand ?? ''}
              onChange={(event) => patch({ headerBand: event.target.value })}
            />
          </label>
          <label>
            {t('columnHeaderBandArabic')}
            <input
              dir="rtl"
              lang="ar"
              value={column.headerBandArabic ?? ''}
              onChange={(event) => patch({ headerBandArabic: event.target.value })}
            />
          </label>
          <p className="muted small">{t('columnHeaderBandHint')}</p>
          <div className="props-flags">
            <label className="props-flag" title={t('columnWrapHint')}>
              <input
                type="checkbox"
                checked={Boolean(column.wrap)}
                onChange={(event) => patch({ wrap: event.target.checked || undefined })}
              />
              {t('columnWrap')}
            </label>
            <label className="props-flag" title={t('columnStickyHint')}>
              <input
                type="checkbox"
                checked={Boolean(column.sticky)}
                onChange={(event) => patch({ sticky: event.target.checked || undefined })}
              />
              {t('columnSticky')}
            </label>
          </div>
          <label>
            {t('aggregate')}
            <select
              value={column.aggregate ?? ''}
              disabled={grouped}
              onChange={(event) => patch({ aggregate: event.target.value || undefined })}
            >
              <option value="">{t('none')}</option>
              <option value="SUM">SUM</option>
              <option value="COUNT">COUNT</option>
              <option value="AVG">AVG</option>
              <option value="MIN">MIN</option>
              <option value="MAX">MAX</option>
            </select>
          </label>
          {formatOptions.length > 0 && (
            <label>
              {t('columnFormat')}
              <select
                value={column.format ?? ''}
                onChange={(event) => patch({ format: event.target.value || undefined })}
              >
                <option value="">{t('formatAuto')}</option>
                {formatOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {t(option.labelKey)}
                  </option>
                ))}
              </select>
            </label>
          )}
        </div>
      )}

      {tab === 'actions' && (
        <div
          className="props-pane"
          role="tabpanel"
          id="column-pane-actions"
          aria-labelledby="column-tab-actions"
        >
          <div className="props-pane-head">
            <h4>{t('columnPropsActions')}</h4>
            <p className="muted small">{t('columnPropsActionsHint')}</p>
          </div>
          <label>
            {t('columnUrl')}
            <input
              value={column.urlTemplate ?? ''}
              placeholder="/reports/{id}/view?{Field}={Field}"
              onChange={(event) => patch({ urlTemplate: event.target.value || null })}
            />
          </label>
          <label>
            {t('columnUrlLinkReport')}
            <select
              value={linkedReportId}
              onChange={(event) => {
                const reportId = event.target.value
                const summary = reports.find((report) => report.id === reportId)
                const dataset = datasets.find((item) => item.id === summary?.datasetId)
                const nextTarget = resolveDrillthroughField(
                  allowedDrillthroughFields(undefined, dataset?.columns),
                  column.field,
                )
                patch({
                  urlTemplate: reportId
                    ? buildReportDrillthroughUrl(reportId, column.field, nextTarget ?? '')
                    : null,
                })
              }}
            >
              <option value="">{t('columnUrlLinkReport')}</option>
              {reports.map((report) => (
                <option key={report.id} value={report.id}>
                  {localizedText(lang, report.name, report.nameArabic)}
                </option>
              ))}
            </select>
          </label>
          {linkedReportId ? (
            <label>
              {t('columnUrlLinkField')}
              <select
                value={resolvedTarget ?? ''}
                onChange={(event) =>
                  patch({
                    urlTemplate: buildReportDrillthroughUrl(
                      linkedReportId,
                      column.field,
                      event.target.value,
                    ),
                  })
                }
              >
                <option value="">{t('columnUrlLinkField')}</option>
                {linkedReportFields.map((field) => (
                  <option key={field} value={field}>
                    {fieldChoiceLabel(lang, field, linkedDatasetColumns)}
                  </option>
                ))}
              </select>
            </label>
          ) : null}
          {linkedReportId && targetField && !resolvedTarget && linkedReportFields.length > 0 ? (
            <p className="muted small">
              {t('columnUrlLinkFieldMissing', { field: column.field })}
            </p>
          ) : null}
          <label>
            {t('columnUrlInsert')}
            <select
              value=""
              onChange={(event) => {
                const field = event.target.value
                if (!field) {
                  return
                }
                patch({
                  urlTemplate: nextUrlTemplate(column.urlTemplate, `{${field}}`),
                })
              }}
            >
              <option value="">{t('columnUrlInsert')}</option>
              {palette.map((item) => (
                <option key={item.columnName} value={item.columnName}>
                  {columnLabel(lang, item)}
                </option>
              ))}
            </select>
          </label>
          {column.urlTemplate?.trim() && !isClickableUrlTemplate(column.urlTemplate) ? (
            <p className="muted small">{t('columnUrlNeedsHttp')}</p>
          ) : (
            <p className="muted small">{t('columnUrlHint')}</p>
          )}
        </div>
      )}

      {tab === 'conditional' && (
        <div
          className="props-pane"
          role="tabpanel"
          id="column-pane-conditional"
          aria-labelledby="column-tab-conditional"
        >
          <div className="props-pane-head">
            <h4>{t('conditionalFormat')}</h4>
            <p className="muted small">{t('conditionalFormatHint')}</p>
          </div>
          <ConditionalFormatEditor
            rules={column.conditionalFormat ?? []}
            onChange={(conditionalFormat) =>
              patch({
                conditionalFormat: conditionalFormat.length ? conditionalFormat : null,
              })
            }
          />
        </div>
      )}
    </div>
  )
}
