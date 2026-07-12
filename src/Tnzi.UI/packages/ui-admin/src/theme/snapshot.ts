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
      recommendColor: themeStore.recommendColor,
      infoFollowPrimary: themeStore.infoFollowPrimary,
      tabCache: themeStore.tabCache,
      breadcrumbShowIcon: themeStore.breadcrumbShowIcon,
      multilingualVisible: themeStore.multilingualVisible,
      globalSearchVisible: themeStore.globalSearchVisible,
      fullscreenVisible: themeStore.fullscreenVisible,
      themeSchemaVisible: themeStore.themeSchemaVisible,
      reloadVisible: themeStore.reloadVisible,
      grayscale: themeStore.grayscale,
      colourWeakness: themeStore.colourWeakness,
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
      contentBg: themeStore.contentBg,
      containerBg: themeStore.containerBg,
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
 * Re-apply the user's own preset color ON TOP of whatever colors are
 * currently active (the one color knob a non-privileged user owns).
 * Single source of the overlay rules: gated on the picker being enabled,
 * primary plus the info-follows-primary companion.
 */
export function overlayUserPreset(themeStore: AdminThemeStore, ctx: ThemeContext): void {
  if (!themeStore.presetPickerVisible || !themeStore.userPresetColor) return
  ctx.setColor('primary', themeStore.userPresetColor)
  if (themeStore.infoFollowPrimary) ctx.setColor('info', themeStore.userPresetColor)
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
  if (typeof snapshot.admin.recommendColor === 'boolean') {
    themeStore.setRecommendColor(snapshot.admin.recommendColor)
  }
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
  if (typeof snapshot.admin.grayscale === 'boolean') {
    themeStore.setGrayscale(snapshot.admin.grayscale)
  }
  if (typeof snapshot.admin.colourWeakness === 'boolean') {
    themeStore.setColourWeakness(snapshot.admin.colourWeakness)
  }
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
  // Background color overrides - `undefined` = absent in an older snapshot
  // (keep current), `null` = clear the override.
  if ('siderBg' in snapshot.admin) {
    themeStore.setSiderBg(snapshot.admin.siderBg ?? null)
  }
  if ('headerBg' in snapshot.admin) {
    themeStore.setHeaderBg(snapshot.admin.headerBg ?? null)
  }
  if ('contentBg' in snapshot.admin) {
    themeStore.setContentBg(snapshot.admin.contentBg ?? null)
  }
  if ('containerBg' in snapshot.admin) {
    themeStore.setContainerBg(snapshot.admin.containerBg ?? null)
  }
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
