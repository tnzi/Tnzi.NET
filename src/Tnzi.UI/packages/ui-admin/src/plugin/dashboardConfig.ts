/**
 * `useAdminDashboardConfig()` — install-time configuration for the default
 * Dashboard page shipped by `@tnzi/ui-admin` (Phase J / 0.2.71+).
 *
 * The default `Dashboard.vue` page consumes this via `inject()` so the
 * consumer can declare the dashboard inline at `defineAdminApp()` time:
 *
 * ```ts
 * defineAdminApp({
 *   client: http,
 *   dashboard: {
 *     widgets: [
 *       { id: 'banner', component: TWidgetHeaderBanner, span: 24, pinned: true },
 *       { id: 'ai-usage', component: TWidgetAiUsage, span: { md: 12, lg: 8 } },
 *       { id: 'identity-stats', component: TWidgetIdentityStats, span: { md: 12, lg: 8 } },
 *     ],
 *     layout: 'draggable',
 *   },
 * })
 * ```
 *
 * When omitted the page falls back to `defaultWorkbenchWidgets()` (see
 * `widgets/presets.ts`) so a fresh consumer still sees a usable dashboard
 * without any code. (The dashboard is built on the generic `TWorkbenchLayout`
 * widget-grid primitive, hence the `WorkbenchConfig` shape.)
 */
import type { App, InjectionKey } from 'vue'
import { inject } from 'vue'
import type { WorkbenchConfig } from '../widgets/types'

export type AdminDashboardConfig = WorkbenchConfig

export const ADMIN_DASHBOARD_CONFIG_KEY: InjectionKey<AdminDashboardConfig> = Symbol(
  'tnzi-admin-dashboard-config',
)

export function provideAdminDashboardConfig(
  app: App,
  config: AdminDashboardConfig,
): void {
  app.provide(ADMIN_DASHBOARD_CONFIG_KEY, config)
}

/**
 * Inject the consumer-supplied dashboard config. Returns `null` when no
 * `defineAdminApp({ dashboard: … })` was passed — the page then renders
 * the bundled default widget set.
 */
export function useAdminDashboardConfig(): AdminDashboardConfig | null {
  return inject(ADMIN_DASHBOARD_CONFIG_KEY, null)
}
