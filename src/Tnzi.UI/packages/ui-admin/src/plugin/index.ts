import type { App } from 'vue'
import type { Pinia } from 'pinia'
import piniaPluginPersistedstate from 'pinia-plugin-persistedstate'
import type { HttpClient } from '@tnzi/core/http'
import {
  THEME_CONTEXT_KEY,
  createThemeContext,
  mergeThemeSettings,
  type ThemeColors,
} from '@tnzi/ui'
import '../styles/index.css'
import { TNZI_ADMIN_CLIENT_KEY } from './client'
import { installDirectives } from '../directives'
import {
  provideAdminLoginConfig,
  type AdminLoginConfig,
} from './loginConfig'
import {
  provideAdminDashboardConfig,
  type AdminDashboardConfig,
} from './dashboardConfig'
import {
  provideAdminSettingsConfig,
  type AdminSettingsConfig,
} from './settingsConfig'
import {
  provideAdminChatConfig,
  type AdminChatConfig,
} from './chatConfig'
import {
  provideAdminThemeConfig,
  type AdminThemeConfig,
} from './themeConfig'

/**
 * Default palette for ui-admin when the consumer hasn't installed
 * `createTnziUi()` upstream. Cyan-500 primary gives admin a distinct
 * identity vs the @tnzi/ui default (Naive green). Consumers wanting a
 * different palette pass `themeOverride: { primary: '...' }`, or install
 * `createTnziUi({ theme: ... })` first to fully take over.
 */
export const DEFAULT_ADMIN_PRIMARY_COLOR = '#06B6D4'
export const DEFAULT_ADMIN_COLORS = {
  primary: '#06B6D4',
  info: '#2080f0',
  success: '#18a058',
  warning: '#f0a020',
  error: '#d03050',
} as const

export interface TnziUiAdminOptions {
  pinia?: Pinia
  installPersistedstate?: boolean
  globalSearchShortcut?: string
  /**
   * HttpClient that admin bridges use to talk to the backend.
   * If omitted, pages that call `useAdminClient()` will throw, and
   * bridge factories stay on their stub / mock paths.
   */
  client?: HttpClient
  /**
   * Partial theme override applied when this plugin installs its own theme
   * context (i.e. consumer didn't already call `createTnziUi()`). Use to
   * change the admin default primary color away from `#646cff`.
   *
   * When the consumer has already installed `@tnzi/ui`, this option is
   * **ignored** — the consumer's theme wins. Pass to `createTnziUi` instead.
   */
  themeOverride?: Partial<ThemeColors>
  /**
   * Phase I.7+ — configuration for the built-in `/login/:module(…)?` route.
   * Pass `brand` + `callbacks.pwdLogin` to get a working login page out of
   * the box; pass `demoAccounts` to render quick-fill buttons.
   *
   * The route still renders without this option, but the 5 form modules
   * behave as placeholders.
   */
  login?: AdminLoginConfig
  /**
   * Phase J / 0.2.71+ — configuration for the built-in Dashboard page.
   * When omitted, the page falls back to `defaultWorkbenchWidgets()`
   * (HeaderBanner + KPIs + business stats + activity timeline + tips).
   * Pass `widgets` to fully control the dashboard, `layout: 'draggable'`
   * to let users re-order cards, and an `addModules` of your own
   * widget components for app-specific cards.
   */
  dashboard?: AdminDashboardConfig
  /**
   * Configuration for the built-in Settings Center page (`/admin/settings`).
   * Pass `sections` to append app-specific sections, `hideGroups` to hide
   * built-in schema groups. When omitted the page renders framework groups only.
   */
  settings?: AdminSettingsConfig
  /**
   * Configuration for the built-in chat feature (TChatHost shell-level widget).
   * When omitted the chat launcher is enabled by default.
   */
  chat?: AdminChatConfig
  /**
   * Global admin theme configuration. `globalSync` (default true) makes the
   * shell load + apply the backend global theme snapshot for every user;
   * `presets` replaces the built-in color-scheme palette.
   */
  theme?: AdminThemeConfig
}

export interface TnziUiAdminInstance {
  uninstall(): void
}

export function createTnziUiAdmin(app: App, options: TnziUiAdminOptions = {}): TnziUiAdminInstance {
  if (options.installPersistedstate !== false && options.pinia) {
    options.pinia.use(piniaPluginPersistedstate)
  }

  if (options.client) {
    app.provide(TNZI_ADMIN_CLIENT_KEY, options.client)
  }

  // Phase I.7.2+ — install the login config so the built-in `/login/:module(…)?`
  // route can pull consumer-supplied auth callbacks + brand + demo accounts
  // without each consumer needing to override the route component itself.
  // Always provide (even when undefined) so `useAdminLoginConfig()` always
  // resolves to a stable object.
  provideAdminLoginConfig(app, options.login ?? {})

  // Phase J — install the dashboard config so the bundled dashboard page
  // can pull consumer-supplied widget descriptors. Provide null when no
  // config supplied so the page falls back to the bundled default deck.
  if (options.dashboard) {
    provideAdminDashboardConfig(app, options.dashboard)
  }

  // Settings Center — install the consumer-supplied section registry so the
  // built-in Settings page can surface app-specific sections alongside the
  // framework schema groups. Only provided when the consumer opts in.
  if (options.settings) {
    provideAdminSettingsConfig(app, options.settings)
  }

  // Chat config — provide so AdminShellRoot can read enabled/disabled state.
  // Always provide (even when undefined) with a stable empty object so
  // useAdminChatConfig() returns a consistent value.
  provideAdminChatConfig(app, options.chat ?? {})

  // Theme config - provide so AdminShellRoot can wire the global admin
  // theme sync + the preset color schemes. Stable empty object = defaults
  // (global sync on, built-in palette).
  provideAdminThemeConfig(app, options.theme ?? {})

  // Install global directives (v-permission, etc).
  installDirectives(app)

  // Provide a fallback `@tnzi/ui` theme context when the consumer has not
  // already installed `createTnziUi()`. Without this, `TThemeDrawer` (mounted
  // by `AdminShellRoot` in 0.2.3+) calls `useTheme()` during setup, the
  // injection fails, and the entire route subtree silently fails to render
  // any of the Phase A features.
  //
  // When we install the fallback, we override the default primary to
  // `#646cff` (soybean-admin signature) so admin apps have a distinct
  // identity vs the site theme. Consumers can opt out by either
  // (a) installing `createTnziUi()` first with their own colors, or
  // (b) passing `themeOverride: { primary: '...' }`.
  //
  // Detection uses `app._context.provides` — an internal-but-stable accessor.
  // The cast to `Record<symbol, unknown>` matches Vue 3.5's own internal type.
  const provides = (app as unknown as {
    _context: { provides: Record<symbol, unknown> }
  })._context.provides
  if (provides[THEME_CONTEXT_KEY] === undefined) {
    const colors: Partial<ThemeColors> = {
      ...DEFAULT_ADMIN_COLORS,
      ...(options.themeOverride ?? {}),
    }
    const defaultThemeCtx = createThemeContext(mergeThemeSettings({ colors }))
    app.provide(THEME_CONTEXT_KEY, defaultThemeCtx)
  }

  let keyHandler: ((e: KeyboardEvent) => void) | null = null
  if (typeof window !== 'undefined') {
    keyHandler = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        window.dispatchEvent(new CustomEvent('tnzi:global-search-toggle'))
      }
    }
    window.addEventListener('keydown', keyHandler)
  }

  return {
    uninstall() {
      if (keyHandler) window.removeEventListener('keydown', keyHandler)
    },
  }
}

export { TnziUiAdminResolver } from './resolver'
export { registerBrandIcon, type BrandIconData } from './brandIcon'
export { TNZI_ADMIN_CLIENT_KEY, useAdminClient } from './client'
export { defineAdminApp } from './defineAdminApp'
export { installDirectives, vPermission, vModule } from '../directives'
export type { DefineAdminAppOptions, DefineAdminAppResult } from './defineAdminApp'
export {
  ADMIN_LOGIN_CONFIG_KEY,
  provideAdminLoginConfig,
  useAdminLoginConfig,
  type AdminLoginConfig,
} from './loginConfig'
export {
  ADMIN_DASHBOARD_CONFIG_KEY,
  provideAdminDashboardConfig,
  useAdminDashboardConfig,
  type AdminDashboardConfig,
} from './dashboardConfig'
export {
  ADMIN_SETTINGS_CONFIG_KEY,
  provideAdminSettingsConfig,
  useAdminSettingsConfig,
  type AdminSettingsConfig,
  type AdminSettingsSection,
} from './settingsConfig'
export {
  ADMIN_DEEP_LINK_KEY,
  resolveDeepLinkConfig,
  tryInjectDeepLinkConfig,
  type AdminDeepLinkConfig,
  type ResolvedDeepLinkConfig,
} from './deepLinkConfig'
export {
  ADMIN_CHAT_CONFIG_KEY,
  provideAdminChatConfig,
  useAdminChatConfig,
  type AdminChatConfig,
} from './chatConfig'
export {
  ADMIN_THEME_CONFIG_KEY,
  provideAdminThemeConfig,
  useAdminThemeConfig,
  type AdminThemeConfig,
  type ThemeColorPreset,
} from './themeConfig'
export {
  fetchAdminManifest,
  type AdminManifest,
  type AdminManifestModule,
  type AdminManifestEntity,
} from '../services/admin-manifest'
export {
  fetchAdminShellModules,
  normalizeModuleName,
  type AdminShellModule,
  type AdminShellModules,
} from '../services/admin-shell-modules'
