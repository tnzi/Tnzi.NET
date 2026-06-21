/**
 * Admin theme configuration serialization helpers.
 *
 * Exposes import / export / clipboard helpers so the Settings Drawer's Preset
 * tab can let users save a snapshot of their theme + layout settings as JSON,
 * share it, or load it on another machine.
 *
 * Note: only fields that are actually persisted (see `useAdminThemeStore`
 * `persist.pick`) plus the @tnzi/ui theme settings are serialized. Transient
 * runtime state (mobile detection, breakpoint) is intentionally excluded.
 */

import type {
  AdminLayoutMode,
  PageTransition,
  TabStyle,
  WatermarkSettings,
} from '../stores/useAdminThemeStore'

export interface AdminThemeSnapshotV1 {
  version: 1
  exportedAt: string
  admin: {
    layoutMode: AdminLayoutMode
    headerVisible: boolean
    tabVisible: boolean
    footerVisible: boolean
    breadcrumbVisible: boolean
    siderWidth: number
    siderCollapsedWidth: number
    mixSiderWidth: number
    headerHeight: number
    tabHeight: number
    tabStyle: TabStyle
    pageTransition: PageTransition
    pageAnimate: boolean
    invertSider: boolean
    fixedHeader: boolean
    fixedTab: boolean
    fixedFooter: boolean
    watermark: WatermarkSettings
    /** Phase D additions — optional in snapshots written by older
        versions; readers default to the current store defaults when
        the field is missing. Producing a snapshot always includes them. */
    recommendColor?: boolean
    infoFollowPrimary?: boolean
    tabCache?: boolean
    breadcrumbShowIcon?: boolean
    multilingualVisible?: boolean
    globalSearchVisible?: boolean
    /** Phase F additions — same versioning rules. */
    grayscale?: boolean
    colourWeakness?: boolean
    /** Phase G addition — same versioning rules. */
    closeTabByMiddleClick?: boolean
    /** Tab-bar active-tab auto-scroll animation (smooth vs instant). */
    tabScrollAnimation?: boolean
    /** Phase H1 — scroll mode (content vs wrapper). */
    scrollMode?: 'content' | 'wrapper'
    /** Phase H2 — mix-layout specific widths + auto-select toggle. */
    mixCollapsedWidth?: number
    mixChildMenuWidth?: number
    autoSelectFirstMenu?: boolean
    /** Theme Drawer additions — radius slider + footer height knob. */
    themeRadius?: number
    footerHeight?: number
    /** Background color overrides — `null` clears the override (falls back
        to the default token). `undefined` means the field was missing from
        an older snapshot and should be skipped (leave current value). */
    siderBg?: string | null
    headerBg?: string | null
    contentBg?: string | null
    containerBg?: string | null
  }
  ui: {
    mode: 'light' | 'dark' | 'auto'
    colors: Record<string, string>
  }
}

export type AdminThemeSnapshot = AdminThemeSnapshotV1

export function isValidSnapshot(value: unknown): value is AdminThemeSnapshot {
  if (!value || typeof value !== 'object') return false
  const candidate = value as { version?: unknown; admin?: unknown; ui?: unknown }
  if (candidate.version !== 1) return false
  if (!candidate.admin || typeof candidate.admin !== 'object') return false
  if (!candidate.ui || typeof candidate.ui !== 'object') return false
  return true
}

export function snapshotToJson(snapshot: AdminThemeSnapshot): string {
  return JSON.stringify(snapshot, null, 2)
}

export function parseSnapshot(json: string): AdminThemeSnapshot {
  const parsed: unknown = JSON.parse(json)
  if (!isValidSnapshot(parsed)) {
    throw new Error('Invalid theme snapshot: missing or wrong version')
  }
  return parsed
}

/**
 * Copy snapshot JSON to clipboard. Returns true on success.
 * Best-effort — silently returns false if clipboard API unavailable
 * (insecure context, headless test env, etc.).
 */
export async function copySnapshotToClipboard(
  snapshot: AdminThemeSnapshot,
): Promise<boolean> {
  if (typeof navigator === 'undefined' || !navigator.clipboard) {
    return false
  }
  try {
    await navigator.clipboard.writeText(snapshotToJson(snapshot))
    return true
  } catch {
    return false
  }
}

/**
 * Trigger a JSON file download in the browser.
 * Filename defaults to `tnzi-admin-theme-{ISO date}.json`.
 */
export function downloadSnapshot(
  snapshot: AdminThemeSnapshot,
  filename?: string,
): void {
  if (typeof document === 'undefined') return
  const json = snapshotToJson(snapshot)
  const blob = new Blob([json], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  const date = new Date().toISOString().slice(0, 10)
  link.href = url
  link.download = filename ?? `tnzi-admin-theme-${date}.json`
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
}
