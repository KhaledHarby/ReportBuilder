import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import type { PermissionRecord } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { AuditLine, Field } from '../components/AdminFields'
import { IconPlus } from '../components/Icons'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'

const emptyForm = { code: '', name: '', description: '' }

export function PermissionsPage() {
  const { can } = useAuth()
  const { t } = useLocale()
  const queryClient = useQueryClient()
  const permissions = useQuery({
    queryKey: ['permissions'],
    queryFn: api.listPermissions,
  })
  const [filter, setFilter] = useState('')
  const [openId, setOpenId] = useState<string | 'new' | null>(null)
  const [form, setForm] = useState(emptyForm)
  const [error, setError] = useState<string | null>(null)

  const canCreate = can('permissions.create')
  const canUpdate = can('permissions.update')
  const canDelete = can('permissions.delete')

  const visible = useMemo(() => {
    const list = permissions.data ?? []
    const q = filter.trim().toLowerCase()
    if (!q) {
      return list
    }
    return list.filter((item) =>
      `${item.code} ${item.name} ${item.description ?? ''}`
        .toLowerCase()
        .includes(q),
    )
  }, [permissions.data, filter])

  const save = useMutation({
    mutationFn: async () => {
      const payload = {
        code: form.code,
        name: form.name,
        description: form.description || undefined,
      }
      return openId === 'new'
        ? api.createPermission(payload)
        : api.updatePermission(openId!, payload)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['permissions'] })
      setOpenId(null)
      setError(null)
    },
    onError: (err) => setError((err as Error).message),
  })

  function startEdit(permission: PermissionRecord) {
    setForm({
      code: permission.code,
      name: permission.name,
      description: permission.description ?? '',
    })
    setOpenId(permission.id)
    setError(null)
  }

  return (
    <section className="page permissions">
      <header className="page-header">
        <div>
          <div className="kicker">{t('identityKicker')}</div>
          <h1>{t('permissionsTitle')}</h1>
          <p className="lede">{t('permissionsLede')}</p>
        </div>
        {canCreate && (
          <button
            type="button"
            className="btn primary"
            onClick={() => {
              setForm(emptyForm)
              setOpenId('new')
            }}
          >
            <IconPlus />
            {t('newPermission')}
          </button>
        )}
      </header>

      {(permissions.isError || error) && (
        <div className="banner error">
          {error || (permissions.error as Error).message}
        </div>
      )}

      <div className="page-header permissions-toolbar">
        <h2>{t('permissions')}</h2>
        <input
          className="search"
          placeholder={t('filterPermissions')}
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
        />
      </div>

      {openId === 'new' && (
        <PermissionForm
          title={t('newPermission')}
          form={form}
          setForm={setForm}
          codeLocked={false}
          canSave
          pending={save.isPending}
          onCancel={() => setOpenId(null)}
          onSave={() => save.mutate()}
        />
      )}

      {permissions.isLoading ? (
        <Spinner page label={t('loadingApp')} />
      ) : (
        <div className="catalog-grid">
          {visible.map((permission) => (
            <article
              key={permission.id}
              className={`catalog-card ${permission.isSystem ? 'enabled' : ''}`}
            >
              <div className="catalog-top">
                <div>
                  <button
                    type="button"
                    className="link"
                    onClick={() =>
                      openId === permission.id
                        ? setOpenId(null)
                        : startEdit(permission)
                    }
                  >
                    {permission.name}
                  </button>
                  <div className="object-name">{permission.code}</div>
                </div>
                {permission.isSystem && <span className="badge table">{t('system')}</span>}
              </div>
              <p className="muted small">
                {permission.description || t('noDescription')}
              </p>
              {openId === permission.id && (
                <PermissionForm
                  title={t('editPermission')}
                  form={form}
                  setForm={setForm}
                  codeLocked={permission.isSystem}
                  canSave={canUpdate}
                  pending={save.isPending}
                  onCancel={() => setOpenId(null)}
                  onSave={() => save.mutate()}
                  extra={
                    canDelete && !permission.isSystem ? (
                      <button
                        type="button"
                        className="btn danger"
                        onClick={async () => {
                          if (!confirm(t('deletePermissionConfirm', { code: permission.code }))) {
                            return
                          }
                          try {
                            await api.deletePermission(permission.id)
                            queryClient.invalidateQueries({
                              queryKey: ['permissions'],
                            })
                            setOpenId(null)
                          } catch (err) {
                            setError((err as Error).message)
                          }
                        }}
                      >
                        {t('delete')}
                      </button>
                    ) : null
                  }
                  audit={permission}
                />
              )}
            </article>
          ))}
        </div>
      )}
    </section>
  )
}

function PermissionForm({
  title,
  form,
  setForm,
  codeLocked,
  canSave,
  pending,
  onCancel,
  onSave,
  extra,
  audit,
}: {
  title: string
  form: typeof emptyForm
  setForm: (form: typeof emptyForm) => void
  codeLocked: boolean
  canSave: boolean
  pending: boolean
  onCancel: () => void
  onSave: () => void
  extra?: ReactNode
  audit?: PermissionRecord
}) {
  const { t } = useLocale()
  return (
    <div className="admin-form">
      <h3>{title}</h3>
      <div className="admin-grid">
        <Field label={t('code')}>
          <input
            value={form.code}
            disabled={codeLocked}
            onChange={(event) => setForm({ ...form, code: event.target.value })}
          />
        </Field>
        <Field label={t('name')}>
          <input
            value={form.name}
            onChange={(event) => setForm({ ...form, name: event.target.value })}
          />
        </Field>
        <Field label={t('description')}>
          <input
            value={form.description}
            onChange={(event) =>
              setForm({ ...form, description: event.target.value })
            }
          />
        </Field>
      </div>
      {audit && <AuditLine {...audit} />}
      <div className="row-actions">
        {canSave && (
          <button
            type="button"
            className="btn primary"
            disabled={pending}
            onClick={onSave}
          >
            {pending ? t('saving') : t('save')}
          </button>
        )}
        <button type="button" className="btn" onClick={onCancel}>
          {t('cancel')}
        </button>
        {extra}
      </div>
    </div>
  )
}
