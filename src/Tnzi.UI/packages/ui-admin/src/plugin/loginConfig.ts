/**
 * `useAdminLoginConfig()` — install-time configuration for the built-in
 * `/login/:module(…)?` route shipped by `@tnzi/ui-admin` (Phase I.7+).
 *
 * The login route component (`pages/login/index.vue`) consumes this via
 * `inject()` to fetch consumer-supplied auth callbacks, brand title,
 * demo accounts, etc. Without a config the page still renders — the
 * 5 placeholder modules behave as no-ops and show the "Phase I.7.x will
 * implement" hint.
 *
 * Consumers pass the config via:
 *
 * ```ts
 * defineAdminApp({
 *   client: http,
 *   login: {
 *     brand: 'Acme Admin',
 *     callbacks: {
 *       pwdLogin: async ({ userName, password, remember }) => { … },
 *     },
 *     demoAccounts: [
 *       { key: 'admin', label: 'Admin', userName: 'admin', password: 'Admin@123456' },
 *     ],
 *   },
 * })
 * ```
 *
 * The factory forwards `login` to `createTnziUiAdmin()`, which provides
 * `LOGIN_CONFIG_KEY` on the app so the route's `setup()` can inject it.
 */
import type { App, InjectionKey } from 'vue'
import { inject } from 'vue'
import type {
  LoginCallbacks,
  LoginDemoAccount,
} from '../pages/login/useLoginContext'

export interface AdminLoginConfig {
  /** Brand title shown next to the logo. */
  brand?: string
  /** Iconify icon name for the brand logo. */
  brandIcon?: string
  /** Pixel size of the brand logo. */
  brandIconSize?: number
  /** Translation function (locale-reactive). */
  translate?: (key: string, fallback?: string) => string
  /** Auth callbacks for each module — `pwdLogin` is the minimum useful set. */
  callbacks?: LoginCallbacks
  /**
   * Demo accounts rendered by `PwdLoginModule` as quick-fill buttons.
   * Empty array hides the demo section.
   */
  demoAccounts?: LoginDemoAccount[]
  /**
   * Header user-avatar dropdown configuration (Phase I.7.7+). The
   * `TAdminUserAvatar` component in the admin header reads from this so
   * "User Center" and "Logout" both navigate / clear the session via the
   * consumer's auth store without each consumer wiring its own avatar.
   *
   * `userName` is read once at render time — consumers wanting reactivity
   * (e.g. binding to `auth.user?.name`) should pass a getter via Vue's
   * normal reactivity (provide a computed-ref-like or override the user
   * slot entirely).
   */
  user?: {
    /** Display name in the dropdown trigger. Falls back to "User". */
    userName?: string
    /** Iconify avatar icon. Defaults to `mdi:account-circle-outline`. */
    avatarIcon?: string
    /** Click handler for "User Center" dropdown option. */
    onUserCenter?: () => void | Promise<void>
    /** Click handler for "Logout" — called after the confirm dialog. */
    onLogout?: () => void | Promise<void>
    /** When false, render a "Sign in" button instead of the avatar. */
    signedIn?: boolean
    /** Click handler when an unsigned user clicks "Sign in". */
    onSignIn?: () => void | Promise<void>
  }
  /**
   * Footer configuration (Phase I.7.9+). When omitted the shell still
   * renders a default `Copyright © {year} {brand}` line so the admin
   * never ships with a bare grey strip.
   */
  footer?: {
    /** Override the copyright line. Defaults to `Copyright © {year} {brand}`. */
    copyright?: string
    /** Optional footer links (e.g. ToS / Privacy). Empty hides the link row. */
    links?: Array<{ label: string; href: string; external?: boolean }>
  }
}

export const ADMIN_LOGIN_CONFIG_KEY: InjectionKey<AdminLoginConfig> = Symbol(
  'tnzi-admin-login-config',
)

export function provideAdminLoginConfig(app: App, config: AdminLoginConfig): void {
  app.provide(ADMIN_LOGIN_CONFIG_KEY, config)
}

/**
 * Inject the consumer-supplied login config. Returns an empty object when
 * no `defineAdminApp({ login: … })` was passed — the route then falls back
 * to its own placeholder defaults.
 */
export function useAdminLoginConfig(): AdminLoginConfig {
  return inject(ADMIN_LOGIN_CONFIG_KEY, {})
}
