import type { DatasetColumn, UserScopeProperty } from '../api/types'
import type { MessageKey } from '../i18n/messages'

const GUID_TYPES = new Set(['uniqueidentifier'])
const INTEGER_TYPES = new Set(['int', 'bigint', 'smallint', 'tinyint'])

export function scheduleScopeProperties(sqlType: string): UserScopeProperty[] {
  return propertiesForSqlType(sqlType).filter((property) => property !== 'displayName')
}

export function propertiesForSqlType(sqlType: string): UserScopeProperty[] {
  const type = sqlType.toLowerCase()
  if (GUID_TYPES.has(type)) {
    return ['id']
  }
  if (INTEGER_TYPES.has(type)) {
    return []
  }
  return ['id', 'userName', 'email', 'displayName']
}

export function defaultPropertyForSqlType(sqlType: string): UserScopeProperty {
  const options = propertiesForSqlType(sqlType)
  return options[0] ?? 'id'
}

export function userScopePropertyLabelKey(property: UserScopeProperty): MessageKey {
  switch (property) {
    case 'id':
      return 'userScopePropertyId'
    case 'userName':
      return 'userScopePropertyUserName'
    case 'email':
      return 'userScopePropertyEmail'
    case 'displayName':
      return 'userScopePropertyDisplayName'
    default:
      return 'userScopePropertyId'
  }
}

export function filterableColumns(columns: DatasetColumn[]) {
  return columns.filter((column) => column.isFilterable)
}

export function isUserScopeConfigured(
  userScope: { enabled?: boolean; field?: string | null; property?: string | null } | null | undefined,
) {
  return Boolean(userScope?.enabled && userScope.field?.trim())
}
