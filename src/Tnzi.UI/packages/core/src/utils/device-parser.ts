/**
 * Device-info parsing — turns a raw `deviceInfo` / `userAgent` string
 * (whatever the backend captured at login time) into a friendly icon +
 * short label pair for table rendering.
 *
 * Heuristics, NOT exhaustive UA parsing — we deliberately avoid pulling
 * `ua-parser-js` (~50 KB gzip) because:
 *   1. We only need OS-family + browser-family granularity, not exact
 *      version numbers.
 *   2. Almost every login event recorded by Tnzi.Identity carries a
 *      pre-formatted `deviceInfo` ("Windows 10 / Chrome 120") rather
 *      than the raw UA — the simple regex catches those just fine.
 *
 * If a future requirement needs detailed UA fields (e.g. exact engine
 * versions) we can swap the body for ua-parser-js without touching the
 * call sites.
 *
 * Sunk from `@tnzi/ui-admin/pages/_shared/device-info.ts` in 0.2.x. The
 * UI-layer concerns (iconify name, brand color tint) live in
 * `@tnzi/ui/utils/device-icon.ts`.
 */

export type DeviceOsFamily = 'windows' | 'mac' | 'linux' | 'ios' | 'android' | 'unknown'

export interface DeviceProfile {
  /** Iconify name suitable for `<TSvgIcon :icon="..." />`. */
  icon: string
  /** Short human label ("Windows · Chrome", "iOS · Safari"). */
  label: string
  /** Coarse OS family, used to colour-tint the icon. */
  osFamily: DeviceOsFamily
}

const UNKNOWN: DeviceProfile = {
  icon: 'mdi:devices',
  label: '—',
  osFamily: 'unknown',
}

const OS_PATTERNS: Array<{ test: RegExp; icon: string; family: DeviceOsFamily; label: string }> = [
  { test: /\b(iPhone|iPad|iPod|iOS)\b/i, icon: 'mdi:apple-ios', family: 'ios', label: 'iOS' },
  { test: /\bAndroid\b/i, icon: 'mdi:android', family: 'android', label: 'Android' },
  { test: /\bMac(intosh)?( OS X)?\b|\bmacOS\b/i, icon: 'mdi:apple', family: 'mac', label: 'macOS' },
  { test: /\bWindows( NT)?\b|\bWin(7|8|10|11)\b/i, icon: 'mdi:microsoft-windows', family: 'windows', label: 'Windows' },
  { test: /\bLinux\b|\bUbuntu\b|\bFedora\b|\bDebian\b/i, icon: 'mdi:linux', family: 'linux', label: 'Linux' },
]

const BROWSER_PATTERNS: Array<{ test: RegExp; label: string }> = [
  // Order matters — Edge/Chromium/Brave all carry "Chrome" in their UA;
  // check more specific brands first so we report "Edge" instead of
  // "Chrome" for users running Edge.
  { test: /\bEdg(e|A|iOS)?\/[\d.]+/i, label: 'Edge' },
  { test: /\bOPR\/[\d.]+|\bOpera\b/i, label: 'Opera' },
  { test: /\bFirefox\b/i, label: 'Firefox' },
  { test: /\bChrome\b/i, label: 'Chrome' },
  { test: /\bSafari\b/i, label: 'Safari' },
]

/**
 * Parse a raw deviceInfo / userAgent string into an icon + label pair.
 * Returns a stable "unknown" profile when nothing matches so callers
 * can render the result unconditionally without null checks.
 */
export function parseDeviceInfo(raw: string | null | undefined): DeviceProfile {
  if (!raw || typeof raw !== 'string') return UNKNOWN
  const os = OS_PATTERNS.find((p) => p.test.test(raw))
  if (!os) return UNKNOWN
  const browser = BROWSER_PATTERNS.find((p) => p.test.test(raw))
  return {
    icon: os.icon,
    label: browser ? `${os.label} · ${browser.label}` : os.label,
    osFamily: os.family,
  }
}
