/**
 * Appearance presets - a curated *whole look*, not just a primary color.
 *
 * A preset bundles the settings of the Appearance tab into one coherent look:
 * the light/dark mode, the accent (theme) color, the corner radius, and the
 * per-surface background colors (which drive the adaptive foreground). Selecting
 * one applies the complete look in a single click - the color pickers alone
 * can't express "midnight-navy chrome with an indigo accent".
 *
 * Scope note: presets deliberately cover the *Appearance* axis (mode / color /
 * radius / surfaces) and NOT the *structural* Layout axis. Applying a look never
 * changes the user's layout mode or tab style - those stay the user's choice.
 * (`layoutMode` / `tabStyle` remain optional on the type so a consumer CAN ship
 * a preset that also sets them, but the built-ins never do.)
 *
 * This is distinct from the primary-only color swatches (`ThemeColorPreset`)
 * that feed the color pickers and the non-privileged user's preset picker.
 */
import type { ThemeColors, ThemeContext } from '@tnzi/ui'
import type { AdminLayoutMode, TabStyle } from '../stores/useAdminThemeStore'
import type { AdminThemeStore } from './snapshot'

export interface AdminThemePreset {
  /** Stable id (also the i18n label key suffix). */
  name: string
  /** Human label. When omitted the drawer derives it from `name`. */
  label?: string
  /** Primary (accent) color - shown as the card swatch and applied as the accent. */
  primary: string
  /** Light / dark / auto mode. */
  mode?: 'light' | 'dark' | 'auto'
  /** Extra role colors (primary is taken from `primary`). */
  colors?: Partial<Omit<ThemeColors, 'primary'>>
  /** Corner radius (0-16). */
  themeRadius?: number
  /** Built-in dark sider shorthand (ignored when `siderBg` is set). */
  invertSider?: boolean
  /** Per-surface background overrides - omitted / null clears the surface. */
  siderBg?: string | null
  headerBg?: string | null
  tabBg?: string | null
  footerBg?: string | null
  contentBg?: string | null
  /** Content-area container surfaces - page-header bar + content cards/lists. */
  pageHeaderBg?: string | null
  cardBg?: string | null
  /** Per-surface foreground (text) color - omitted/null = auto (derive from bg). */
  siderTextColor?: string | null
  headerTextColor?: string | null
  tabTextColor?: string | null
  footerTextColor?: string | null
  contentTextColor?: string | null
  pageHeaderTextColor?: string | null
  cardTextColor?: string | null
  /** Structural knobs - NOT set by the built-in looks, but available so a
   *  consumer can ship a preset that also pins the layout / tab style. */
  layoutMode?: AdminLayoutMode
  tabStyle?: TabStyle
}

/**
 * Built-in curated looks - 18 presets across clearly distinct style FAMILIES
 * rather than 14 shades of "light body + dark sider". Sources: established
 * palettes verified against their official pages (Flexoki, Solarized,
 * Catppuccin Latte, Tokyo Night, Rosé Pine, Gruvbox, Nord) and product
 * aesthetics (Vercel/Geist monochrome, Stripe navy, Slack aubergine,
 * Supabase near-black + emerald), applied through color-craft rules from the
 * research pass (Refactoring UI, Radix, Material 3):
 *
 *  - Distinctness lives in the hue family of the CHROME + the canvas
 *    temperature (warm paper vs cool slate) + radius personality - not in the
 *    accent alone. Users fine-tune shades themselves; presets stake out
 *    territory.
 *  - Colored chrome makes a deliberate header decision: either UNIFIED (the
 *    header joins the sider - aubergine, espresso, all dark looks) or FRAMED
 *    (dark sider + light floating header - navy, forest, wine). Unified looks
 *    keep their identity in the `horizontal` layout (no sider there); framed
 *    looks degrade gracefully to tinted canvas + accent.
 *  - Dark looks never use flat black-on-black: the card sits one lightness
 *    step above the canvas (elevation by lightness). The one near-black OLED
 *    look (terminal) is a deliberate aesthetic, paired with a punchy accent.
 *  - Radius encodes personality: 0-4 editorial/technical, 6-8 modern default,
 *    10-12 soft/consumer.
 *  - Foreground is part of the palette: looks whose source palette defines its
 *    own foreground set `siderTextColor` (Flexoki warm gray, Solarized base1,
 *    Gruvbox cream, Nord snow, Tokyo periwinkle-gray, Rosé Pine subtle, Ayu
 *    mirage fg) so the chrome text carries the palette's temperature instead
 *    of a generic white. Active menu items on CUSTOM dark chrome are pinned
 *    near-white by polish.css (`data-tnzi-sider-tone` / `data-tnzi-header-tone`)
 *    because naive's primary-colored active label melts into same-hue chrome.
 *  - Accents must survive both button fills and menu labels: light/bright
 *    accents (mint, amber, frost) are only used in DARK looks (naive dark
 *    buttons render dark text on primary); light looks keep accents dark
 *    enough for white button text. Near-black accents are banned outright -
 *    every primary-tinted chip/avatar/active-pill turns gray-on-gray mud.
 *
 * Each is self-contained: applying it establishes a complete appearance, so
 * surfaces it does not mention are cleared to their defaults
 * (see {@link applyAppearancePreset}).
 */
export const BUILTIN_APPEARANCE_PRESETS: AdminThemePreset[] = [
  // ── Baseline ──
  // Shipped default - light body, built-in dark sider, Naive blue. Applied via
  // the "Reset to default" path (see TThemeDrawer.applyLook), so its literals
  // mirror the store's reset() defaults (radius 4). The accent is whatever the
  // app configured as its default primary, not necessarily this hex.
  { name: 'default', primary: '#2080F0', mode: 'light', invertSider: true, themeRadius: 4 },

  // ── All-light ──
  // Cloud - clean cool light (Radix slate neutrals): soft slate nav rail,
  // white header/cards, calm blue. The safe modern SaaS light theme.
  { name: 'cloud', primary: '#2563EB', mode: 'light', invertSider: false, themeRadius: 8, siderBg: '#F1F3F5', contentBg: '#F8F9FA' },
  // Mint - fresh all-light teal: minty nav rail, teal-tinted canvas, deep
  // teal accent. Airy and energetic without a dark surface anywhere.
  { name: 'mint', primary: '#0D9488', mode: 'light', invertSider: false, themeRadius: 10, siderBg: '#CCFBF1', headerBg: '#F0FDFA', contentBg: '#F0FDFA' },
  // Carbon - IBM-style enterprise flat: pure white chrome on a flat gray
  // canvas, iconic blue, ZERO radius. Dense, square, engineered.
  { name: 'carbon', primary: '#0F62FE', mode: 'light', invertSider: false, themeRadius: 0, siderBg: '#FFFFFF', headerBg: '#FFFFFF', contentBg: '#F4F4F4' },
  // Latte - Catppuccin Latte pastel: lavender-gray chrome, mauve accent,
  // generous radius. Soft, friendly, consumer-grade.
  { name: 'latte', primary: '#8839EF', mode: 'light', invertSider: false, themeRadius: 12, siderBg: '#E6E9EF', headerBg: '#EFF1F5', contentBg: '#EFF1F5' },
  // Dawn - Rosé Pine Dawn: warm rose-tinted ivory chrome, muted rose accent.
  // The warm-pastel counterpart to Latte's cool lavender.
  { name: 'dawn', primary: '#B4637A', mode: 'light', invertSider: false, themeRadius: 10, siderBg: '#F2E9E1', headerBg: '#FAF4ED', contentBg: '#FAF4ED', pageHeaderBg: '#FFFAF3', cardBg: '#FFFAF3' },

  // ── Warm paper (editorial) ──
  // Paper - Flexoki ink-on-paper: warm cream canvas, warm-black sider (resting
  // menu text in Flexoki's own warm gray), ink orange accent, sharp corners.
  { name: 'paper', primary: '#DA702C', mode: 'light', invertSider: true, themeRadius: 4, siderBg: '#1C1B1A', headerBg: '#FFFCF0', contentBg: '#FFFCF0', pageHeaderBg: '#F2F0E5', siderTextColor: '#CECDC3' },
  // Solarized - the classic low-contrast cream world: base03 teal-navy sider
  // with base1 resting text, cream cards floating on a deeper cream canvas.
  { name: 'solarized', primary: '#268BD2', mode: 'light', invertSider: true, themeRadius: 4, siderBg: '#002B36', headerBg: '#FDF6E3', contentBg: '#EEE8D5', pageHeaderBg: '#FDF6E3', cardBg: '#FDF6E3', siderTextColor: '#93A1A1' },

  // ── Colored chrome on light content ──
  // Navy - fintech trust (Stripe): deep navy sider FRAMING light content,
  // floating white header, electric blurple accent on a cool tinted canvas.
  { name: 'navy', primary: '#635BFF', mode: 'light', invertSider: true, themeRadius: 8, siderBg: '#0A2540', contentBg: '#F6F9FC' },
  // Forest - natural / finance: deep green frame, emerald accent, faintly
  // green-tinted canvas (neutrals tinted toward the accent).
  { name: 'forest', primary: '#16A34A', mode: 'light', invertSider: true, themeRadius: 8, siderBg: '#14532D', contentBg: '#F0FDF4' },
  // Aubergine - Slack's signature: UNIFIED aubergine chrome (sider + header
  // one block, so the identity survives the horizontal layout), deep-purple
  // accent, neutral canvas.
  { name: 'aubergine', primary: '#611F69', mode: 'light', invertSider: true, themeRadius: 8, siderBg: '#3F0E40', headerBg: '#3F0E40', contentBg: '#F8F8F8' },
  // Wine - refined crimson frame (rose-950 sider), rose-tinted canvas.
  { name: 'wine', primary: '#E11D48', mode: 'light', invertSider: true, themeRadius: 10, siderBg: '#4C0519', contentBg: '#FFF1F2' },
  // Espresso - warm boutique: UNIFIED espresso chrome + caramel accent (an
  // analogous warm pairing), soft warm canvas. Cozy, artisanal.
  { name: 'espresso', primary: '#D97706', mode: 'light', invertSider: true, themeRadius: 8, siderBg: '#2B211C', headerBg: '#2B211C', contentBg: '#FAF7F2' },
  // Steel - UNIFIED slate chrome (neutral cool gray-navy, survives the
  // horizontal layout) + azure accent on a cool canvas. Industrial, composed.
  { name: 'steel', primary: '#0284C7', mode: 'light', invertSider: true, themeRadius: 6, siderBg: '#1E293B', headerBg: '#1E293B', contentBg: '#F1F5F9' },
  // Graphite - pure neutral ink frame + a lime pop accent on a neutral
  // canvas. The one high-energy accent in the light set.
  { name: 'graphite', primary: '#65A30D', mode: 'light', invertSider: true, themeRadius: 6, siderBg: '#171717', contentBg: '#FAFAFA' },

  // ── Dark ──
  // Nord - the beloved arctic palette (Polar Night + Frost accent). Cards use
  // Polar Night `nord1`; resting sider text in Snow Storm `nord4`.
  { name: 'nord', primary: '#88C0D0', mode: 'dark', invertSider: false, themeRadius: 8, siderBg: '#2E3440', headerBg: '#3B4252', contentBg: '#242933', cardBg: '#3B4252', siderTextColor: '#D8DEE9' },
  // Gruvbox - retro warm dark: hard-contrast warm blacks + bright orange and
  // the signature warm-cream foreground on the sider. Nostalgic terminal.
  { name: 'gruvbox', primary: '#FE8019', mode: 'dark', invertSider: false, themeRadius: 4, siderBg: '#1D2021', headerBg: '#282828', contentBg: '#282828', cardBg: '#32302F', siderTextColor: '#EBDBB2' },
  // Terminal - deliberate OLED near-black + electric emerald (Supabase-style):
  // the one place a punchy accent on near-black is correct. Sharp corners.
  { name: 'terminal', primary: '#3ECF8E', mode: 'dark', invertSider: false, themeRadius: 2, siderBg: '#0A0A0A', headerBg: '#0A0A0A', contentBg: '#0A0A0A', cardBg: '#161616' },
]

/**
 * Apply a full appearance preset to the store + theme context.
 *
 * The accent (and its info-follows-primary companion) always apply, as do mode
 * and radius when the preset specifies them. The surface fields DO fully apply
 * - including clearing to `null` when unspecified - because they collectively
 * define the preset's look; leaving a stale custom background around would
 * corrupt it. `layoutMode` / `tabStyle` only apply when a (custom) preset opts
 * into them, so the built-in looks never disturb the user's layout.
 */
export function applyAppearancePreset(
  preset: AdminThemePreset,
  themeStore: AdminThemeStore,
  ctx: ThemeContext,
): void {
  ctx.setColor('primary', preset.primary)
  // Mirror the "Info color follows primary" toggle (same as the picker path).
  if (themeStore.infoFollowPrimary) ctx.setColor('info', preset.primary)
  if (preset.colors) {
    for (const [role, color] of Object.entries(preset.colors)) {
      if (color) ctx.setColor(role as keyof ThemeColors, color)
    }
  }
  if (preset.mode) ctx.setMode(preset.mode)
  if (typeof preset.themeRadius === 'number') themeStore.setThemeRadius(preset.themeRadius)
  if (typeof preset.invertSider === 'boolean' && preset.invertSider !== themeStore.invertSider) {
    themeStore.toggleInvertSider()
  }
  // Structural knobs - only when a custom preset opts in.
  if (preset.layoutMode) themeStore.setLayoutMode(preset.layoutMode)
  if (preset.tabStyle) themeStore.setTabStyle(preset.tabStyle)
  // Surfaces define the look - apply each (clearing unspecified ones).
  themeStore.setSiderBg(preset.siderBg ?? null)
  themeStore.setHeaderBg(preset.headerBg ?? null)
  themeStore.setTabBg(preset.tabBg ?? null)
  themeStore.setFooterBg(preset.footerBg ?? null)
  themeStore.setContentBg(preset.contentBg ?? null)
  themeStore.setPageHeaderBg(preset.pageHeaderBg ?? null)
  themeStore.setCardBg(preset.cardBg ?? null)
  // Foreground colors - clear to auto unless the look forces one.
  themeStore.setSiderTextColor(preset.siderTextColor ?? null)
  themeStore.setHeaderTextColor(preset.headerTextColor ?? null)
  themeStore.setTabTextColor(preset.tabTextColor ?? null)
  themeStore.setFooterTextColor(preset.footerTextColor ?? null)
  themeStore.setContentTextColor(preset.contentTextColor ?? null)
  themeStore.setPageHeaderTextColor(preset.pageHeaderTextColor ?? null)
  themeStore.setCardTextColor(preset.cardTextColor ?? null)
}
