/**
 * `useAdminSettingsConfig()` — install-time configuration for the built-in
 * Settings Center page shipped by `@tnzi/ui-admin`.
 *
 * The default `Settings.vue` page consumes this via `inject()` so the
 * consumer can register custom sections at `defineAdminApp()` time:
 *
 * ```ts
 * defineAdminApp({
 *   client: http,
 *   settings: {
 *     sections: [
 *       { key: 'channels', label: 'Channels', icon: 'mdi:link-variant', group: 'Cowork', component: ChannelsSection },
 *     ],
 *     hideGroups: ['ai-tools'],
 *   },
 * })
 * ```
 */
import type { App, Component, InjectionKey } from 'vue'
import { inject } from 'vue'

export interface AdminSettingsSection {
  /** Unique section key (used as nav key `custom:<key>`). */
  key: string
  /** i18n key (`admin.*`) or plain label. */
  label: string
  /** Iconify icon name. */
  icon?: string
  /** Left-nav group label; defaults to 'App'. */
  group?: string
  /** Sort order inside the nav (schema groups use backend Order). */
  order?: number
  /** Optional permission code required to SEE this section. Checked via usePermissionGuard().can(); omit to always show (super-admin / not-yet-loaded fail-open as everywhere). */
  permission?: string
  /** Panel component — rendered in the right panel, owns its own data + save bar. */
  component: Component | (() => Promise<unknown>)
}

/**
 * Realtime route for `Settings.Changed` broadcasts: when a Global setting whose
 * key matches `prefix` changes (exact match or prefix match, e.g. 'Blog:'),
 * `handler` runs in every open admin session — the consumer's twin of the
 * built-in routes (Appearance:AdminTheme → theme reload, Chat:* → chat config).
 */
export interface AdminSettingsRealtimeRoute {
  /** Key prefix to match (exact key or `startsWith` prefix, e.g. 'Blog:'). */
  prefix: string
  handler: (payload: { key: string; isRemoval?: boolean }) => void
}

export interface AdminSettingsConfig {
  sections?: AdminSettingsSection[]
  /** Hide built-in schema-driven groups by group key (e.g. 'ai-tools'). */
  hideGroups?: string[]
  /**
   * Consumer realtime routes for the app's own `[RuntimeSetting]` configs —
   * re-fetch/apply live when a super admin changes them (no page reload).
   */
  realtime?: AdminSettingsRealtimeRoute[]
  /**
   * Override the SignalR settings hub URL. Default '/hubs/settings' (root-relative,
   * resolved against the page origin). Set e.g. '/api/hubs/settings' when the API
   * is hosted under a sub-path.
   */
  hubUrl?: string
}

export const ADMIN_SETTINGS_CONFIG_KEY: InjectionKey<AdminSettingsConfig> = Symbol(
  'tnzi-admin-settings-config',
)

export function provideAdminSettingsConfig(app: App, config: AdminSettingsConfig): void {
  app.provide(ADMIN_SETTINGS_CONFIG_KEY, config)
}

export function useAdminSettingsConfig(): AdminSettingsConfig | null {
  return inject(ADMIN_SETTINGS_CONFIG_KEY, null)
}
