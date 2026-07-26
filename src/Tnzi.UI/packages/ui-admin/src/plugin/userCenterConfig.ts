/**
 * `useAdminUserCenterConfig()` - install-time configuration for the built-in
 * User Center (self-service personal center) page shipped by `@tnzi/ui-admin`.
 *
 * The default `UserCenter.vue` page is a **section-registry-driven** shell (the
 * same shape as the Settings Center): six built-in sections (profile / security
 * / sessions / history / linked / danger) live in a registry, and this config
 * lets a consuming application reshape that registry at `defineAdminApp()` time -
 * hide a section that doesn't fit its business, move a section to a different (or
 * a brand-new) group, override a built-in section with its own component, append
 * entirely custom sections, and tune individual Profile fields.
 *
 * ```ts
 * defineAdminApp({
 *   client: http,
 *   userCenter: {
 *     // Hide the whole GDPR danger zone for an internal back-office.
 *     hideSections: ['danger'],
 *     // Move "Linked accounts" out of Advanced into its own group.
 *     sectionGroups: { linked: 'Connections' },
 *     // Replace the built-in Security section with the app's own component.
 *     overrides: { security: () => import('./MySecurity.vue') },
 *     // Append an app-specific section under a custom group.
 *     sections: [
 *       { key: 'billing', label: 'Billing', icon: 'mdi:credit-card', group: 'Connections', component: BillingSection },
 *     ],
 *     // Field-level: hide the free-text website/bio, lock the display name.
 *     profile: { hideFields: ['website', 'bio'], readonlyFields: ['nickname'] },
 *   },
 * })
 * ```
 *
 * Everything here is OPTIONAL and additive: with no `userCenter` config the page
 * renders the six built-in sections exactly as before.
 */
import type { App, Component, InjectionKey } from 'vue'
import { inject } from 'vue'

/** Keys of the six built-in sections - the values accepted by `hideSections`,
 *  `overrides`, `sectionGroups` and `sectionOrder`. */
export type UserCenterBuiltInSectionKey =
  | 'profile'
  | 'security'
  | 'sessions'
  | 'history'
  | 'linked'
  | 'danger'

/** Configurable Profile fields (identity-core fields username/email/phone are
 *  never hidden - email/phone visibility follows backend channel config). */
export type UserCenterProfileField =
  | 'firstName'
  | 'lastName'
  | 'nickname'
  | 'gender'
  | 'birthday'
  | 'bio'
  | 'address'
  | 'website'

/** A consumer-registered custom section appended to the User Center nav. */
export interface AdminUserCenterSection {
  /** Unique section key (rendered as nav key `custom:<key>`). Must not collide
   *  with a built-in key. */
  key: string
  /** i18n key (`admin.*`) or a plain label. */
  label: string
  /** Iconify icon name. */
  icon?: string
  /** Left-nav group label; defaults to 'App'. A built-in group key
   *  (`account` / `activity` / `advanced`) drops the section into that group. */
  group?: string
  /** Sort order across the whole nav (built-ins default 10/20/…/60). */
  order?: number
  /** Permission code required to SEE this section - checked via
   *  `usePermissionGuard().can()`; omit to always show (super-admin /
   *  not-yet-loaded fail-open as everywhere). */
  permission?: string
  /** Module short-name required to SEE this section - hidden when the backend
   *  hasn't loaded that module (e.g. `'storage'`). Fail-open when the module
   *  signal is unavailable. */
  module?: string
  /** Panel component - rendered in the right panel, owns its own data + save
   *  bar. Either a component object (incl. a `defineAsyncComponent(...)` result)
   *  or a plain loader (`() => import('./Section.vue')`). */
  component: Component | (() => Promise<unknown>)
}

/** Field-level control for the built-in Profile section. */
export interface AdminUserCenterProfileConfig {
  /** Hide individual profile fields by key. */
  hideFields?: UserCenterProfileField[]
  /** Render individual profile fields read-only. */
  readonlyFields?: UserCenterProfileField[]
}

export interface AdminUserCenterConfig {
  /** Consumer custom sections appended to the nav. */
  sections?: AdminUserCenterSection[]
  /** Hide built-in sections by key (e.g. `['danger']`). */
  hideSections?: UserCenterBuiltInSectionKey[]
  /** Hide entire groups by group key - built-in keys `account` / `activity` /
   *  `advanced`, or a custom group label. */
  hideGroups?: string[]
  /** Reassign a section (built-in or custom) to a different / brand-new group
   *  (e.g. `{ linked: 'Connections' }`). The target may be a built-in group key
   *  or any custom label. */
  sectionGroups?: Partial<Record<UserCenterBuiltInSectionKey, string>> & Record<string, string>
  /** Override the nav sort order per section (built-ins default 10/20/…/60). */
  sectionOrder?: Partial<Record<UserCenterBuiltInSectionKey, number>> & Record<string, number>
  /** Override a built-in section's body with a consumer component (the original
   *  built-in body is hidden; the nav entry - label/icon/group - is preserved
   *  unless also reassigned). Value is a component object or a loader. */
  overrides?: Partial<Record<UserCenterBuiltInSectionKey, Component | (() => Promise<unknown>)>>
  /** Field-level control for the built-in Profile section. */
  profile?: AdminUserCenterProfileConfig
}

export const ADMIN_USER_CENTER_CONFIG_KEY: InjectionKey<AdminUserCenterConfig> = Symbol(
  'tnzi-admin-user-center-config',
)

export function provideAdminUserCenterConfig(app: App, config: AdminUserCenterConfig): void {
  app.provide(ADMIN_USER_CENTER_CONFIG_KEY, config)
}

/** Read the app-wide User Center config. Returns an empty object when nothing
 *  was provided (bare test mounts / no `defineAdminApp` config) so callers never
 *  branch on null. */
export function useAdminUserCenterConfig(): AdminUserCenterConfig {
  return inject(ADMIN_USER_CENTER_CONFIG_KEY, EMPTY_CONFIG)
}

const EMPTY_CONFIG: AdminUserCenterConfig = Object.freeze({})
