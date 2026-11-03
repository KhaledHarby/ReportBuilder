import { useEffect, useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { CacheProviderConnection, CacheSettings } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { Field } from '../components/AdminFields'
import { AiSettingsPanel } from '../components/AiSettingsPanel'
import { SmtpSettingsPanel } from '../components/SmtpSettingsPanel'
import { EmailTemplateSettingsPanel } from '../components/EmailTemplateSettingsPanel'
import { GeneralSettingsPanel } from '../components/GeneralSettingsPanel'
import { PasswordInput } from '../components/PasswordInput'
import { Spinner } from '../components/Spinner'
import { useLocale } from '../i18n/LocaleProvider'
import type { MessageKey } from '../i18n/messages'

type ProviderKey = 'ClickHouse' | 'Timescale' | 'Redis' | 'Postgres' | 'Mongo'

const comingSoon: ProviderKey[] = ['Timescale']

const emptyConnection = (port: number, database = '', username = ''): CacheProviderConnection => ({
  host: '',
  port,
  database,
  username,
  password: '',
  secure: false,
  connectionString: '',
  hasPassword: false,
})

function cloneSettings(data: CacheSettings): CacheSettings {
  return structuredClone(data)
}

export function SettingsPage() {
  const { can } = useAuth()
  const { t } = useLocale()
  const queryClient = useQueryClient()
  const canUpdate = can('settings.update')
  const settings = useQuery({ queryKey: ['cache-settings'], queryFn: api.getCacheSettings })
  const [tab, setTab] = useState<'general' | 'cache' | 'smtp' | 'email' | 'ai'>('general')
  const [form, setForm] = useState<CacheSettings | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [testing, setTesting] = useState<string | null>(null)

  useEffect(() => {
    if (settings.data) {
      setForm(cloneSettings(settings.data))
    }
  }, [settings.data])

  const save = useMutation({
    mutationFn: async () => {
      if (!form) {
        throw new Error('Settings are not loaded.')
      }
      const payload = {
        ...form,
        clickHouse: normalizeClickHouseConnection(form.clickHouse),
        redis: normalizeRedisConnection(form.redis),
        mongo: normalizeMongoConnection(form.mongo),
        postgres: normalizePostgresConnection(form.postgres),
      }
      setForm(payload)
      return api.updateCacheSettings(payload)
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['cache-settings'], data)
      setForm(cloneSettings(data))
      setError(null)
      setNotice(t('settingsSaved'))
    },
    onError: (err: Error) => {
      setNotice(null)
      setError(err.message)
    },
  })

  async function testProvider(provider: string, connection?: CacheProviderConnection) {
    const normalized =
      provider === 'ClickHouse' && connection
        ? normalizeClickHouseConnection(connection)
        : provider === 'Redis' && connection
          ? normalizeRedisConnection(connection)
          : provider === 'Mongo' && connection
          ? normalizeMongoConnection(connection)
          : provider === 'Postgres' && connection
            ? normalizePostgresConnection(connection)
            : connection
    if (provider === 'ClickHouse' && form && normalized) {
      setForm({ ...form, clickHouse: normalized })
    }
    if (provider === 'Redis' && form && normalized) {
      setForm({ ...form, redis: normalized })
    }
    if (provider === 'Mongo' && form && normalized) {
      setForm({ ...form, mongo: normalized })
    }
    if (provider === 'Postgres' && form && normalized) {
      setForm({ ...form, postgres: normalized })
    }
    setTesting(provider)
    setError(null)
    setNotice(null)
    try {
      await api.testCacheSettings(provider, normalized)
      setNotice(t('settingsTestOk', { provider: providerLabel(provider, t) }))
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setTesting(null)
    }
  }

  return (
    <section className="page settings">
      <header className="page-header">
        <div>
          <div className="kicker">{t('settingsKicker')}</div>
          <h1>{t('navSettings')}</h1>
          <p className="lede">
            {tab === 'general'
              ? t('generalHint')
              : tab === 'cache'
                ? t('settingsLede')
                : tab === 'smtp'
                  ? t('smtpHint')
                  : tab === 'ai'
                    ? t('aiSettingsHint')
                    : t('emailTemplateHint')}
          </p>
        </div>
        {tab === 'cache' && canUpdate && form && (
          <button
            type="button"
            className="btn primary"
            disabled={save.isPending}
            onClick={() => save.mutate()}
          >
            {save.isPending ? t('saving') : t('saveSettings')}
          </button>
        )}
      </header>

      <div className="status-tabs settings-tabs" role="tablist" aria-label={t('navSettings')}>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'general'}
          className={tab === 'general' ? 'active' : ''}
          onClick={() => setTab('general')}
        >
          {t('settingsTabGeneral')}
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'cache'}
          className={tab === 'cache' ? 'active' : ''}
          onClick={() => setTab('cache')}
        >
          {t('settingsTabCache')}
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'smtp'}
          className={tab === 'smtp' ? 'active' : ''}
          onClick={() => setTab('smtp')}
        >
          {t('settingsTabSmtp')}
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'email'}
          className={tab === 'email' ? 'active' : ''}
          onClick={() => setTab('email')}
        >
          {t('settingsTabEmail')}
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'ai'}
          className={tab === 'ai' ? 'active' : ''}
          onClick={() => setTab('ai')}
        >
          {t('settingsTabAi')}
        </button>
      </div>

      {tab === 'general' ? (
        <GeneralSettingsPanel />
      ) : tab === 'smtp' ? (
        <SmtpSettingsPanel />
      ) : tab === 'ai' ? (
        <AiSettingsPanel />
      ) : tab === 'email' ? (
        <EmailTemplateSettingsPanel />
      ) : settings.isLoading || !form ? (
        <Spinner page label={t('loadingApp')} />
      ) : (
        <>
      {error && <p className="banner error">{error}</p>}
      {notice && <p className="banner">{notice}</p>}

      <div className="settings-grid">
        <article className="settings-card">
          <div className="settings-card-head">
            <div>
              <h2>{t('settingsDefaultProvider')}</h2>
              <p className="muted small">{t('settingsDefaultHint')}</p>
            </div>
          </div>
          <Field label={t('settingsDefaultProvider')}>
            <select
              value={form.defaultProvider}
              disabled={!canUpdate}
              onChange={(event) =>
                setForm({ ...form, defaultProvider: event.target.value })
              }
            >
              <option value="SqlServer">{t('settingsProviderSqlServer')}</option>
              <option value="ClickHouse">{t('settingsProviderClickHouse')}</option>
              <option value="Redis">{t('settingsProviderRedis')}</option>
              <option value="Postgres">{t('settingsProviderPostgres')}</option>
              <option value="Mongo">{t('settingsProviderMongo')}</option>
            </select>
          </Field>
        </article>

        <ProviderCard
          title={t('settingsProviderSqlServer')}
          hint={t('settingsSqlServerHint')}
          implemented
          selected={form.defaultProvider === 'SqlServer'}
        >
          {canUpdate && (
            <button type="button" className="btn" onClick={() => void testProvider('SqlServer')}>
              {testing === 'SqlServer' ? t('settingsTesting') : t('settingsTest')}
            </button>
          )}
        </ProviderCard>

        <ConnectionCard
          title={t('settingsProviderClickHouse')}
          hint={t('settingsClickHouseHint')}
          implemented
          selected={form.defaultProvider === 'ClickHouse'}
          connection={form.clickHouse}
          canUpdate={canUpdate}
          testing={testing === 'ClickHouse'}
          parseHost={normalizeClickHouseConnection}
          onChange={(clickHouse) => setForm({ ...form, clickHouse })}
          onTest={() => void testProvider('ClickHouse', form.clickHouse)}
        />

        <ConnectionCard
          title={t('settingsProviderRedis')}
          hint={t('settingsRedisHint')}
          implemented
          selected={form.defaultProvider === 'Redis'}
          connection={form.redis}
          canUpdate={canUpdate}
          testing={testing === 'Redis'}
          redis
          parseHost={normalizeRedisConnection}
          onChange={(redis) => setForm({ ...form, redis })}
          onTest={() => void testProvider('Redis', form.redis)}
        />

        <ConnectionCard
          title={t('settingsProviderMongo')}
          hint={t('settingsMongoHint')}
          implemented
          selected={form.defaultProvider === 'Mongo'}
          connection={form.mongo}
          canUpdate={canUpdate}
          testing={testing === 'Mongo'}
          mongo
          parseHost={normalizeMongoConnection}
          onChange={(mongo) => setForm({ ...form, mongo })}
          onTest={() => void testProvider('Mongo', form.mongo)}
        />

        <ConnectionCard
          title={t('settingsProviderPostgres')}
          hint={t('settingsPostgresHint')}
          implemented
          selected={form.defaultProvider === 'Postgres'}
          connection={form.postgres}
          canUpdate={canUpdate}
          testing={testing === 'Postgres'}
          postgres
          parseHost={normalizePostgresConnection}
          onChange={(postgres) => setForm({ ...form, postgres })}
          onTest={() => void testProvider('Postgres', form.postgres)}
        />

        {comingSoon.map((key) => (
          <ConnectionCard
            key={key}
            title={providerLabel(key, t)}
            hint={t('settingsComingSoonHint')}
            implemented={false}
            selected={false}
            connection={connectionOf(form, key)}
            canUpdate={false}
            testing={false}
            redis={key === 'Redis'}
            mongo={key === 'Mongo'}
            onChange={(connection) => setForm({ ...form, ...assignConnection(key, connection) })}
            onTest={() => undefined}
          />
        ))}
      </div>
        </>
      )}
    </section>
  )
}

function normalizeClickHouseConnection(connection: CacheProviderConnection): CacheProviderConnection {
  const next = { ...connection }
  let host = (next.host ?? '').trim()
  try {
    if (/^https?:\/\//i.test(host)) {
      const uri = new URL(host)
      next.secure = uri.protocol === 'https:'
      next.host = uri.hostname
      if (uri.port) {
        next.port = Number(uri.port)
      } else if (next.secure && (!next.port || next.port === 8123)) {
        next.port = 8443
      }
      host = next.host
    }
  } catch {
    /* keep the typed value */
  }

  const hostPort = host.match(/^([^:/]+):(\d+)$/)
  if (hostPort) {
    next.host = hostPort[1]
    next.port = Number(hostPort[2])
    host = next.host
  }

  if (host.toLowerCase().includes('clickhouse.cloud')) {
    next.secure = true
    if (!next.port || next.port === 8123) {
      next.port = 8443
    }
    if (!next.database?.trim()) {
      next.database = 'default'
    }
    if (!next.username?.trim()) {
      next.username = 'default'
    }
  }

  return next
}

function normalizeRedisConnection(connection: CacheProviderConnection): CacheProviderConnection {
  const next = { ...connection }
  let host = (next.host ?? '').trim()
  try {
    if (/^rediss?:\/\//i.test(host)) {
      const uri = new URL(host)
      next.secure = uri.protocol === 'rediss:'
      next.host = uri.hostname
      if (uri.port) {
        next.port = Number(uri.port)
      }
      if (uri.username) {
        next.username = decodeURIComponent(uri.username)
      }
      if (uri.password) {
        next.password = decodeURIComponent(uri.password)
      }
      const db = uri.pathname.replace(/^\//, '')
      if (db && !Number.isNaN(Number(db))) {
        next.redisDatabase = Number(db)
      }
      host = next.host
    }
  } catch {
    /* keep the typed value */
  }

  const hostPort = host.match(/^([^:/]+):(\d+)$/)
  if (hostPort) {
    next.host = hostPort[1]
    next.port = Number(hostPort[2])
    host = next.host
  }

  const azure =
    host.toLowerCase().includes('redis.cache.windows.net') ||
    host.toLowerCase().includes('redis.azure.net')
  if (azure) {
    next.secure = true
    if (!next.port || next.port === 6379) {
      next.port = 6380
    }
  }

  if (next.redisDatabase == null || next.redisDatabase < 0 || next.redisDatabase > 15) {
    next.redisDatabase = 0
  }

  return next
}

function normalizeMongoConnection(connection: CacheProviderConnection): CacheProviderConnection {
  const next = { ...connection }
  const raw = (next.connectionString || next.host || '').trim()
  const mongoUrl = raw.match(
    /^(mongodb(?:\+srv)?):\/\/(?:([^:@/]*)(?::([^@/]*))?@)?([^/?#:]+)(?::(\d+))?/i,
  )
  if (mongoUrl) {
    const scheme = mongoUrl[1].toLowerCase()
    next.secure = scheme === 'mongodb+srv' || next.secure
    next.host = mongoUrl[4]
    if (mongoUrl[5]) {
      next.port = Number(mongoUrl[5])
    } else if (scheme === 'mongodb+srv') {
      next.port = 0
    } else if (!next.port) {
      next.port = 27017
    }
    if (mongoUrl[2]) {
      next.username = decodeURIComponent(mongoUrl[2])
    }
    if (mongoUrl[3]) {
      next.password = decodeURIComponent(mongoUrl[3])
    }
    try {
      const uri = new URL(raw.replace(/^mongodb(\+srv)?:/i, 'https:'))
      const db = uri.pathname.replace(/^\//, '').split('/')[0]
      const existing = (connection.database ?? '').trim()
      if (db) {
        const fromUri = decodeURIComponent(db)
        // Keep ReportBuilder when the URI only has /reportbuilder (Windows rejects both).
        next.database =
          existing && existing.toLowerCase() === fromUri.toLowerCase() ? existing : fromUri
      }
    } catch {
      /* host/user already parsed */
    }
    next.connectionString = raw
  } else {
    const hostPort = (next.host ?? '').trim().match(/^([^:/]+):(\d+)$/)
    if (hostPort) {
      next.host = hostPort[1]
      next.port = Number(hostPort[2])
    }
  }

  if ((next.host ?? '').toLowerCase().includes('mongodb.net')) {
    next.secure = true
  }
  if (!next.database?.trim()) {
    next.database = 'reportbuilder'
  }
  if (!next.port && !(next.host ?? '').toLowerCase().includes('mongodb.net')) {
    next.port = 27017
  }
  const databaseName = (next.database ?? '').trim()
  if (looksLikeMongoUrl(next.connectionString) && databaseName) {
    next.connectionString = replaceMongoDatabase(next.connectionString, databaseName)
  }

  return next
}

function looksLikeMongoUrl(value: string | undefined) {
  return !!value && /^(mongodb(?:\+srv)?):\/\//i.test(value)
}

function replaceMongoDatabase(raw: string, database: string) {
  const match = raw.match(/^(mongodb(?:\+srv)?:\/\/[^/?]+)(?:\/[^?]*)?(\?.*)?$/i)
  if (!match) {
    return raw
  }

  return `${match[1]}/${encodeURIComponent(database)}${match[2] ?? ''}`
}

function normalizePostgresConnection(connection: CacheProviderConnection): CacheProviderConnection {
  const next = { ...connection }
  const raw = (next.connectionString || next.host || '').trim()
  const postgresUrl = raw.match(
    /^(postgres(?:ql)?):\/\/(?:([^:@/]*)(?::([^@/]*))?@)?([^/?#:]+)(?::(\d+))?/i,
  )
  if (postgresUrl) {
    next.host = postgresUrl[4]
    if (postgresUrl[5]) {
      next.port = Number(postgresUrl[5])
    }
    if (postgresUrl[2]) {
      next.username = decodeURIComponent(postgresUrl[2])
    }
    if (postgresUrl[3]) {
      next.password = decodeURIComponent(postgresUrl[3])
    }
    try {
      const uri = new URL(raw.replace(/^postgres(?:ql)?:/i, 'https:'))
      const db = uri.pathname.replace(/^\//, '').split('/')[0]
      if (db) {
        next.database = decodeURIComponent(db)
      }
      if (uri.searchParams.get('sslmode') && uri.searchParams.get('sslmode') !== 'disable') {
        next.secure = true
      }
    } catch {
      /* host/user already parsed */
    }
    next.connectionString = raw
  } else {
    const hostPort = (next.host ?? '').trim().match(/^([^:/]+):(\d+)$/)
    if (hostPort) {
      next.host = hostPort[1]
      next.port = Number(hostPort[2])
    }
  }

  const host = (next.host ?? '').toLowerCase()
  if (
    host.includes('neon.tech') ||
    host.includes('supabase.co') ||
    host.includes('timescale.cloud') ||
    host.includes('rds.amazonaws.com')
  ) {
    next.secure = true
  }
  if (!next.database?.trim()) {
    next.database = 'reportbuilder'
  }
  if (!next.port) {
    next.port = 5432
  }

  return next
}

function connectionOf(settings: CacheSettings, key: ProviderKey): CacheProviderConnection {
  switch (key) {
    case 'ClickHouse':
      return settings.clickHouse
    case 'Timescale':
      return settings.timescale
    case 'Redis':
      return settings.redis
    case 'Postgres':
      return settings.postgres
    case 'Mongo':
      return settings.mongo
  }
}

function assignConnection(
  key: ProviderKey,
  connection: CacheProviderConnection,
): Partial<CacheSettings> {
  switch (key) {
    case 'ClickHouse':
      return { clickHouse: connection }
    case 'Timescale':
      return { timescale: connection }
    case 'Redis':
      return { redis: connection }
    case 'Postgres':
      return { postgres: connection }
    case 'Mongo':
      return { mongo: connection }
  }
}

function providerLabel(provider: string, t: (key: MessageKey) => string) {
  switch (provider) {
    case 'SqlServer':
      return t('settingsProviderSqlServer')
    case 'ClickHouse':
      return t('settingsProviderClickHouse')
    case 'Timescale':
      return t('settingsProviderTimescale')
    case 'Redis':
      return t('settingsProviderRedis')
    case 'Postgres':
      return t('settingsProviderPostgres')
    case 'Mongo':
      return t('settingsProviderMongo')
    default:
      return provider
  }
}

function ProviderCard({
  title,
  hint,
  implemented,
  selected,
  children,
}: {
  title: string
  hint: string
  implemented: boolean
  selected: boolean
  children?: ReactNode
}) {
  const { t } = useLocale()
  return (
    <article className={`settings-card${implemented ? '' : ' is-soon'}${selected ? ' is-selected' : ''}`}>
      <div className="settings-card-head">
        <div>
          <h2>{title}</h2>
          <p className="muted small">{hint}</p>
        </div>
        <span className={`badge ${implemented ? (selected ? 'published' : 'table') : ''}`}>
          {implemented
            ? selected
              ? t('settingsSelected')
              : t('settingsReady')
            : t('settingsComingSoon')}
        </span>
      </div>
      {children}
    </article>
  )
}

function ConnectionCard({
  title,
  hint,
  implemented,
  selected,
  connection,
  canUpdate,
  testing,
  redis,
  mongo,
  postgres,
  parseHost,
  onChange,
  onTest,
}: {
  title: string
  hint: string
  implemented: boolean
  selected: boolean
  connection: CacheProviderConnection
  canUpdate: boolean
  testing: boolean
  redis?: boolean
  mongo?: boolean
  postgres?: boolean
  parseHost?: (connection: CacheProviderConnection) => CacheProviderConnection
  onChange: (connection: CacheProviderConnection) => void
  onTest: () => void
}) {
  const { t } = useLocale()
  const disabled = !canUpdate || !implemented
  const value = connection ?? emptyConnection(0)

  function patch(partial: Partial<CacheProviderConnection>) {
    onChange({ ...value, ...partial })
  }

  return (
    <ProviderCard title={title} hint={hint} implemented={implemented} selected={selected}>
      <div className="settings-fields">
        <Field label={t('settingsHost')}>
          <input
            value={value.host}
            disabled={disabled}
            autoComplete="off"
            placeholder={
              parseHost
                ? redis
                  ? 'your-cache.redis.cache.windows.net'
                  : mongo
                    ? 'cluster0.xxxxx.mongodb.net'
                    : postgres
                      ? 'ep-xxx.region.aws.neon.tech'
                      : 'your-service.region.azure.clickhouse.cloud'
                : undefined
            }
            onChange={(event) => patch({ host: event.target.value })}
            onBlur={() => {
              if (parseHost) {
                onChange(parseHost(value))
              }
            }}
          />
        </Field>
        <Field label={t('settingsPort')}>
          <input
            type="number"
            value={value.port || ''}
            disabled={disabled}
            onChange={(event) => patch({ port: Number(event.target.value) || 0 })}
          />
        </Field>
        {!redis && (
          <Field label={t('settingsDatabase')}>
            <input
              value={value.database}
              disabled={disabled}
              autoComplete="off"
              onChange={(event) => patch({ database: event.target.value })}
            />
          </Field>
        )}
        {redis && (
          <Field label={t('settingsRedisDatabase')}>
            <input
              type="number"
              value={value.redisDatabase ?? 0}
              disabled={disabled}
              onChange={(event) => patch({ redisDatabase: Number(event.target.value) || 0 })}
            />
          </Field>
        )}
        <Field label={t('settingsUsername')}>
          <input
            value={value.username}
            disabled={disabled}
            autoComplete="off"
            onChange={(event) => patch({ username: event.target.value })}
          />
        </Field>
        <Field
          label={
            value.hasPassword ? t('settingsPasswordKeep') : t('settingsPassword')
          }
        >
          <PasswordInput
            value={value.password}
            disabled={disabled}
            autoComplete="new-password"
            placeholder={value.hasPassword ? t('settingsPasswordPlaceholder') : ''}
            onChange={(event) => patch({ password: event.target.value })}
          />
        </Field>
        <label className="field checkbox">
          <span>{t('settingsSecure')}</span>
          <input
            type="checkbox"
            checked={value.secure}
            disabled={disabled}
            onChange={(event) => patch({ secure: event.target.checked })}
          />
        </label>
        {(mongo || postgres) && (
          <Field label={t('settingsConnectionString')}>
            <input
              value={value.connectionString}
              disabled={disabled}
              autoComplete="off"
              placeholder={
                postgres
                  ? 'postgresql://user:pass@host:5432/reportbuilder'
                  : 'mongodb+srv://user:pass@cluster.mongodb.net'
              }
              onChange={(event) => patch({ connectionString: event.target.value })}
              onBlur={() => {
                if (parseHost) {
                  onChange(parseHost(value))
                }
              }}
            />
          </Field>
        )}
      </div>
      {implemented && canUpdate && (
        <div className="row-actions">
          <button type="button" className="btn" disabled={testing} onClick={onTest}>
            {testing ? t('settingsTesting') : t('settingsTest')}
          </button>
        </div>
      )}
    </ProviderCard>
  )
}
