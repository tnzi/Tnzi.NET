/**
 * Common Enums - Shared across modules
 *
 * Note: Gender/OAuthProvider are defined in services/identity/metadata.ts
 * (domain-specific enums belong with their service module).
 */

/**
 * Boolean status enum (for explicit true/false/unknown states)
 */
export enum TriState {
  Unknown = 0,
  False = 1,
  True = 2,
}

/**
 * Enable/disable status
 */
export enum EnableStatus {
  Disabled = 0,
  Enabled = 1,
}

/**
 * Check if status is enabled
 */
export function isEnabled(status: EnableStatus): boolean {
  return status === EnableStatus.Enabled;
}

/**
 * Common status
 */
export enum CommonStatus {
  Inactive = 0,
  Active = 1,
}

/**
 * Record operation type
 */
export enum OperationType {
  Create = 1,
  Update = 2,
  Delete = 3,
}

/**
 * Date range preset
 */
export enum DateRangePreset {
  Today = 'today',
  Yesterday = 'yesterday',
  ThisWeek = 'thisWeek',
  LastWeek = 'lastWeek',
  ThisMonth = 'thisMonth',
  LastMonth = 'lastMonth',
  ThisQuarter = 'thisQuarter',
  LastQuarter = 'lastQuarter',
  ThisYear = 'thisYear',
  LastYear = 'lastYear',
  Custom = 'custom',
}
