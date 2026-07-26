import { getCurrentInstance, inject, type InjectionKey } from 'vue'

/**
 * App-wide deep-link switch, set via `defineAdminApp({ deepLink })`.
 *
 * - `false` - kill switch: NO UI state enters the URL query anywhere in the
 *   app (built-in pages included) - neither overlay open-states
 *   (`?detail=` / `?roles=` / any custom key) nor active sections
 *   (`?section=`). Overrides per-page options, because the built-in pages'
 *   options are the framework's choice, not the consumer's.
 * - `{ detail?, section? }` - disable one channel only: `detail` covers every
 *   overlay open-state key regardless of its name; `section` covers every
 *   section key.
 * - `true` / omitted - default: per-page options decide (CRUD overlays on by
 *   default, everything else opt-in).
 */
export type AdminDeepLinkConfig = boolean | { detail?: boolean; section?: boolean }

export interface ResolvedDeepLinkConfig {
  /** Overlay open-state channel (`?detail=view:<id>` and custom keys). */
  detail: boolean
  /** Active-section channel (`?section=` and custom keys). */
  section: boolean
}

const DEEP_LINK_DEFAULTS: ResolvedDeepLinkConfig = { detail: true, section: true }

export const ADMIN_DEEP_LINK_KEY: InjectionKey<ResolvedDeepLinkConfig> =
  Symbol('tnzi-admin-deep-link')

/** Normalise the consumer-facing shape to a full `{ detail, section }`. */
export function resolveDeepLinkConfig(
  config: AdminDeepLinkConfig | undefined,
): ResolvedDeepLinkConfig {
  if (config === false) return { detail: false, section: false }
  if (config === true || config == null) return { ...DEEP_LINK_DEFAULTS }
  return { detail: config.detail ?? true, section: config.section ?? true }
}

/**
 * Read the app-wide deep-link config from the current component tree.
 * Safe outside a component (bare composable calls in unit tests): falls back
 * to the all-enabled defaults without triggering Vue's inject() warning.
 */
export function tryInjectDeepLinkConfig(): ResolvedDeepLinkConfig {
  if (!getCurrentInstance()) return DEEP_LINK_DEFAULTS
  return inject(ADMIN_DEEP_LINK_KEY, DEEP_LINK_DEFAULTS)
}
