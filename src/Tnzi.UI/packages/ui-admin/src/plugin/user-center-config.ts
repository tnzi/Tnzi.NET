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
 *     // Field-level: hide the free-text website/bio, lock the display name, and
 *     // lock the login email against self-service rebinding (the phone keeps
 *     // its verify-code change flow - the two are independent).
 *     profile: {
 *       hideFields: ['website', 'bio'],
 *       readonlyFields: ['nickname', 'email'],
 *       // Append the app's own (self-contained) field block to the built-in Profile.
 *       extra: () => import('./ProfileContactBlock.vue'),
 *     },
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

/** Optional Profile fields - the ones `hideFields` accepts. Identity-core
 *  fields (username / email / phone) are never hidden: a user who cannot see
 *  which address their account signs in with cannot reason about it at all.
 *  To stop them CHANGING one, see {@link UserCenterIdentityField}. */
export type UserCenterProfileField =
  | 'firstName'
  | 'lastName'
  | 'nickname'
  | 'gender'
  | 'birthday'
  | 'bio'
  | 'address'
  | 'website'

/**
 * Identity-core fields that can be locked against **self-service** changes.
 *
 * Listing one in `readonlyFields` drops the `Change…` affordance from that row
 * (no dead button - the value still renders, because the user needs to know
 * which address their account uses). Use it when the address is assigned and
 * owned by the organisation rather than by the person: e.g. the login email is
 * the staff identity, handed out centrally, and changing it would amount to
 * changing accounts.
 *
 * The two fields are independent - locking `email` leaves the phone-change flow
 * intact, which is exactly the shape an app wants when staff email is fixed but
 * personal mobile numbers move.
 *
 * ⚠️ **This is NOT the backend channel configuration.** Whether the row offers a
 * change flow at all already follows the deployment's auth channels
 * (`allowEmailLogin` / `codeLoginViaEmail` / `recoveryViaEmail` /
 * `registerViaEmail`, and the SMS equivalents). Those switches answer "can this
 * deployment authenticate over email/SMS at all" - turning one off to stop
 * self-service rebinding would also take out email login and account recovery.
 * `readonlyFields` answers a separate question: "may the user rebind it
 * themselves?" Keep the two apart; they are ANDed, never substituted.
 */
export type UserCenterIdentityField = 'email' | 'phone'

/** Everything `readonlyFields` accepts: the optional profile fields plus the
 *  identity-core fields that can be locked against self-service changes. */
export type UserCenterReadonlyField = UserCenterProfileField | UserCenterIdentityField

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
  /** Hide individual profile fields by key. Identity-core fields are not
   *  hideable - see {@link UserCenterProfileField}. */
  hideFields?: UserCenterProfileField[]
  /**
   * Render individual fields read-only.
   *
   * For an ordinary profile field this disables its input. For an identity-core
   * field (`'email'` / `'phone'`) it removes that row's `Change…` button - the
   * value keeps rendering, only the self-service rebinding flow goes away.
   *
   * This is a **self-service** switch and is independent of the backend channel
   * capability that governs whether the change flow exists at all; read
   * {@link UserCenterIdentityField} before conflating the two.
   */
  readonlyFields?: UserCenterReadonlyField[]
  /**
   * Extension block appended to the built-in Profile section. This is the
   * middle ground between "take the built-in section as-is" and
   * `overrides.profile` (which replaces the whole body - forcing the app to
   * re-implement identity editing, the two-step email/phone verify-change and
   * avatar upload just to add a couple of business fields).
   *
   * It receives no props and no framework state (the section's internals stay
   * out of the public contract). The block picks ONE of two modes itself - the
   * config does not choose for it:
   *
   * 1. **Self-contained (default).** It fetches its own data, runs its own
   *    validation and ships its own save button. The framework's Reset/Save
   *    governs the identity fields ONLY and never triggers, awaits or reports
   *    on the block, so neither half can block the other. The framework's save
   *    bar stays directly under the identity fields it governs, and the block
   *    renders below it.
   *
   * 2. **Joined.** The block calls `useUserCenterProfileExtra({ save, reset?,
   *    dirty? })` in its `setup()`. The Profile section then drives both halves
   *    from ONE Reset/Save pair (which moves below the block), and the block
   *    must NOT render a save button of its own. The two writes are **not
   *    atomic** - the identity fields and the app's fields live in different
   *    backends - so read the contract in
   *    `pages/account/useUserCenterProfileExtra.ts` before registering: it
   *    defines the order, what happens to the surviving half when the other
   *    fails, and how the failure is attributed to the user.
   *
   * Same source contract as `AdminUserCenterSection.component`: either a
   * component object (incl. a `defineAsyncComponent(...)` result) or a plain
   * loader (`() => import('./Block.vue')`).
   */
  extra?: Component | (() => Promise<unknown>)
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
