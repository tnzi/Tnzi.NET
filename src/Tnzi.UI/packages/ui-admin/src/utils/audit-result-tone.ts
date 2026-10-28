import { AuditResultType } from '@tnzi/core/services/audit'

/**
 * Tag tone for an audit operation's result. The wire value is the PascalCase
 * enum member name (the backend's global `JsonStringEnumConverter`), so the
 * comparison is against `AuditResultType.*`; anything else (no result, an
 * unknown value) stays neutral.
 */
export function auditResultTone(value: unknown): 'success' | 'error' | 'warning' | 'default' {
  if (value === AuditResultType.Success) return 'success'
  if (value === AuditResultType.Failed) return 'error'
  if (value === AuditResultType.Warning) return 'warning'
  return 'default'
}
