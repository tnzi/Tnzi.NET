/**
 * System Module Metadata
 */

/**
 * Health status
 */
export enum HealthStatus {
  Unhealthy = 0,
  Degraded = 1,
  Healthy = 2,
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
