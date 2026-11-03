import { useMutation, useQuery } from '@tanstack/react-query'
import { useMemo, useRef, useState } from 'react'
import { api } from '../api/client'
import type { AiAssistResult, ReportLayout } from '../api/types'
import { promptPlaceholder, suggestionsFor } from '../lib/aiSuggestions'
import { useLocale } from '../i18n/LocaleProvider'
import { IconSpark } from './Icons'

export function AiAssistPanel({
  datasetId,
  name,
  nameArabic,
  description,
  descriptionArabic,
  layout,
  onApply,
}: {
  datasetId?: string
  name?: string
  nameArabic?: string
  description?: string
  descriptionArabic?: string
  layout?: ReportLayout
  onApply: (suggestion: AiAssistResult) => void
}) {
  const { t } = useLocale()
  const [prompt, setPrompt] = useState('')
  const promptRef = useRef<HTMLTextAreaElement>(null)
  const status = useQuery({
    queryKey: ['ai-status'],
    queryFn: api.aiStatus,
    staleTime: 0,
    refetchOnMount: 'always',
  })
  const datasets = useQuery({
    queryKey: ['datasets', 'enabled'],
    queryFn: () => api.listDatasets(true),
    enabled: status.data?.configured === true,
  })
  const assist = useMutation({
    mutationFn: () =>
      api.aiAssist({
        prompt,
        datasetId: datasetId || undefined,
        name,
        nameArabic,
        description,
        descriptionArabic,
        layout,
      }),
    onSuccess: (result) => {
      onApply(result)
    },
  })

  const dataset = useMemo(() => {
    const list = datasets.data ?? []
    if (datasetId) {
      return list.find((item) => item.id === datasetId) ?? list[0]
    }
    return list[0]
  }, [datasets.data, datasetId])

  const examples = suggestionsFor(dataset)
  const configured = status.data?.configured === true

  const fillPrompt = (text: string) => {
    setPrompt(text)
    promptRef.current?.focus()
  }

  if (status.isLoading || !configured) {
    return null
  }

  return (
    <section className="ai-panel">
      <div className="ai-panel-head">
        <div>
          <div className="kicker">
            <IconSpark /> {t('assistant')}
          </div>
          <h2>{t('aiTitle')}</h2>
          <p className="muted small">{t('aiHint')}</p>
        </div>
      </div>

      <form
        className="ai-form"
        onSubmit={(event) => {
          event.preventDefault()
          if (!prompt.trim()) {
            return
          }
          assist.mutate()
        }}
      >
        <textarea
          ref={promptRef}
          rows={3}
          value={prompt}
          onChange={(event) => setPrompt(event.target.value)}
          placeholder={promptPlaceholder(dataset)}
          disabled={assist.isPending}
        />
        <div className="ai-form-actions">
          <div className="ai-chips">
            {examples.map((example) => (
              <button
                key={example}
                type="button"
                className="chip"
                disabled={assist.isPending}
                onClick={() => fillPrompt(example)}
              >
                {example}
              </button>
            ))}
          </div>
          <button
            type="submit"
            className="btn primary"
            disabled={assist.isPending || !prompt.trim()}
          >
            <IconSpark />
            {assist.isPending ? t('drafting') : t('draftLayoutBtn')}
          </button>
        </div>
      </form>

      {assist.isError && (
        <div className="banner error">{(assist.error as Error).message}</div>
      )}

      {assist.isSuccess && (
        <p className="ai-explain">{assist.data.explanation}</p>
      )}
    </section>
  )
}
