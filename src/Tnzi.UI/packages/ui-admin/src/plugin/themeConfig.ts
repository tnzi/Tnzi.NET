import type { App, InjectionKey } from 'vue'
import { inject } from 'vue'

/** A color-scheme preset offered in the theme drawer / user preset picker. */
export interface ThemeColorPreset {
  name: string
  primary: string
}

export interface AdminThemeConfig {
  /**
   * Sync the admin theme from the backend global snapshot (default true).
   * When enabled, the shell loads `GET /appearance/admin-theme` after
   * sign-in and applies it for every user; privileged users
   * (system.appearance.update - super admins by default) edit the global
   * snapshot through the theme drawer's "save for all users" action.
   * Set to `false` for a purely local (legacy) theme experience.
   */
  globalSync?: boolean
  /**
   * Preset color schemes offered to users. Replaces the built-in
   * 12-color palette in both the drawer's Preset tab and the
   * non-privileged users' preset picker.
   */
  presets?: ThemeColorPreset[]
}

export const ADMIN_THEME_CONFIG_KEY: InjectionKey<AdminThemeConfig> = Symbol(
  'tnzi-admin-theme-config',
)

export function provideAdminThemeConfig(app: App, config: AdminThemeConfig): void {
  app.provide(ADMIN_THEME_CONFIG_KEY, config)
}

export function useAdminThemeConfig(): AdminThemeConfig | null {
  return inject(ADMIN_THEME_CONFIG_KEY, null)
}
