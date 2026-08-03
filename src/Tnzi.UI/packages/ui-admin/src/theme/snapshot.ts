/**
 * Shared build / apply helpers for {@link AdminThemeSnapshot}.
 *
 * Extracted from TThemeDrawer so the same snapshot logic serves three
 * consumers: the drawer's export/import tab, the global-theme boot apply
 * (server snapshot -> every user's shell) and the super admin's
 * "save for all users" flow.
 */
import type { ThemeContext, ThemeColors } from '@tnzi/ui'
import type { useAdminThemeStore } from '../stores/useAdminThemeStore'
import type { AdminThemeSnapshot } from './admin-config'
import { applyAppearancePreset, type AdminThemePreset } from './appearance-presets'

export type AdminThemeStore = ReturnType<typeof useAdminThemeStore>

/** Serialize the current store + theme-context state into a snapshot. */
export function buildThemeSnapshot(themeStore: AdminThemeStore, ctx: ThemeContext): AdminThemeSnapshot {
  return {
    version: 1,
    exportedAt: new Date().toISOString(),
    admin: {
      layoutMode: themeStore.layoutMode,
      headerVisible: themeStore.headerVisible,
      tabVisible: themeStore.tabVisible,
      footerVisible: themeStore.footerVisible,
      breadcrumbVisible: themeStore.breadcrumbVisible,
      siderWidth: themeStore.siderWidth,
      siderCollapsedWidth: themeStore.siderCollapsedWidth,
      mixSiderWidth: themeStore.mixSiderWidth,
      headerHeight: themeStore.headerHeight,
      tabHeight: themeStore.tabHeight,
      tabStyle: themeStore.tabStyle,
      pageTransition: themeStore.pageTransition,
      pageAnimate: themeStore.pageAnimate,
      invertSider: themeStore.invertSider,
      fixedHeader: themeStore.fixedHeader,
      fixedTab: themeStore.fixedTab,
      fixedFooter: themeStore.fixedFooter,
      watermark: { ...themeStore.watermark },
      infoFollowPrimary: themeStore.infoFollowPrimary,
      tabCache: themeStore.tabCache,
      breadcrumbShowIcon: themeStore.breadcrumbShowIcon,
      multilingualVisible: themeStore.multilingualVisible,
      globalSearchVisible: themeStore.globalSearchVisible,
      fullscreenVisible: themeStore.fullscreenVisible,
      themeSchemaVisible: themeStore.themeSchemaVisible,
      reloadVisible: themeStore.reloadVisible,
      // NOTE: grayscale / colourWeakness (accessibility filters) are deliberately
      // NOT serialized - they are a PERSONAL, per-user preference (persisted
      // locally by the store), never pushed to every user via the global theme.
      closeTabByMiddleClick: themeStore.closeTabByMiddleClick,
      tabScrollAnimation: themeStore.tabScrollAnimation,
      scrollMode: themeStore.scrollMode,
      mixCollapsedWidth: themeStore.mixCollapsedWidth,
      mixChildMenuWidth: themeStore.mixChildMenuWidth,
      autoSelectFirstMenu: themeStore.autoSelectFirstMenu,
      presetPickerVisible: themeStore.presetPickerVisible,
      themeRadius: themeStore.themeRadius,
      footerHeight: themeStore.footerHeight,
      siderBg: themeStore.siderBg,
      headerBg: themeStore.headerBg,
      tabBg: themeStore.tabBg,
      footerBg: themeStore.footerBg,
      contentBg: themeStore.contentBg,
      pageHeaderBg: themeStore.pageHeaderBg,
      cardBg: themeStore.cardBg,
      siderTextColor: themeStore.siderTextColor,
      headerTextColor: themeStore.headerTextColor,
      tabTextColor: themeStore.tabTextColor,
      footerTextColor: themeStore.footerTextColor,
      contentTextColor: themeStore.contentTextColor,
      pageHeaderTextColor: themeStore.pageHeaderTextColor,
      cardTextColor: themeStore.cardTextColor,
    },
    ui: {
      mode: ctx.settings.value.mode,
      colors: { ...ctx.settings.value.colors },
    },
  }
}

export interface ApplyThemeSnapshotOptions {
  /**
   * Treat `snapshot.ui.mode` as the GLOBAL DEFAULT scheme (the boot-apply
   * path) instead of applying it unconditionally (the drawer-import path).
   *
   * Default semantics: the mode only applies when the admin hid the schema
   * toggle (`themeSchemaVisible: false` = admin-decided) or the user never
   * DIVERGED from the previously applied default. Divergence is judged
   * against `themeStore.lastAppliedDefaultMode` rather than a bare
   * `themeSchema == null` check - the context→themeSchema mirror records
   * every mode change including programmatic default applies, so after the
   * first boot apply `themeSchema` is always non-null and a null-check
   * would freeze every user on the first default forever.
   */
  modeAsDefault?: boolean
}

/**
 * Re-apply the user's own personal choice ON TOP of the global theme (called
 * after a global snapshot lands, so a non-privileged user's pick wins).
 *
 * Precedence: a full appearance LOOK (`userPresetLook` - the whole coordinated
 * preset the user picked) beats the legacy color-only overlay (`userPresetColor`).
 * Both are gated on the preset picker being enabled. `presets` (the resolved
 * appearance-look list) is required to resolve a look by name.
 */
export function overlayUserPreset(
  themeStore: AdminThemeStore,
  ctx: ThemeContext,
  presets?: AdminThemePreset[],
): void {
  if (!themeStore.presetPickerVisible) return
  // A whole personal look wins - surfaces + accent + mode + radius all apply.
  const lookName = themeStore.userPresetLook
  if (lookName && presets) {
    const look = presets.find((p) => p.name === lookName)
    if (look) {
      applyAppearancePreset(look, themeStore, ctx)
      return
    }
  }
  // Legacy: color-only overlay (primary + the info-follows-primary companion).
  if (themeStore.userPresetColor) {
    ctx.setColor('primary', themeStore.userPresetColor)
    if (themeStore.infoFollowPrimary) ctx.setColor('info', themeStore.userPresetColor)
  }
}

/**
 * Apply a snapshot to the store + theme context. Optional fields missing
 * from older snapshots keep the current value (same versioning rules as
 * the drawer import).
 *
 * Mode handling: unconditional by default (drawer import applies exactly
 * what the snapshot says); pass `modeAsDefault: true` on the global-theme
 * boot path so a user's own divergent dark/light choice survives.
 */
export function applyThemeSnapshot(snapshot: AdminThemeSnapshot, themeStore: AdminThemeStore, ctx: ThemeContext, options?: ApplyThemeSnapshotOptions): void {
  themeStore.setLayoutMode(snapshot.admin.layoutMode)
  themeStore.setHeaderVisible(snapshot.admin.headerVisible)
  themeStore.setTabVisible(snapshot.admin.tabVisible)
  themeStore.setFooterVisible(snapshot.admin.footerVisible)
  themeStore.setBreadcrumbVisible(snapshot.admin.breadcrumbVisible)
  themeStore.setSiderWidth(snapshot.admin.siderWidth)
  themeStore.setSiderCollapsedWidth(snapshot.admin.siderCollapsedWidth)
  themeStore.setMixSiderWidth(snapshot.admin.mixSiderWidth)
  themeStore.setHeaderHeight(snapshot.admin.headerHeight)
  themeStore.setTabHeight(snapshot.admin.tabHeight)
  themeStore.setTabStyle(snapshot.admin.tabStyle)
  themeStore.setPageTransition(snapshot.admin.pageTransition)
  themeStore.setPageAnimate(snapshot.admin.pageAnimate)
  if (themeStore.invertSider !== snapshot.admin.invertSider) themeStore.toggleInvertSider()
  themeStore.setFixedHeader(snapshot.admin.fixedHeader)
  themeStore.setFixedTab(snapshot.admin.fixedTab)
  themeStore.setFixedFooter(snapshot.admin.fixedFooter)
  themeStore.setWatermark(snapshot.admin.watermark)
  if (typeof snapshot.admin.infoFollowPrimary === 'boolean') {
    themeStore.setInfoFollowPrimary(snapshot.admin.infoFollowPrimary)
  }
  if (typeof snapshot.admin.tabCache === 'boolean') {
    themeStore.setTabCache(snapshot.admin.tabCache)
  }
  if (typeof snapshot.admin.breadcrumbShowIcon === 'boolean') {
    themeStore.setBreadcrumbShowIcon(snapshot.admin.breadcrumbShowIcon)
  }
  if (typeof snapshot.admin.multilingualVisible === 'boolean') {
    themeStore.setMultilingualVisible(snapshot.admin.multilingualVisible)
  }
  if (typeof snapshot.admin.globalSearchVisible === 'boolean') {
    themeStore.setGlobalSearchVisible(snapshot.admin.globalSearchVisible)
  }
  if (typeof snapshot.admin.fullscreenVisible === 'boolean') {
    themeStore.setFullscreenVisible(snapshot.admin.fullscreenVisible)
  }
  if (typeof snapshot.admin.reloadVisible === 'boolean') {
    themeStore.setReloadVisible(snapshot.admin.reloadVisible)
  }
  // grayscale / colourWeakness intentionally NOT applied from a snapshot - they
  // are a personal per-user accessibility preference (see buildThemeSnapshot),
  // so a super admin's global save must never toggle them for everyone. Any
  // value in an older snapshot is ignored.
  if (typeof snapshot.admin.closeTabByMiddleClick === 'boolean') {
    themeStore.setCloseTabByMiddleClick(snapshot.admin.closeTabByMiddleClick)
  }
  if (typeof snapshot.admin.tabScrollAnimation === 'boolean') {
    themeStore.setTabScrollAnimation(snapshot.admin.tabScrollAnimation)
  }
  if (snapshot.admin.scrollMode === 'content' || snapshot.admin.scrollMode === 'wrapper') {
    themeStore.setScrollMode(snapshot.admin.scrollMode)
  }
  if (typeof snapshot.admin.mixCollapsedWidth === 'number') {
    themeStore.setMixCollapsedWidth(snapshot.admin.mixCollapsedWidth)
  }
  if (typeof snapshot.admin.mixChildMenuWidth === 'number') {
    themeStore.setMixChildMenuWidth(snapshot.admin.mixChildMenuWidth)
  }
  if (typeof snapshot.admin.autoSelectFirstMenu === 'boolean') {
    themeStore.setAutoSelectFirstMenu(snapshot.admin.autoSelectFirstMenu)
  }
  if (typeof snapshot.admin.presetPickerVisible === 'boolean') {
    themeStore.setPresetPickerVisible(snapshot.admin.presetPickerVisible)
  }
  if (typeof snapshot.admin.themeRadius === 'number') {
    themeStore.setThemeRadius(snapshot.admin.themeRadius)
  }
  if (typeof snapshot.admin.footerHeight === 'number') {
    themeStore.setFooterHeight(snapshot.admin.footerHeight)
  }
  // Per-surface background overrides - `undefined` = absent in an older
  // snapshot (keep current), `null` = clear the override.
  if ('siderBg' in snapshot.admin) {
    themeStore.setSiderBg(snapshot.admin.siderBg ?? null)
  }
  if ('headerBg' in snapshot.admin) {
    themeStore.setHeaderBg(snapshot.admin.headerBg ?? null)
  }
  if ('tabBg' in snapshot.admin) {
    themeStore.setTabBg(snapshot.admin.tabBg ?? null)
  }
  if ('footerBg' in snapshot.admin) {
    themeStore.setFooterBg(snapshot.admin.footerBg ?? null)
  }
  if ('contentBg' in snapshot.admin) {
    themeStore.setContentBg(snapshot.admin.contentBg ?? null)
  }
  if ('pageHeaderBg' in snapshot.admin) {
    themeStore.setPageHeaderBg(snapshot.admin.pageHeaderBg ?? null)
  }
  if ('cardBg' in snapshot.admin) {
    themeStore.setCardBg(snapshot.admin.cardBg ?? null)
  }
  // Per-surface foreground (text) color - `undefined` = absent in an older
  // snapshot (keep current); `null` clears the override (back to auto).
  if ('siderTextColor' in snapshot.admin) themeStore.setSiderTextColor(snapshot.admin.siderTextColor ?? null)
  if ('headerTextColor' in snapshot.admin) themeStore.setHeaderTextColor(snapshot.admin.headerTextColor ?? null)
  if ('tabTextColor' in snapshot.admin) themeStore.setTabTextColor(snapshot.admin.tabTextColor ?? null)
  if ('footerTextColor' in snapshot.admin) themeStore.setFooterTextColor(snapshot.admin.footerTextColor ?? null)
  if ('contentTextColor' in snapshot.admin) themeStore.setContentTextColor(snapshot.admin.contentTextColor ?? null)
  if ('pageHeaderTextColor' in snapshot.admin) themeStore.setPageHeaderTextColor(snapshot.admin.pageHeaderTextColor ?? null)
  if ('cardTextColor' in snapshot.admin) themeStore.setCardTextColor(snapshot.admin.cardTextColor ?? null)
  // Colors always apply; mode follows the default-vs-user-choice rule
  // documented on ApplyThemeSnapshotOptions.
  if (!options?.modeAsDefault) {
    ctx.setMode(snapshot.ui.mode)
  } else {
    const schemaLocked = snapshot.admin.themeSchemaVisible === false
    const userDiverged =
      themeStore.themeSchema != null &&
      themeStore.themeSchema !== themeStore.lastAppliedDefaultMode
    if (schemaLocked || !userDiverged) {
      ctx.setMode(snapshot.ui.mode)
    }
    themeStore.setLastAppliedDefaultMode(snapshot.ui.mode)
  }
  if (typeof snapshot.admin.themeSchemaVisible === 'boolean') {
    themeStore.setThemeSchemaVisible(snapshot.admin.themeSchemaVisible)
  }
  for (const [role, color] of Object.entries(snapshot.ui.colors)) {
    if (color) ctx.setColor(role as keyof ThemeColors, color)
  }
}
