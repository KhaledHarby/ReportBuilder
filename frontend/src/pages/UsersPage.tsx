import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import type { UserAccount } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { setStoredUser } from '../auth/session'
import { AuditLine, Field, PermissionPicker } from '../components/AdminFields'
import { IconGrid, IconList, IconPlus, IconUsers } from '../components/Icons'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'
import { localizedText } from '../i18n/localized'

type UsersView = 'card' | 'list'

const VIEW_KEY = 'rb.usersView'

function readUsersView(): UsersView {
  return localStorage.getItem(VIEW_KEY) === 'list' ? 'list' : 'card'
}

const emptyForm = {
  userName: '',
  email: '',
  displayName: '',
  displayNameArabic: '',
  password: '',
  isActive: true,
  roleIds: [] as string[],
  permissionIds: [] as string[],
}

export function UsersPage() {
  const { user: current, can } = useAuth()
  const { lang, t } = useLocale()
  const queryClient = useQueryClient()
  const users = useQuery({ queryKey: ['users'], queryFn: api.listUsers })
  const roles = useQuery({
    queryKey: ['roles'],
    queryFn: api.listRoles,
    enabled: can('roles.view'),
  })
  const permissions = useQuery({
    queryKey: ['permissions'],
    queryFn: api.listPermissions,
    enabled: can('permissions.view'),
  })
  const [filter, setFilter] = useState('')
  const [usersView, setUsersView] = useState<UsersView>(readUsersView)

  useEffect(() => {
    localStorage.setItem(VIEW_KEY, usersView)
  }, [usersView])
  const [openId, setOpenId] = useState<string | 'new' | null>(null)
  const [form, setForm] = useState(emptyForm)
  const [error, setError] = useState<string | null>(null)

  const canCreate = can('users.create')
  const canUpdate = can('users.update')
  const canDelete = can('users.delete')
  const canActivate = can('users.activate')
  const canAssign =
    (can('users.update') || can('assignments.manage')) &&
    can('roles.view') &&
    can('permissions.view')

  const sourceCounts = useMemo(() => {
    const counts = new Map<string, number>()
    for (const user of users.data ?? []) {
      const source = user.externalSource?.trim() || t('userSourceLocal')
      counts.set(source, (counts.get(source) ?? 0) + 1)
    }
    return [...counts.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
  }, [users.data, t])

  const visible = useMemo(() => {
    const list = users.data ?? []
    const q = filter.trim().toLowerCase()
    if (!q) {
      return list
    }
    return list.filter((item) =>
      `${item.displayName} ${item.displayNameArabic ?? ''} ${item.userName} ${item.email} ${item.externalSource ?? ''} ${item.roles.join(' ')}`
        .toLowerCase()
        .includes(q),
    )
  }, [users.data, filter])

  const save = useMutation({
    mutationFn: async () => {
      const payload = {
        userName: form.userName,
        email: form.email,
        displayName: form.displayName,
        displayNameArabic: form.displayNameArabic,
        password: form.password || undefined,
        isActive: form.isActive,
        roleIds: form.roleIds,
        permissionIds: form.permissionIds,
      }
      return openId === 'new'
        ? api.createUser(payload)
        : api.updateUser(openId!, payload)
    },
    onSuccess: (saved) => {
      queryClient.invalidateQueries({ queryKey: ['users'] })
      if (saved.id === current?.id) {
        setStoredUser(saved)
      }
      setOpenId(null)
      setError(null)
    },
    onError: (err) => setError((err as Error).message),
  })

  function startCreate() {
    setForm(emptyForm)
    setOpenId('new')
    setError(null)
  }

  function startEdit(user: UserAccount) {
    setForm({
      userName: user.userName,
      email: user.email,
      displayName: user.displayName,
      displayNameArabic: user.displayNameArabic ?? '',
      password: '',
      isActive: user.isActive,
      roleIds: user.roleIds,
      permissionIds: user.directPermissionIds,
    })
    setOpenId(user.id)
    setError(null)
  }

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <div className="kicker">{t('identityKicker')}</div>
          <h1>{t('usersTitle')}</h1>
          <p className="lede">{t('usersLede')}</p>
        </div>
        {canCreate && (
          <button type="button" className="btn primary" onClick={startCreate}>
            <IconPlus />
            {t('newUser')}
          </button>
        )}
      </header>

      <div className="stats user-stats">
        <div className="stat">
          <span className="muted small">{t('usersTotal')}</span>
          <b>{users.data?.length ?? 0}</b>
        </div>
        {sourceCounts.map(([source, count]) => (
          <div className="stat" key={source}>
            <span className="muted small">{source}</span>
            <b>{count}</b>
          </div>
        ))}
      </div>

      {(users.isError || error) && (
        <div className="banner error">
          {error || (users.error as Error).message}
        </div>
      )}

      <div className="page-header">
        <h2>{t('users')}</h2>
        <div className="studio-head-tools">
          <input
            className="search"
            placeholder={t('filterPeople')}
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
          />
          <div className="view-toggle" role="group" aria-label={t('usersView')}>
            <button
              type="button"
              className={usersView === 'card' ? 'active' : ''}
              aria-pressed={usersView === 'card'}
              title={t('viewCards')}
              onClick={() => setUsersView('card')}
            >
              <IconGrid />
              {t('viewCards')}
            </button>
            <button
              type="button"
              className={usersView === 'list' ? 'active' : ''}
              aria-pressed={usersView === 'list'}
              title={t('viewList')}
              onClick={() => setUsersView('list')}
            >
              <IconList />
              {t('viewList')}
            </button>
          </div>
        </div>
      </div>

      {openId === 'new' && (
        <UserForm
          title={t('newUser')}
          form={form}
          setForm={setForm}
          roles={roles.data ?? []}
          permissions={permissions.data ?? []}
          canAssign={canAssign}
          requirePassword
          pending={save.isPending}
          onCancel={() => setOpenId(null)}
          onSave={() => save.mutate()}
        />
      )}

      {users.isLoading ? (
        <Spinner page label={t('loadingApp')} />
      ) : (
        <div className={usersView === 'list' ? 'studio-list' : 'catalog-grid'}>
          {visible.map((user) => (
            <article
              key={user.id}
              className={
                usersView === 'list'
                  ? `studio-list-row user-list-row ${user.isActive ? '' : 'draft'}`
                  : `catalog-card ${user.isActive ? 'enabled' : ''}`
              }
            >
              <div className={usersView === 'list' ? 'studio-list-copy' : 'catalog-top'}>
                <div>
                  <button
                    type="button"
                    className="link"
                    onClick={() =>
                      openId === user.id ? setOpenId(null) : startEdit(user)
                    }
                  >
                    {localizedText(lang, user.displayName, user.displayNameArabic)}
                  </button>
                  <div className="object-name">
                    {user.userName} · {user.email}
                  </div>
                </div>
                {usersView === 'card' && (
                  <div className="catalog-meta">
                    {user.externalSource && (
                      <span className="badge published" title={t('fromSource', { source: user.externalSource })}>
                        {user.externalSource}
                      </span>
                    )}
                    <span className={`badge ${user.isActive ? 'table' : ''}`}>
                      {user.isActive ? t('active') : t('inactive')}
                    </span>
                  </div>
                )}
              </div>
              {usersView === 'card' ? (
                <p className="muted small">{user.roles.join(', ') || t('noRoles')}</p>
              ) : (
                <>
                  <span className="muted small user-list-roles">
                    {user.roles.join(', ') || t('noRoles')}
                  </span>
                  <div className="studio-list-meta">
                    {user.externalSource && (
                      <span className="badge published" title={t('fromSource', { source: user.externalSource })}>
                        {user.externalSource}
                      </span>
                    )}
                    <span className={`badge ${user.isActive ? 'table' : ''}`}>
                      {user.isActive ? t('active') : t('inactive')}
                    </span>
                  </div>
                </>
              )}
              {openId === user.id && (
                <UserForm
                  title={t('editUser')}
                  form={form}
                  setForm={setForm}
                  roles={roles.data ?? []}
                  permissions={permissions.data ?? []}
                  canAssign={canAssign}
                  canSave={canUpdate}
                  requirePassword={false}
                  pending={save.isPending}
                  onCancel={() => setOpenId(null)}
                  onSave={() => save.mutate()}
                  extra={
                    <div className="row-actions">
                      {canActivate && current?.id !== user.id && (
                        <button
                          type="button"
                          className="btn"
                          onClick={async () => {
                            try {
                              await api.activateUser(user.id, !user.isActive)
                              queryClient.invalidateQueries({ queryKey: ['users'] })
                            } catch (err) {
                              setError((err as Error).message)
                            }
                          }}
                        >
                          {user.isActive ? t('deactivate') : t('activate')}
                        </button>
                      )}
                      {canDelete && current?.id !== user.id && (
                        <button
                          type="button"
                          className="btn danger"
                          onClick={async () => {
                            if (
                              !confirm(
                                t('deleteUserConfirm', {
                                  name: localizedText(lang, user.displayName, user.displayNameArabic),
                                }),
                              )
                            ) {
                              return
                            }
                            try {
                              await api.deleteUser(user.id)
                              queryClient.invalidateQueries({ queryKey: ['users'] })
                              setOpenId(null)
                            } catch (err) {
                              setError((err as Error).message)
                            }
                          }}
                        >
                          {t('delete')}
                        </button>
                      )}
                    </div>
                  }
                  audit={user}
                />
              )}
            </article>
          ))}
          {!visible.length && (
            <div className="empty">
              <IconUsers />
              <h2>{t('noUsersMatch')}</h2>
              <p>{t('noUsersHint')}</p>
            </div>
          )}
        </div>
      )}
    </section>
  )
}

function UserForm({
  title,
  form,
  setForm,
  roles,
  permissions,
  canAssign,
  canSave = true,
  requirePassword,
  pending,
  onCancel,
  onSave,
  extra,
  audit,
}: {
  title: string
  form: typeof emptyForm
  setForm: (form: typeof emptyForm) => void
  roles: { id: string; name: string; description?: string | null }[]
  permissions: Parameters<typeof PermissionPicker>[0]['permissions']
  canAssign: boolean
  canSave?: boolean
  requirePassword: boolean
  pending: boolean
  onCancel: () => void
  onSave: () => void
  extra?: ReactNode
  audit?: UserAccount
}) {
  const { t } = useLocale()
  return (
    <div className="admin-form">
      <h3>{title}</h3>
      <div className="admin-grid">
        <Field label={t('userName')}>
          <input
            value={form.userName}
            disabled={!requirePassword}
            onChange={(event) =>
              setForm({ ...form, userName: event.target.value })
            }
          />
        </Field>
        <Field label={t('email')}>
          <input
            type="email"
            value={form.email}
            onChange={(event) => setForm({ ...form, email: event.target.value })}
          />
        </Field>
        <Field label={t('displayName')}>
          <input
            value={form.displayName}
            onChange={(event) =>
              setForm({ ...form, displayName: event.target.value })
            }
          />
        </Field>
        <Field label={t('displayNameArabic')}>
          <input
            dir="rtl"
            lang="ar"
            value={form.displayNameArabic}
            onChange={(event) =>
              setForm({ ...form, displayNameArabic: event.target.value })
            }
          />
        </Field>
        <Field label={requirePassword ? t('password') : t('newPasswordOptional')}>
          <input
            type="password"
            value={form.password}
            onChange={(event) =>
              setForm({ ...form, password: event.target.value })
            }
          />
        </Field>
      </div>
      {audit?.externalSource && (
        <p className="muted small">{t('fromSource', { source: audit.externalSource })}</p>
      )}
      {canAssign && (
        <>
          <h3>{t('roles')}</h3>
          <div className="check-grid">
            {roles.map((role) => (
              <label key={role.id} className="check-item">
                <input
                  type="checkbox"
                  checked={form.roleIds.includes(role.id)}
                  onChange={() =>
                    setForm({
                      ...form,
                      roleIds: form.roleIds.includes(role.id)
                        ? form.roleIds.filter((id) => id !== role.id)
                        : [...form.roleIds, role.id],
                    })
                  }
                />
                <span>
                  <b>{role.name}</b>
                  {role.description ? <small>{role.description}</small> : null}
                </span>
              </label>
            ))}
          </div>
          <h3>{t('directPermissions')}</h3>
          <p className="muted small">{t('directPermissionsHint')}</p>
          <PermissionPicker
            permissions={permissions}
            selected={form.permissionIds}
            onChange={(permissionIds) => setForm({ ...form, permissionIds })}
          />
        </>
      )}
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
