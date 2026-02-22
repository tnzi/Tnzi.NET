/**
 * Common Enums - Shared across modules
 */

/**
 * User gender
 */
export enum Gender {
  Unknown = 0,
  Male = 1,
  Female = 2,
}

/**
 * Get gender display name
 */
export function getGenderLabel(gender: Gender): string {
  switch (gender) {
    case Gender.Male:
      return 'Male';
    case Gender.Female:
      return 'Female';
    default:
      return 'Unknown';
  }
}

/**
 * OAuth provider types
 */
export enum OAuthProvider {
  Google = 'Google',
  Microsoft = 'Microsoft',
  Facebook = 'Facebook',
  Twitter = 'Twitter',
  GitHub = 'GitHub',
}

/**
 * Get OAuth provider display name
 */
export function getOAuthProviderLabel(provider: OAuthProvider): string {
  return provider;
}

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
 * Sort direction
 */
export enum SortDirection {
  Ascending = 'asc',
  Descending = 'desc',
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
