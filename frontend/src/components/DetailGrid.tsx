import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { ReportLayout } from '../api/types'
import { detailQueryLayout } from '../lib/detailLayout'
import { useLocale } from '../i18n/LocaleProvider'
import { ResultGrid } from './ResultGrid'

const detailTake = 20

export function DetailGrid({
  reportId,
  layout,
  parentValue,
  lang,
  sendLayout,
}: {
  reportId: string | null
  layout: ReportLayout
  parentValue: string
  lang: string
  sendLayout: boolean
}) {
  const { t } = useLocale()
  const [skip, setSkip] = useState(0)
  const detail = layout.detail
  const query = useQuery({
    queryKey: ['report-detail', reportId, parentValue, skip, lang, sendLayout || !reportId ? detail : 'saved'],
    queryFn: () => {
      if (!detail?.datasetId || !detail.childField) {
        throw new Error(t('detailNotConfigured'))
      }
      if (!reportId) {
        return api.preview({
          datasetId: detail.datasetId,
          layout: detailQueryLayout(detail, parentValue),
          skip,
          take: detailTake,
          lang,
        })
      }
      return api.queryReportDetails(reportId, {
        parentValue,
        skip,
        take: detailTake,
        lang,
        layout: sendLayout ? layout : undefined,
      })
    },
  })

  return (
    <div className="detail-grid">
      <ResultGrid
        result={query.data ?? null}
        loading={query.isFetching}
        error={query.isError ? (query.error as Error).message : null}
        emptyHint={t('detailEmpty')}
        emptyLabel={t('detailEmpty')}
        onPageChange={setSkip}
        fillWidth
        autoFit
      />
    </div>
  )
}
