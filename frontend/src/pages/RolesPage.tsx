import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import type { RoleRecord } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { AuditLine, Field, PermissionPicker } from '../components/AdminFields'
import { IconPlus } from '../components/Icons'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'

const emptyForm = {
  name: '',
  description: '',
  permissionIds: [] as string[],
}

export function RolesPage() {
  const { can } = useAuth()
  const { t } = useLocale()
  const queryClient = useQueryClient()
  const roles = useQuery({ queryKey: ['roles'], queryFn: api.listRoles })
  const permissions = useQuery({
    queryKey: ['permissions'],
    queryFn: api.listPermissions,
    enabled: can('permissions.view'),
  })
  const [filter, setFilter] = useState('')
  const [openId, setOpenId] = useState<string | 'new' | null>(null)
  const [membersRole, setMembersRole] = useState<RoleRecord | null>(null)
  const [form, setForm] = useState(emptyForm)
  const [error, setError] = useState<string | null>(null)

  const canCreate = can('roles.create')
  const canUpdate = can('roles.update')
  const canDelete = can('roles.delete')

  const visible = useMemo(() => {
    const list = roles.data ?? []
    const q = filter.trim().toLowerCase()
    if (!q) {
      return list
    }
    return list.filter((item) =>
      `${item.name} ${item.description ?? ''}`.toLowerCase().includes(q),
    )
  }, [roles.data, filter])

  const save = useMutation({
    mutationFn: async () => {
      const payload = {
        name: form.name,
        description: form.description || undefined,
        permissionIds: form.permissionIds,
      }
      return openId === 'new'
        ? api.createRole(payload)
        : api.updateRole(openId!, payload)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['roles'] })
      setOpenId(null)
      setError(null)
    },
    onError: (err) => setError((err as Error).message),
  })

  function startEdit(role: RoleRecord) {
    setForm({
      name: role.name,
      description: role.description ?? '',
      permissionIds: role.permissionIds,
    })
    setOpenId(role.id)
    setError(null)
  }

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <div className="kicker">{t('identityKicker')}</div>
          <h1>{t('rolesTitle')}</h1>
          <p className="lede">{t('rolesLede')}</p>
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
            {t('newRole')}
          </button>
        )}
      </header>

      {(roles.isError || error) && (
        <div className="banner error">
          {error || (roles.error as Error).message}
        </div>
      )}

      <div className="page-header">
        <h2>{t('roles')}</h2>
        <input
          className="search"
          placeholder={t('filterRoles')}
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
        />
      </div>

      {openId === 'new' && (
        <RoleForm
          title={t('newRole')}
          form={form}
          setForm={setForm}
          permissions={permissions.data ?? []}
          canSave
          pending={save.isPending}
          onCancel={() => setOpenId(null)}
          onSave={() => save.mutate()}
        />
      )}

      {roles.isLoading ? (
        <Spinner page label={t('loadingApp')} />
      ) : (
        <div className="catalog-grid">
          {visible.map((role) => (
            <article
              key={role.id}
              className={`catalog-card ${role.isSystem ? 'enabled' : ''}`}
            >
              <div className="catalog-top">
                <div>
                  <button
                    type="button"
                    className="link"
                    onClick={() =>
                      openId === role.id ? setOpenId(null) : startEdit(role)
                    }
                  >
                    {role.name}
                  </button>
                  <div className="object-name">
                    <button
                      type="button"
                      className="role-members-btn"
                      onClick={() => setMembersRole(role)}
                    >
                      {t('userCount', {
                        n: role.userCount,
                        label: role.userCount === 1 ? t('userSingular') : t('userPlural'),
                      })}
                    </button>
                    {' · '}
                    {t('permissionCount', { n: role.permissions.length })}
                  </div>
                </div>
                {role.isSystem && <span className="badge table">{t('system')}</span>}
              </div>
              <p className="muted small">
                {role.description || t('noDescription')}
              </p>
              {openId === role.id && (
                <RoleForm
                  title={t('editRole')}
                  form={form}
                  setForm={setForm}
                  permissions={permissions.data ?? []}
                  canSave={canUpdate}
                  pending={save.isPending}
                  onCancel={() => setOpenId(null)}
                  onSave={() => save.mutate()}
                  extra={
                    canDelete && !role.isSystem ? (
                      <button
                        type="button"
                        className="btn danger"
                        onClick={async () => {
                          if (!confirm(t('deleteRoleConfirm', { name: role.name }))) {
                            return
                          }
                          try {
                            await api.deleteRole(role.id)
                            queryClient.invalidateQueries({ queryKey: ['roles'] })
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
                  audit={role}
                />
              )}
            </article>
          ))}
        </div>
      )}

      <RoleMembersDialog role={membersRole} onClose={() => setMembersRole(null)} />
    </section>
  )
}

function RoleMembersDialog({
  role,
  onClose,
}: {
  role: RoleRecord | null
  onClose: () => void
}) {
  const { lang, t } = useLocale()
  const [query, setQuery] = useState('')
  const users = useQuery({
    queryKey: ['role-users', role?.id],
    queryFn: () => api.listRoleUsers(role!.id),
    enabled: Boolean(role),
  })

  useEffect(() => {
    if (!role) {
      setQuery('')
      return
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [role, onClose])

  const visible = useMemo(() => {
    const q = query.trim().toLowerCase()
    return (users.data ?? [])
      .filter((user) => {
        if (!q) {
          return true
        }
        const name = localizedText(lang, user.displayName, user.displayNameArabic)
        return `${name} ${user.userName} ${user.email}`.toLowerCase().includes(q)
      })
      .slice()
      .sort((left, right) =>
        localizedText(lang, left.displayName, left.displayNameArabic).localeCompare(
          localizedText(lang, right.displayName, right.displayNameArabic),
          lang,
        ),
      )
  }, [users.data, query, lang])

  if (!role) {
    return null
  }

  return (
    <div className="grants-overlay" onClick={onClose} role="presentation">
      <section
        className="grants-dialog role-members-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="role-members-title"
        onClick={(event) => event.stopPropagation()}
      >
        <header className="grants-chrome">
          <div className="grants-head">
            <div className="grants-copy">
              <div className="kicker">{t('roles')}</div>
              <h2 id="role-members-title">{t('roleMembersTitle', { name: role.name })}</h2>
            </div>
            <button type="button" className="icon-btn" aria-label={t('close')} onClick={onClose}>
              <span aria-hidden="true">×</span>
            </button>
          </div>
          <input
            className="search grants-search"
            placeholder={t('roleMembersFilter')}
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
        </header>
        <div className="grants-body">
          {users.isLoading ? (
            <Spinner label={t('loadingApp')} />
          ) : users.isError ? (
            <div className="banner error">{(users.error as Error).message}</div>
          ) : (users.data ?? []).length === 0 ? (
            <div className="grants-empty">{t('roleMembersEmpty')}</div>
          ) : visible.length === 0 ? (
            <div className="grants-empty">{t('roleMembersNone')}</div>
          ) : (
            <ul className="grants-list">
              {visible.map((user) => {
                const name = localizedText(lang, user.displayName, user.displayNameArabic)
                return (
                  <li key={user.id} className="grants-item">
                    <div>
                      <div className="grants-row-copy">
                        <b>{name || user.userName}</b>
                        <span className="muted small">
                          {user.userName}
                          {user.email ? ` · ${user.email}` : ''}
                        </span>
                      </div>
                      {!user.isActive && <span className="badge">{t('inactive')}</span>}
                    </div>
                  </li>
                )
              })}
            </ul>
          )}
        </div>
      </section>
    </div>
  )
}

function RoleForm({
  title,
  form,
  setForm,
  permissions,
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
  permissions: Parameters<typeof PermissionPicker>[0]['permissions']
  canSave: boolean
  pending: boolean
  onCancel: () => void
  onSave: () => void
  extra?: ReactNode
  audit?: RoleRecord
}) {
  const { t } = useLocale()
  return (
    <div className="admin-form">
      <h3>{title}</h3>
      <div className="admin-grid">
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
      <h3>{t('permissions')}</h3>
      <PermissionPicker
        permissions={permissions}
        selected={form.permissionIds}
        onChange={(permissionIds) => setForm({ ...form, permissionIds })}
      />
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
