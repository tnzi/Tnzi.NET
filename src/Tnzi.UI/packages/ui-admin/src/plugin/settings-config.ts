/**
 * `useAdminSettingsConfig()` - install-time configuration for the built-in
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
  /** Left-nav group label; defaults to 'App'. A backend module name (e.g.
   *  'System') merges the section into that schema group instead. */
  group?: string
  /** Sort order inside the nav (schema groups use backend Order). */
  order?: number
  /** Optional permission code required to SEE this section. Checked via usePermissionGuard().can(); omit to always show (super-admin / not-yet-loaded fail-open as everywhere). */
  permission?: string
  /**
   * Roles allowed to SEE this section: any one of them is enough, compared
   * case-insensitively, the same rule as a route's `meta.roles`. For a section
   * whose endpoints answer by role and have no permission code. Combined with
   * `permission`, both must hold. Super-user bypass and not-yet-loaded fail-open
   * as everywhere; the backend stays the real wall.
   */
  roles?: string[]
  /** Module short-name required to SEE this section - hidden when the backend
   *  hasn't loaded that module (e.g. `'chat'`). Fail-open when the module
   *  signal is unavailable. Mirrors the User Center section contract. */
  module?: string
  /** Panel component - rendered in the right panel, owns its own data + save bar. */
  component: Component | (() => Promise<unknown>)
}

/**
 * Realtime route for `Settings.Changed` broadcasts: when a Global setting whose
 * key matches `prefix` changes (exact match or prefix match, e.g. 'Blog:'),
 * `handler` runs in every open admin session - the consumer's twin of the
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
   * Hide the built-in "Advanced / Parameters" section (the raw setting-value
   * table). Default `false` (shown when the caller holds `system.parameter.view`).
   * Set `true` for a product that doesn't want to expose the raw parameter
   * editor at all - the schema-driven groups stay, only the Advanced entry drops.
   */
  hideAdvanced?: boolean
  /**
   * Consumer realtime routes for the app's own `[RuntimeSetting]` configs -
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
  registerSettingsSectionEntryRules(config.sections ?? [])
}

/**
 * What it takes to see one of the consumer's gated custom sections: its
 * permission code and / or its roles (any-of). Both, when both are declared.
 */
export interface SettingsSectionEntryRule {
  readonly permission?: string
  readonly roles: readonly string[]
}

/**
 * Entry rules of the consumer's custom sections that declare a `permission` or
 * `roles`. Being able to see any of them opens the Settings Center (route guard
 * and the sidebar entry), because those sections live nowhere else: a billing
 * manager whose only grant is `payment.account.view` must reach the billing
 * sections without also holding a settings or parameter code. A section is an
 * entry only for whoever passes its whole rule, so a roles-only section opens
 * the gear for holders of the role and for nobody else. Ungated sections are
 * not entries (they would open the Settings Center to every administrator).
 * Module-level (not injected) because the router guard asks outside any
 * component setup.
 */
let sectionEntryRules: readonly SettingsSectionEntryRule[] = []

function registerSettingsSectionEntryRules(sections: readonly AdminSettingsSection[]): void {
  sectionEntryRules = sections
    .map((section) => ({ permission: section.permission || undefined, roles: nonEmptyStrings(section.roles) }))
    .filter((rule) => rule.permission !== undefined || rule.roles.length > 0)
}

function nonEmptyStrings(value: readonly string[] | undefined): string[] {
  return (value ?? []).filter((v) => typeof v === 'string' && v !== '')
}

/** Rules registered by {@link provideAdminSettingsConfig}; read by the Settings Center gate. */
export function settingsSectionEntryRules(): readonly SettingsSectionEntryRule[] {
  return sectionEntryRules
}

export function useAdminSettingsConfig(): AdminSettingsConfig | null {
  return inject(ADMIN_SETTINGS_CONFIG_KEY, null)
}
