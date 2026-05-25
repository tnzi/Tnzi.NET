/**
 * `useAdminWorkbenchConfig()` — install-time configuration for the default
 * Workbench page shipped by `@tnzi/ui-admin` (Phase J / 0.2.71+).
 *
 * The default `Workbench.vue` page consumes this via `inject()` so the
 * consumer can declare the dashboard inline at `defineAdminApp()` time:
 *
 * ```ts
 * defineAdminApp({
 *   client: http,
 *   workbench: {
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
 * without any code.
 */
import type { App, InjectionKey } from 'vue'
import { inject } from 'vue'
import type { WorkbenchConfig } from '../widgets/types'

export type AdminWorkbenchConfig = WorkbenchConfig

export const ADMIN_WORKBENCH_CONFIG_KEY: InjectionKey<AdminWorkbenchConfig> = Symbol(
  'tnzi-admin-workbench-config',
)

export function provideAdminWorkbenchConfig(
  app: App,
  config: AdminWorkbenchConfig,
): void {
  app.provide(ADMIN_WORKBENCH_CONFIG_KEY, config)
}

/**
 * Inject the consumer-supplied workbench config. Returns `null` when no
 * `defineAdminApp({ workbench: … })` was passed — the page then renders
 * the bundled default widget set.
 */
export function useAdminWorkbenchConfig(): AdminWorkbenchConfig | null {
  return inject(ADMIN_WORKBENCH_CONFIG_KEY, null)
}
