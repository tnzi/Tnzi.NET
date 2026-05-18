/**
 * Built-in theme presets — soybean-style four quick-pick themes.
 *
 * Each preset is a flat snapshot of the admin theme tokens consumers can
 * apply via `useAdminThemeStore().applyPreset(preset)` or compose into
 * their own pickers. The shapes match what `useAdminThemeStore` reads;
 * unrecognized keys are ignored.
 *
 * `default` matches the soybean default palette (#646cff primary,
 * comfortable spacing, fade-slide transitions).
 * `dark` flips to a dark scheme + slightly larger radius.
 * `compact` reduces sider / tab heights for dense ops dashboards.
 * `azir` ships an alt brand palette (cyan primary) for variety.
 */

export interface ThemePreset {
  /** Stable key for the preset selector. */
  name: string
  /** Display label (English; consumers translate themselves). */
  label: string
  /** Short tagline shown under the label. */
  description: string
  /** Hex primary color (drives `--tnzi-primary`). */
  primaryColor: string
  /** Pixel radius (drives `--tnzi-admin-radius`). */
  themeRadius: number
  /** `light` / `dark` (drives the `dark` class on html). */
  themeScheme: 'light' | 'dark' | 'auto'
  /** Sider / tab / header sizing patch. */
  layout: {
    siderWidth: number
    siderCollapsedWidth: number
    headerHeight: number
    tabHeight: number
  }
  /** Page transition pick. */
  pageTransition:
    | 'fade'
    | 'fade-slide'
    | 'fade-bottom'
    | 'fade-scale'
    | 'zoom-fade'
    | 'zoom-out'
}

export const defaultPreset: ThemePreset = {
  name: 'default',
  label: 'Default',
  description: 'Soybean-style violet + comfortable spacing.',
  primaryColor: '#646cff',
  themeRadius: 6,
  themeScheme: 'light',
  layout: {
    siderWidth: 220,
    siderCollapsedWidth: 64,
    headerHeight: 56,
    tabHeight: 44,
  },
  pageTransition: 'fade-slide',
}

export const darkPreset: ThemePreset = {
  name: 'dark',
  label: 'Dark',
  description: 'Same palette, dark scheme + softer radius.',
  primaryColor: '#646cff',
  themeRadius: 8,
  themeScheme: 'dark',
  layout: {
    siderWidth: 220,
    siderCollapsedWidth: 64,
    headerHeight: 56,
    tabHeight: 44,
  },
  pageTransition: 'fade-slide',
}

export const compactPreset: ThemePreset = {
  name: 'compact',
  label: 'Compact',
  description: 'Tighter heights + 4 px radius for dense ops dashboards.',
  primaryColor: '#646cff',
  themeRadius: 4,
  themeScheme: 'light',
  layout: {
    siderWidth: 200,
    siderCollapsedWidth: 56,
    headerHeight: 48,
    tabHeight: 36,
  },
  pageTransition: 'fade',
}

export const azirPreset: ThemePreset = {
  name: 'azir',
  label: 'Azir',
  description: 'Cyan brand variant for product-marketing dashboards.',
  primaryColor: '#0ea5e9',
  themeRadius: 8,
  themeScheme: 'light',
  layout: {
    siderWidth: 220,
    siderCollapsedWidth: 64,
    headerHeight: 56,
    tabHeight: 44,
  },
  pageTransition: 'zoom-fade',
}

export const themePresets: ThemePreset[] = [
  defaultPreset,
  darkPreset,
  compactPreset,
  azirPreset,
]

/**
 * Serialize the full admin theme settings to a JSON string suitable for
 * download / sharing. Consumer is expected to assemble the snapshot
 * (typically `JSON.stringify(themeStore.$state, null, 2)`) — this
 * helper just adds the standard wrapper with a version stamp.
 */
export function exportThemeConfig(snapshot: Record<string, unknown>): string {
  return JSON.stringify(
    {
      version: 1,
      exportedAt: new Date().toISOString(),
      settings: snapshot,
    },
    null,
    2,
  )
}

/**
 * Parse an exported config back into a settings snapshot. Throws if the
 * payload is malformed or version-incompatible. Consumer applies the
 * returned object to the theme store.
 */
export function importThemeConfig(json: string): Record<string, unknown> {
  const payload = JSON.parse(json) as {
    version?: number
    settings?: Record<string, unknown>
  }
  if (typeof payload !== 'object' || payload === null) {
    throw new Error('Invalid theme config: not a JSON object')
  }
  if (payload.version !== 1) {
    throw new Error(
      `Unsupported theme config version: ${String(payload.version)}; expected 1`,
    )
  }
  if (!payload.settings || typeof payload.settings !== 'object') {
    throw new Error('Invalid theme config: missing `settings` object')
  }
  return payload.settings
}
