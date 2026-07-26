/**
 * System Module Metadata
 */

/**
 * Health status. String enum (member name = value): `Tnzi.HealthChecks`
 * writes `report.Status.ToString()` into the health payload, so the wire value
 * is always the PascalCase name.
 */
export enum HealthStatus {
  Unhealthy = 'Unhealthy',
  Degraded = 'Degraded',
  Healthy = 'Healthy',
}

/**
 * Get health status label
 */
export function getHealthStatusLabel(status: HealthStatus): string {
  switch (status) {
    case HealthStatus.Unhealthy:
      return 'Unhealthy';
    case HealthStatus.Degraded:
      return 'Degraded';
    case HealthStatus.Healthy:
      return 'Healthy';
    default:
      return 'Unknown';
  }
}

/**
 * Menu badge type
 */
export type MenuBadgeType = 'primary' | 'success' | 'warning' | 'danger' | 'info';

/**
 * Feature requirement type
 */
export type FeatureRequirementType = 'user' | 'role' | 'organization' | 'percentage' | 'time';
