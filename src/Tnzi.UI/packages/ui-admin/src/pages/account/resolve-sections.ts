import type { Component } from 'vue'
import type {
  AdminUserCenterConfig,
  UserCenterBuiltInSectionKey,
} from '../../plugin/user-center-config'

/** A built-in section registry entry (declared in UserCenter.vue). */
export interface UserCenterBuiltInDef {
  key: UserCenterBuiltInSectionKey
  component: Component
  group: string
  order: number
  icon: string
  /** Relative i18n key for the nav label (e.g. `nav.profile`). */
  labelKey: string
}

/** A fully-resolved nav section (built-in merged with consumer config, or a
 *  consumer custom section). */
export interface ResolvedUserCenterSection {
  key: string
  label: string
  icon?: string
  /** Stable group key used for `hideGroups` matching (built-in group id or a
   *  custom label). */
  groupKey: string
  /** Display group label. */
  group: string
  order: number
  component: Component | (() => Promise<unknown>)
}

export interface ResolveSectionsDeps {
  /** Page translator (resolves a built-in label key). */
  t: (key: string) => string
  /** Permission gate for consumer custom sections. */
  can: (permission: string) => boolean
  /** Module-availability gate for consumer custom sections. */
  hasModule: (module: string) => boolean
  /** Resolve a group key to its display label. */
  groupLabel: (groupKey: string) => string
}

/**
 * Pure resolution of the User Center nav: merge the built-in registry with the
 * consumer config, then hide / regroup / reorder / override built-ins, append
 * permission+module-gated custom sections, drop hidden groups, and sort by order.
 *
 * Extracted from UserCenter.vue so the extensibility matrix
 * (hide / regroup / override / custom / hideGroups / order) is unit-testable
 * without mounting the page.
 */
export function resolveUserCenterSections(
  builtins: UserCenterBuiltInDef[],
  config: AdminUserCenterConfig,
  deps: ResolveSectionsDeps,
): ResolvedUserCenterSection[] {
  const hideSections = new Set(config.hideSections ?? [])
  const hideGroups = new Set(config.hideGroups ?? [])
  const groupOverride = config.sectionGroups ?? {}
  const orderOverride = config.sectionOrder ?? {}
  const overrides = config.overrides ?? {}

  const out: ResolvedUserCenterSection[] = []

  for (const b of builtins) {
    if (hideSections.has(b.key)) continue
    const groupKey = groupOverride[b.key] ?? b.group
    out.push({
      key: b.key,
      label: deps.t(b.labelKey),
      icon: b.icon,
      groupKey,
      group: deps.groupLabel(groupKey),
      order: orderOverride[b.key] ?? b.order,
      component: overrides[b.key] ?? b.component,
    })
  }

  for (const s of config.sections ?? []) {
    if (s.permission && !deps.can(s.permission)) continue
    if (s.module && !deps.hasModule(s.module)) continue
    const groupKey = groupOverride[s.key] ?? s.group ?? 'App'
    out.push({
      key: `custom:${s.key}`,
      label: s.label,
      icon: s.icon,
      groupKey,
      group: deps.groupLabel(groupKey),
      order: orderOverride[s.key] ?? s.order ?? 100,
      component: s.component,
    })
  }

  return out.filter((s) => !hideGroups.has(s.groupKey)).sort((a, b) => a.order - b.order)
}
