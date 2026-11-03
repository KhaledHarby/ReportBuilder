import type { ConditionalFormatRule } from '../api/types'
import {
  CONDITIONAL_FILL_PRESETS,
  CONDITIONAL_TEXT_PRESETS,
  MAX_CONDITIONAL_RULES,
  colorsEqual,
  emptyConditionalRule,
  normalizeHexColor,
} from '../lib/conditionalFormat'
import { FILTER_OPERATORS, needsFilterValue, operatorMessageKey } from '../lib/filters'
import { useLocale } from '../i18n/LocaleProvider'

type Props = {
  rules: ConditionalFormatRule[]
  onChange: (rules: ConditionalFormatRule[]) => void
}

export function ConditionalFormatEditor({ rules, onChange }: Props) {
  const { t } = useLocale()

  const updateRule = (index: number, patch: Partial<ConditionalFormatRule>) => {
    onChange(rules.map((rule, itemIndex) => (itemIndex === index ? { ...rule, ...patch } : rule)))
  }

  return (
    <div className="cf-editor">
      <div className="cf-editor-head">
        <strong>{t('conditionalFormat')}</strong>
        <p className="muted small">{t('conditionalFormatHint')}</p>
      </div>
      {rules.map((rule, index) => (
        <div key={index} className="cf-rule">
          <div className="cf-rule-top">
            <label>
              {t('conditionalFormatWhen')}
              <select
                value={rule.operator}
                aria-label={t('conditionalFormatWhen')}
                onChange={(event) => updateRule(index, { operator: event.target.value })}
              >
                {FILTER_OPERATORS.map((op) => (
                  <option key={op.value} value={op.value}>
                    {t(operatorMessageKey(op.value))}
                  </option>
                ))}
              </select>
            </label>
            {needsFilterValue(rule.operator) ? (
              <label>
                {t('conditionalFormatValue')}
                <input
                  value={rule.value ?? ''}
                  placeholder={t('value')}
                  aria-label={t('conditionalFormatValue')}
                  onChange={(event) => updateRule(index, { value: event.target.value })}
                />
              </label>
            ) : (
              <span className="muted small">{t('noValueNeeded')}</span>
            )}
            <button
              type="button"
              className="link danger"
              onClick={() => onChange(rules.filter((_, itemIndex) => itemIndex !== index))}
            >
              {t('conditionalFormatRemove')}
            </button>
          </div>
          <div className="cf-swatch-row">
            <span>{t('conditionalFormatFill')}</span>
            <div className="cf-swatches">
              {CONDITIONAL_FILL_PRESETS.map((preset) => {
                const selected = colorsEqual(rule.backgroundColor, preset.backgroundColor)
                return (
                  <button
                    key={preset.id}
                    type="button"
                    className={`cf-swatch${selected ? ' selected' : ''}${preset.backgroundColor ? '' : ' is-none'}`}
                    style={
                      preset.backgroundColor
                        ? { background: preset.backgroundColor, color: preset.color ?? undefined }
                        : undefined
                    }
                    title={t(preset.labelKey)}
                    aria-label={t(preset.labelKey)}
                    aria-pressed={selected}
                    onClick={() =>
                      updateRule(index, {
                        backgroundColor: preset.backgroundColor,
                        color: preset.color,
                      })
                    }
                  >
                    {preset.backgroundColor ? 'A' : '×'}
                  </button>
                )
              })}
              <label className="cf-custom-color" title={t('conditionalFormatCustomFill')}>
                <span className="sr-only">{t('conditionalFormatCustomFill')}</span>
                <input
                  type="color"
                  value={normalizeHexColor(rule.backgroundColor) ?? '#def7ec'}
                  aria-label={t('conditionalFormatCustomFill')}
                  onChange={(event) =>
                    updateRule(index, { backgroundColor: event.target.value })
                  }
                />
              </label>
            </div>
          </div>
          <div className="cf-swatch-row">
            <span>{t('conditionalFormatText')}</span>
            <div className="cf-swatches">
              {CONDITIONAL_TEXT_PRESETS.map((preset) => {
                const selected = colorsEqual(rule.color, preset.color)
                return (
                  <button
                    key={preset.id}
                    type="button"
                    className={`cf-swatch text${selected ? ' selected' : ''}${preset.color ? '' : ' is-none'}`}
                    style={preset.color ? { color: preset.color } : undefined}
                    title={t(preset.labelKey)}
                    aria-label={t(preset.labelKey)}
                    aria-pressed={selected}
                    onClick={() => updateRule(index, { color: preset.color })}
                  >
                    A
                  </button>
                )
              })}
              <label className="cf-custom-color" title={t('conditionalFormatCustomText')}>
                <span className="sr-only">{t('conditionalFormatCustomText')}</span>
                <input
                  type="color"
                  value={normalizeHexColor(rule.color) ?? '#111827'}
                  aria-label={t('conditionalFormatCustomText')}
                  onChange={(event) => updateRule(index, { color: event.target.value })}
                />
              </label>
            </div>
          </div>
          <label className="cf-bold">
            <input
              type="checkbox"
              checked={Boolean(rule.bold)}
              onChange={(event) => updateRule(index, { bold: event.target.checked })}
            />
            {t('conditionalFormatBold')}
          </label>
        </div>
      ))}
      <button
        type="button"
        className="btn"
        disabled={rules.length >= MAX_CONDITIONAL_RULES}
        onClick={() => onChange([...rules, emptyConditionalRule()])}
      >
        {t('conditionalFormatAdd')}
      </button>
    </div>
  )
}
