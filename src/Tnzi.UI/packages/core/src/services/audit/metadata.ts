/**
 * Audit Module Metadata
 */

/**
 * Audit result type.
 *
 * Serialized as PascalCase member names by the backend's global
 * `JsonStringEnumConverter` (C# `AuditResultType`: Success=1 / Failed=2 /
 * Warning=3). The string-valued enum keeps the wire format and member names
 * in one place — comparisons and filter option values use `AuditResultType.*`
 * directly and match the response strings.
 */
export enum AuditResultType {
  Success = 'Success',
  Failed = 'Failed',
  Warning = 'Warning',
}

/**
 * Get audit result type label
 */
export function getAuditResultTypeLabel(type: AuditResultType): string {
  switch (type) {
    case AuditResultType.Success:
      return 'Success';
    case AuditResultType.Failed:
      return 'Failed';
    case AuditResultType.Warning:
      return 'Warning';
    default:
      return 'Unknown';
  }
}

/**
 * Entity change type (maps to backend Tnzi.Audit.Entities.EntityState).
 *
 * Serialized as PascalCase member names by the backend's global
 * `JsonStringEnumConverter` (same wire format as {@link AuditResultType}) —
 * string-valued members keep comparisons and rendering aligned with the
 * response strings.
 */
export enum EntityChangeType {
  Unchanged = 'Unchanged',
  Added = 'Added',
  Modified = 'Modified',
  Deleted = 'Deleted',
  Detached = 'Detached',
}

/**
 * Get entity change type label
 */
export function getEntityChangeTypeLabel(type: EntityChangeType): string {
  switch (type) {
    case EntityChangeType.Unchanged:
      return 'Unchanged';
    case EntityChangeType.Added:
      return 'Added';
    case EntityChangeType.Modified:
      return 'Modified';
    case EntityChangeType.Deleted:
      return 'Deleted';
    case EntityChangeType.Detached:
      return 'Detached';
    default:
      return 'Unknown';
  }
}

/**
 * Audit trend grouping interval
 */
export enum AuditTrendGroupBy {
  Daily = 0,
  Weekly = 1,
  Monthly = 2,
}

/**
 * Get audit trend group-by label
 */
export function getAuditTrendGroupByLabel(groupBy: AuditTrendGroupBy): string {
  switch (groupBy) {
    case AuditTrendGroupBy.Daily:
      return 'Daily';
    case AuditTrendGroupBy.Weekly:
      return 'Weekly';
    case AuditTrendGroupBy.Monthly:
      return 'Monthly';
    default:
      return 'Unknown';
  }
}
