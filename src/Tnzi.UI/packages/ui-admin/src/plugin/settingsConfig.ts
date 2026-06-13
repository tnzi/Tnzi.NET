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
  /** Panel component — rendered in the right panel, owns its own data + save bar. */
  component: Component | (() => Promise<unknown>)
}

export interface AdminSettingsConfig {
  sections?: AdminSettingsSection[]
  /** Hide built-in schema-driven groups by group key (e.g. 'ai-tools'). */
  hideGroups?: string[]
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
