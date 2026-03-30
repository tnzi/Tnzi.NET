/**
 * Audit Module Metadata
 */

/**
 * Audit result type
 */
export enum AuditResultType {
  Success = 1,
  Failed = 2,
  Warning = 3,
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
 * Entity change type (maps to backend Tnzi.Audit.Entities.EntityState)
 */
export enum EntityChangeType {
  Unchanged = 0,
  Added = 1,
  Modified = 2,
  Deleted = 3,
  Detached = 4,
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
