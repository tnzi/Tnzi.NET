/**
 * `createAuditHumanizer` — turn the framework audit wire shape into business
 * language. The audit trail records `functionName` ("Identity.Update"), CLR
 * entity type names, bookkeeping columns (Id / ConcurrencyStamp / CreatorId …),
 * and raw GUID / ISO string values — none of which read well to a business user.
 * Any consumer surfacing an audit view re-implements the same translation, so
 * this ships it once: action verb + icon + tone, friendly entity label, hidden
 * bookkeeping columns filtered out, and value formatting (dates, booleans,
 * GUID shortening, empty → em-dash). The domain label tables stay app-supplied.
 */
import type { StatusType } from '@tnzi/ui'
import { formatDateTime } from '@tnzi/core'
import { EntityChangeType } from '@tnzi/core/services/audit'
import type { AuditEntityEntryDto, AuditPropertyEntryDto } from '@tnzi/core/services/audit'

/** Framework-managed bookkeeping columns hidden from a business audit view. */
const DEFAULT_HIDDEN_FIELDS = [
  'Id', 'ConcurrencyStamp', 'CreatorId', 'CreationTime', 'LastModifierId',
  'LastModificationTime', 'DeleterId', 'DeletionTime', 'IsDeleted', 'TenantId', 'ExtraProperties',
]

export interface AuditAction {
  label: string
  icon?: string
  tone?: StatusType
}

const DEFAULT_ACTIONS: Record<string, AuditAction> = {
  create: { label: 'Created', icon: 'mdi:plus-circle-outline', tone: 'success' },
  update: { label: 'Updated', icon: 'mdi:pencil-outline', tone: 'info' },
  delete: { label: 'Deleted', icon: 'mdi:delete-outline', tone: 'error' },
  view: { label: 'Viewed', icon: 'mdi:eye-outline', tone: 'default' },
}

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const ISO_RE = /^\d{4}-\d{2}-\d{2}T/

/** `StaffProfile` → `Staff Profile`. */
function spaceCamel(name: string): string {
  return name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').trim()
}

export interface AuditHumanizerOptions {
  /** Extra property names to hide beyond the built-in bookkeeping columns. */
  hiddenFields?: string[]
  /** CLR entity type name → friendly label (e.g. `{ StaffProfile: 'Staff member' }`). */
  entityLabels?: Record<string, string>
  /** Override the default action labels/icons/tones (e.g. localized verbs). */
  actionLabels?: Partial<Record<'create' | 'update' | 'delete' | 'view', AuditAction>>
  /** Domain value formatter, applied first; return `undefined` to fall through to the built-ins. */
  formatValue?: (raw: string | null | undefined, prop?: AuditPropertyEntryDto) => string | undefined
}

export interface AuditHumanizer {
  /** Business action from `functionName` (preferred) or an entity `operationType`. */
  action(functionName?: string | null, operationType?: EntityChangeType): AuditAction
  /** CLR entity type name → friendly label (falls back to a spaced form). */
  entity(entityTypeName?: string | null): string
  /** Format a raw audit value (date / boolean / shortened GUID / empty → em-dash). */
  value(raw: string | null | undefined, prop?: AuditPropertyEntryDto): string
  /** Whether a property is a hidden bookkeeping column. */
  isHidden(propertyName?: string | null): boolean
  /** An entity entry's property changes with bookkeeping columns removed. */
  visibleProps(entry: AuditEntityEntryDto): AuditPropertyEntryDto[]
}

export function createAuditHumanizer(options: AuditHumanizerOptions = {}): AuditHumanizer {
  const hidden = new Set([...DEFAULT_HIDDEN_FIELDS, ...(options.hiddenFields ?? [])])
  const actions: Record<string, AuditAction> = { ...DEFAULT_ACTIONS, ...(options.actionLabels ?? {}) }

  function verbFromFunction(functionName?: string | null): string | undefined {
    if (!functionName) return undefined
    const suffix = (functionName.split('.').pop() ?? '').toLowerCase()
    if (suffix.includes('create') || suffix.includes('add')) return 'create'
    if (suffix.includes('update') || suffix.includes('edit') || suffix.includes('modify')) return 'update'
    if (suffix.includes('delete') || suffix.includes('remove')) return 'delete'
    if (suffix.includes('get') || suffix.includes('query') || suffix.includes('view') || suffix.includes('list')) return 'view'
    return undefined
  }
  function verbFromChange(t?: EntityChangeType): string | undefined {
    switch (t) {
      case EntityChangeType.Added: return 'create'
      case EntityChangeType.Modified: return 'update'
      case EntityChangeType.Deleted: return 'delete'
      default: return undefined
    }
  }

  function isHidden(propertyName?: string | null): boolean {
    return !!propertyName && hidden.has(propertyName)
  }

  return {
    action(functionName, operationType) {
      const verb = verbFromFunction(functionName) ?? verbFromChange(operationType)
      if (verb && actions[verb]) return actions[verb]
      const suffix = functionName?.split('.').pop()
      return { label: suffix ? spaceCamel(suffix) : 'Changed', tone: 'default' }
    },
    entity(entityTypeName) {
      if (!entityTypeName) return ''
      return options.entityLabels?.[entityTypeName] ?? spaceCamel(entityTypeName)
    },
    value(raw, prop) {
      const custom = options.formatValue?.(raw, prop)
      if (custom !== undefined) return custom
      if (raw === null || raw === undefined || raw === '') return '—'
      if (raw === 'true') return 'Yes'
      if (raw === 'false') return 'No'
      if (ISO_RE.test(raw)) return formatDateTime(raw, { fallback: raw })
      if (GUID_RE.test(raw)) return `${raw.slice(0, 8)}…`
      return raw
    },
    isHidden,
    visibleProps(entry) {
      return (entry.propertyEntries ?? []).filter((p) => !isHidden(p.propertyName))
    },
  }
}
