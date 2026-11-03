import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../api/client'
import type { ReportCategory } from '../api/types'
import { Field } from './AdminFields'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'

export function ReportCategoryManager() {
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const categories = useQuery({
    queryKey: ['report-categories'],
    queryFn: api.listReportCategories,
  })
  const [name, setName] = useState('')
  const [nameArabic, setNameArabic] = useState('')
  const [sortOrder, setSortOrder] = useState(0)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  function resetForm() {
    setName('')
    setNameArabic('')
    setSortOrder((categories.data?.length ?? 0) * 10)
    setEditingId(null)
    setError(null)
  }

  const save = useMutation({
    mutationFn: async () => {
      const trimmed = name.trim()
      if (!trimmed) {
        throw new Error(t('reportCategoryNameRequired'))
      }
      const body = {
        name: trimmed,
        nameArabic: nameArabic.trim(),
        sortOrder,
      }
      return editingId
        ? api.updateReportCategory(editingId, body)
        : api.createReportCategory(body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['report-categories'] })
      await queryClient.invalidateQueries({ queryKey: ['reports'] })
      resetForm()
    },
    onError: (err: Error) => setError(err.message),
  })

  const remove = useMutation({
    mutationFn: api.deleteReportCategory,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['report-categories'] })
      await queryClient.invalidateQueries({ queryKey: ['reports'] })
      if (editingId) {
        resetForm()
      }
    },
    onError: (err: Error) => setError(err.message),
  })

  function startEdit(category: ReportCategory) {
    setEditingId(category.id)
    setName(category.name)
    setNameArabic(category.nameArabic ?? '')
    setSortOrder(category.sortOrder)
    setError(null)
  }

  return (
    <section className="studio-section report-category-manager">
      <div className="studio-section-head">
        <div>
          <h3>{t('reportCategories')}</h3>
          <p className="muted small">{t('reportCategoriesHint')}</p>
        </div>
      </div>
      {error && <p className="banner error">{error}</p>}
      {categories.isError && (
        <p className="banner error">{(categories.error as Error).message}</p>
      )}
      <form
        className="report-category-form"
        onSubmit={(event) => {
          event.preventDefault()
          save.mutate()
        }}
      >
        <Field label={t('reportCategory')}>
          <input
            value={name}
            required
            minLength={2}
            autoComplete="off"
            onChange={(event) => setName(event.target.value)}
          />
        </Field>
        <Field label={t('reportCategoryArabic')}>
          <input
            value={nameArabic}
            dir="rtl"
            lang="ar"
            autoComplete="off"
            onChange={(event) => setNameArabic(event.target.value)}
          />
        </Field>
        <Field label={t('categorySortOrder')}>
          <input
            type="number"
            value={sortOrder}
            onChange={(event) => setSortOrder(Number(event.target.value) || 0)}
          />
        </Field>
        <div className="report-category-form-actions">
          <button type="submit" className="btn primary" disabled={save.isPending}>
            {save.isPending
              ? t('saving')
              : editingId
                ? t('saveCategory')
                : t('addCategory')}
          </button>
          {editingId && (
            <button type="button" className="btn" onClick={resetForm}>
              {t('cancel')}
            </button>
          )}
        </div>
      </form>
      {categories.data?.length ? (
        <ul className="report-category-list">
          {categories.data.map((category) => (
            <li key={category.id}>
              <div>
                <strong>{localizedText(lang, category.name, category.nameArabic)}</strong>
                {lang !== 'ar' && category.nameArabic ? (
                  <span className="muted small"> {category.nameArabic}</span>
                ) : null}
              </div>
              <div className="row-actions">
                <button type="button" className="btn sm" onClick={() => startEdit(category)}>
                  {t('edit')}
                </button>
                <button
                  type="button"
                  className="btn sm danger"
                  disabled={remove.isPending}
                  onClick={() => {
                    const label = localizedText(lang, category.name, category.nameArabic)
                    if (window.confirm(t('deleteCategoryConfirm', { name: label }))) {
                      remove.mutate(category.id)
                    }
                  }}
                >
                  {t('delete')}
                </button>
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <p className="muted small studio-section-empty">{t('noCategoriesYet')}</p>
      )}
    </section>
  )
}
