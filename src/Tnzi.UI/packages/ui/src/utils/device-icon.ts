/**
 * Device-icon brand colour mapping. The CSS colour value for each OS
 * family is kept here (UI layer) rather than in `@tnzi/core` because the
 * mapping mixes brand-hex (Windows blue, Android green, Linux yellow)
 * with theme-token references (`var(--tnzi-base-text)`) - the
 * theme-token half is meaningless outside a Tnzi UI shell.
 *
 * Sunk from `@tnzi/ui-admin/pages/_shared/device-info.ts` in 0.2.x.
 * The pure UA-parsing half lives in `@tnzi/core/utils/device-parser.ts`.
 */
import type { DeviceOsFamily } from '@tnzi/core'

/**
 * Brand-hex colours for each OS family. These are the upstream brand
 * guidelines (microsoft.com / android.com / linux.org press kits) and
 * should not be overridden lightly - the recognisability of the tint is
 * the whole point. Consumers wanting a different palette can pass an
 * override table to {@link deviceIconColor}.
 */
export const DEFAULT_DEVICE_BRAND_COLORS: Readonly<Record<DeviceOsFamily, string>> = Object.freeze({
  windows: '#00a4ef',
  android: '#3ddc84',
  linux: '#fcc624',
  // mac/ios use the active theme text colour so the Apple logo reads
  // properly against both light and dark backgrounds (a fixed grey would
  // disappear in dark mode).
  mac: 'var(--tnzi-base-text)',
  ios: 'var(--tnzi-base-text)',
  unknown: 'var(--tnzi-base-text-muted)',
})

/**
 * Resolve a CSS colour for the OS family icon. Returns a brand-coloured
 * tint when recognised, otherwise the muted text colour. Plain CSS
 * strings so callers can inline without importing extra design tokens.
 *
 * Pass an override table to substitute project-specific brand colours
 * (rare - most apps use the upstream brand hex unchanged).
 */
export function deviceIconColor(
  family: DeviceOsFamily,
  override?: Partial<Record<DeviceOsFamily, string>>,
): string {
  const table = override ? { ...DEFAULT_DEVICE_BRAND_COLORS, ...override } : DEFAULT_DEVICE_BRAND_COLORS
  return table[family] ?? table.unknown
}
