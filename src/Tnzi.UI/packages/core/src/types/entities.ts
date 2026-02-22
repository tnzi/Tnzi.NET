/**
 * Entity Base Types - Aligned with Tnzi.NET backend entity hierarchy
 */

/**
 * Key types used in entities
 */
export type EntityKey = string | number;

/**
 * Base entity interface - all entities have an ID
 */
export interface IEntity<TKey extends EntityKey = string> {
  id: TKey;
}

/**
 * Creation audited entity - tracks creation time
 * Use for: logs, snapshots, immutable records
 */
export interface CreationAuditedEntity<TKey extends EntityKey = string> extends IEntity<TKey> {
  /** UTC timestamp when entity was created */
  creationTime: Date;
}

/**
 * Audited entity - tracks creation and modification times
 * Use for: business entities without soft delete
 */
export interface AuditedEntity<TKey extends EntityKey = string> extends CreationAuditedEntity<TKey> {
  /** UTC timestamp when entity was last modified (null if never modified) */
  lastModificationTime?: Date | null;
}

/**
 * Full audited entity - complete audit trail with soft delete and tenant support
 * Use for: main business entities with full audit requirements
 */
export interface FullAuditedEntity<TKey extends EntityKey = string> extends AuditedEntity<TKey> {
  /** ID of user who created this entity */
  creatorId?: string | null;
  /** ID of user who last modified this entity */
  lastModifierId?: string | null;
  /** Soft delete flag */
  isDeleted: boolean;
  /** ID of user who deleted this entity */
  deleterId?: string | null;
  /** UTC timestamp when entity was deleted */
  deletionTime?: Date | null;
  /** Tenant ID for multi-tenancy */
  tenantId?: string | null;
}

/**
 * Entity with version for optimistic concurrency
 */
export interface VersionedEntity<TKey extends EntityKey = string> extends IEntity<TKey> {
  /** Version number for optimistic concurrency control */
  version: number;
}

/**
 * Entity with display order
 */
export interface OrderedEntity<TKey extends EntityKey = string> extends IEntity<TKey> {
  /** Display order (lower = higher priority) */
  sortOrder: number;
}

/**
 * Entity with soft delete only (no creation/modification tracking)
 */
export interface SoftDeletableEntity<TKey extends EntityKey = string> extends IEntity<TKey> {
  isDeleted: boolean;
  deleterId?: string | null;
  deletionTime?: Date | null;
}

/**
 * Entity with tenant support only
 */
export interface TenantEntity<TKey extends EntityKey = string> extends IEntity<TKey> {
  tenantId?: string | null;
}

/**
 * Type guard for checking if entity has creation audit
 */
export function isCreationAudited<TKey extends EntityKey>(
  entity: IEntity<TKey>
): entity is CreationAuditedEntity<TKey> {
  return 'creationTime' in entity;
}

/**
 * Type guard for checking if entity has modification audit
 */
export function isAudited<TKey extends EntityKey>(
  entity: IEntity<TKey>
): entity is AuditedEntity<TKey> {
  return isCreationAudited(entity) && 'lastModificationTime' in entity;
}

/**
 * Type guard for checking if entity has full audit
 */
export function isFullAudited<TKey extends EntityKey>(
  entity: IEntity<TKey>
): entity is FullAuditedEntity<TKey> {
  return (
    isAudited(entity) &&
    'isDeleted' in entity &&
    typeof (entity as FullAuditedEntity<TKey>).isDeleted === 'boolean'
  );
}
