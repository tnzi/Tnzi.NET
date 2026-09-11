/**
 * Feature Module Types - feature flags: definitions, scoped values, usage analytics.
 * Aligned with `Tnzi.Feature` (`Dtos/FeatureDtos.cs`, `Dtos/FeatureUsageDtos.cs`).
 */

// ============================================
// Enums
// ============================================

// The backend registers a global JsonStringEnumConverter, so enum-typed
// response fields serialize as their PascalCase member names. String members
// keep the wire shape exact.

/** `FeatureValueType` - shape of a feature's value. */
export enum FeatureValueType {
  Boolean = 'Boolean',
  Integer = 'Integer',
  String = 'String',
}

/**
 * `FeatureValueSource` - where the effective value shown for a scope comes from.
 *  - `Explicit`: the requested scope holds its own value
 *  - `Inherited`: a lower-priority keyless scope (Global) supplies it
 *  - `Default`: no scope holds a value; the definition default applies
 */
export enum FeatureValueSource {
  Default = 'Default',
  Inherited = 'Inherited',
  Explicit = 'Explicit',
}

/** Origin of a definition row: persisted (editable) or shipped in code (read-only). */
export type FeatureDefinitionSource = 'Database' | 'Code';

/** Trend bucket size accepted by `GET /admin/feature/usage/trend` (wire contract, lowercase). */
export type FeatureUsagePeriod = 'daily' | 'weekly' | 'monthly';

// ============================================
// Definitions
// ============================================

/** Mirror of `FeatureDefinitionDto`. */
export interface FeatureDefinitionDto {
  /** `Guid.Empty` (all zeros) for code-defined rows - they have no database id. */
  id: string;
  name: string;
  displayName?: string | null;
  description?: string | null;
  defaultValue?: string | null;
  valueType: FeatureValueType;
  parentName?: string | null;
  isEnabled: boolean;
  group?: string | null;
  source: FeatureDefinitionSource;
  /**
   * True for code-defined definitions. Edit/delete are rejected server-side, and
   * so are value writes (a `FeatureValue` references the definition by database
   * id, which these rows do not have).
   */
  isReadOnly: boolean;
}

/** Mirror of `CreateFeatureDefinitionRequest`. */
export interface CreateFeatureDefinitionDto {
  name: string;
  displayName?: string | null;
  description?: string | null;
  defaultValue?: string | null;
  valueType: FeatureValueType;
  parentName?: string | null;
  group?: string | null;
}

/** Mirror of `UpdateFeatureDefinitionRequest`. */
export interface UpdateFeatureDefinitionDto {
  displayName?: string | null;
  description?: string | null;
  defaultValue?: string | null;
  valueType: FeatureValueType;
  parentName?: string | null;
  isEnabled?: boolean;
  group?: string | null;
}

// ============================================
// Values
// ============================================

/**
 * Mirror of `FeatureValueProviderDto` - a registered value provider, i.e. a
 * scope values can be written to. `GET /admin/feature-values/providers`.
 */
export interface FeatureValueProviderDto {
  /** Canonical name - the exact `providerName` to send. */
  name: string;
  /** Runtime evaluation priority; higher is consulted first. */
  priority: number;
  /** Keyed providers (Tenant) require `providerKey`; keyless ones (Global) reject it. */
  requiresKey: boolean;
  /**
   * False when the provider cannot evaluate anything in this deployment (the
   * tenant provider with multi-tenancy off). Writes to it are refused with 400;
   * existing rows stay readable for cleanup.
   */
  isActive: boolean;
  inactiveReason?: string | null;
}

/** Mirror of `FeatureValueDto` - one stored value row. */
export interface FeatureValueDto {
  id: string;
  featureDefinitionId: string;
  featureName?: string | null;
  providerName: string;
  providerKey?: string | null;
  value: string;
}

/** Mirror of `SetFeatureValueRequest`. */
export interface SetFeatureValueDto {
  featureDefinitionId: string;
  providerName: string;
  providerKey?: string | null;
  value: string;
}

/** Mirror of `BatchFeatureValueItem`. */
export interface BatchFeatureValueItemDto {
  featureDefinitionId: string;
  value: string;
}

/** Mirror of `BatchSetFeatureValuesRequest`. */
export interface BatchSetFeatureValuesDto {
  providerName: string;
  providerKey?: string | null;
  values: BatchFeatureValueItemDto[];
}

/** Mirror of `BatchSetFeatureValuesResultDto`. */
export interface BatchSetFeatureValuesResultDto {
  succeededCount: number;
  failedCount: number;
  errors: string[];
}

/**
 * Mirror of `FeatureValueWithDefinitionDto` - every enabled definition with the
 * value the runtime resolves for one scope. `GET /admin/feature-values/all`.
 */
export interface FeatureValueWithDefinitionDto {
  /** Id of the scope's own value row; all-zeros when the scope holds none. */
  id: string;
  /** All-zeros for code-defined definitions (`canOverride === false`). */
  featureDefinitionId: string;
  featureName: string;
  displayName?: string | null;
  description?: string | null;
  group?: string | null;
  valueType: FeatureValueType;
  defaultValue?: string | null;
  /** Scope's own value, else the inherited keyless value, else the default. */
  effectiveValue: string;
  /** Whether the requested scope holds its own value. */
  isExplicitlySet: boolean;
  effectiveSource: FeatureValueSource;
  /** Provider whose value is effective; null when the default applies. */
  effectiveProvider?: string | null;
  isEnabled: boolean;
  source: FeatureDefinitionSource;
  /** False for code-defined definitions - the UI must not offer to set a value. */
  canOverride: boolean;
}

// ============================================
// Usage analytics
// ============================================

/** Optional inclusive date-range filter shared by the usage endpoints (ISO strings). */
export interface FeatureUsageRangeQuery {
  from?: string | null;
  to?: string | null;
}

/** Mirror of `FeatureUsageStatsDto`. */
export interface FeatureUsageStatsDto {
  featureName: string;
  totalChecks: number;
  uniqueUsers: number;
  /** Percentage of checks that answered "enabled", 0-100. */
  enableRate: number;
  firstUsed?: string | null;
  lastUsed?: string | null;
}

/** Mirror of `FeatureUsageTrendDto` - one bucket of the trend series. */
export interface FeatureUsageTrendDto {
  /** `yyyy-MM-dd` / `yyyy-Www` (ISO week) / `yyyy-MM` depending on the period. */
  period: string;
  checkCount: number;
  enableCount: number;
  disableCount: number;
}

/** Mirror of `FeaturePopularityDto` - one row of the most-checked ranking. */
export interface FeaturePopularityDto {
  featureName: string;
  checkCount: number;
  uniqueUsers: number;
  enableRate: number;
}
