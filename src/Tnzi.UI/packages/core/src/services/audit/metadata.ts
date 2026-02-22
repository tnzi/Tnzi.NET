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
 * Entity change type
 */
export enum EntityChangeType {
  Created = 1,
  Updated = 2,
  Deleted = 3,
}

/**
 * Get entity change type label
 */
export function getEntityChangeTypeLabel(type: EntityChangeType): string {
  switch (type) {
    case EntityChangeType.Created:
      return 'Created';
    case EntityChangeType.Updated:
      return 'Updated';
    case EntityChangeType.Deleted:
      return 'Deleted';
    default:
      return 'Unknown';
  }
}
