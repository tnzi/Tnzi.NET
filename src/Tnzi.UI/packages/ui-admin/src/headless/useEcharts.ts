/**
 * `useEcharts` re-export. Sunk to `@tnzi/ui` in 0.2.x so site/chat/
 * mobile dashboards can reuse the same chart wiring; admin call-sites
 * keep their existing import path through this barrel.
 *
 * Prefer importing directly from `@tnzi/ui` in new code:
 *   import { useEcharts } from '@tnzi/ui'
 */
export { useEcharts } from '@tnzi/ui'
export type { UseEchartsOptions, UseEchartsReturn } from '@tnzi/ui'
