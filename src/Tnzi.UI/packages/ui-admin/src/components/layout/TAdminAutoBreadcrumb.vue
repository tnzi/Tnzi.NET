<script setup lang="ts">
/**
 * `TAdminAutoBreadcrumb` - route-driven wrapper around `TAdminBreadcrumb`.
 *
 * Walks `route.matched`, skips the bare `/admin` shell root and any
 * route flagged `hideInMenu`, and emits the resulting items into the
 * existing items-driven `TAdminBreadcrumb`. Icons are pulled from the
 * same `DEFAULT_ROUTE_ICONS` map the sidebar uses, so the breadcrumb
 * stays visually consistent with the active menu row.
 *
 * Mirrors `soybean-admin-example/src/layouts/modules/global-breadcrumb/index.vue`
 * in spirit - soybean drives its breadcrumb from a separate
 * `routeStore.breadcrumbs` builder, but the matched-array walk
 * produces the same result for a standard parent→child route tree.
 */
import { computed } from 'vue'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded } from 'vue-router'
import TAdminBreadcrumb, { type TAdminBreadcrumbItem } from './TAdminBreadcrumb.vue'
import { TSvgIcon } from '@tnzi/ui'
import { DEFAULT_ROUTE_ICONS } from '../../router/routeIcons'
import { humanise, translatePageKey } from '../../pages/_shared/translate'
import { useAdminBreadcrumbStore } from '../../stores/useAdminBreadcrumbStore'
import { breadcrumbRouteKey } from '../../headless/useBreadcrumb'

interface Props {
  /** Show the route icon beside each crumb label. Default `true`. */
  showIcon?: boolean
  /** Custom translator (e.g. vue-i18n's `$t`). */
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  showIcon: true,
  translate: undefined,
})

const route = useRoute()
const router = useRouter()

// Runtime breadcrumb contributions (record name / cross-entity drill trail).
// Guarded: TAdminAutoBreadcrumb is mounted in unit tests WITHOUT pinia, so the
// store may be unavailable - degrade to the pure route-derived walk.
let breadcrumbStore: ReturnType<typeof useAdminBreadcrumbStore> | null = null
try {
  breadcrumbStore = useAdminBreadcrumbStore()
} catch {
  breadcrumbStore = null
}
const currentKey = computed(() => breadcrumbRouteKey(route))

// i18n-key label pattern (dotted lowerCamel ASCII). Contributed labels that
// match are resolved through the dictionary; human strings (record names) pass
// through verbatim so "Smith v. Jones" is never mangled by `humanise`.
const I18N_KEY = /^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)+$/
function resolveContributedLabel(label: string): string {
  if (!I18N_KEY.test(label)) return label
  if (props.translate) {
    const tr = props.translate(label)
    if (tr && tr !== label) return tr
  }
  const bundled = translatePageKey('', label)
  if (bundled && bundled !== label) return bundled
  return label
}

function resolveLabel(r: RouteLocationNormalizedLoaded['matched'][number]): string {
  const raw =
    (r.meta?.title as string | undefined) ??
    (typeof r.name === 'string' ? r.name : r.path)
  if (props.translate) {
    const translated = props.translate(raw)
    // When translate returns the key unchanged it missed - fall back to
    // a humanised version of the key so the user never sees raw
    // dotted-i18n strings in the breadcrumb.
    if (translated && translated !== raw) return translated
  }
  // Bundled-locale fallback so breadcrumb labels localise without the
  // consumer wiring `translate`. Mirrors TAdminTabs.renderTitle.
  const bundled = translatePageKey('', raw)
  if (bundled && bundled !== raw) return bundled
  return humanise(raw)
}

type MatchedRecord = RouteLocationNormalizedLoaded['matched'][number]

function toCrumb(r: MatchedRecord): TAdminBreadcrumbItem {
  const name = typeof r.name === 'string' ? r.name : ''
  return {
    label: resolveLabel(r),
    // Only leaf routes (no children) are navigable; branch nodes have
    // no dedicated page, so `to` stays undefined and the item is shown
    // as static text.
    to: r.children?.length ? undefined : r.path,
    icon: (r.meta?.icon as string | undefined) ?? DEFAULT_ROUTE_ICONS[name],
  }
}

/**
 * Route-derived crumbs (`route.matched` + `meta.activeMenu`). This is the
 * fallback when a page contributes no runtime trail.
 */
function buildStaticCrumbs(): TAdminBreadcrumbItem[] {
  // Hidden detail/sub routes (e.g. `ai.agents.detail`) are NOT part of their
  // list page's `matched` chain (they're siblings under the module branch) and
  // carry `hideInMenu`, so a plain matched-walk would collapse the breadcrumb to
  // just the module ("AI"). When such a route declares `meta.activeMenu`, build
  // the trail from the PARENT list route's matched chain (→ "AI / Agents") and
  // append the current page's own title as a trailing, non-navigable crumb
  // (→ "AI / Agents / Agent Detail").
  const activeMenu = (route.meta as { activeMenu?: string } | undefined)?.activeMenu
  if (activeMenu) {
    try {
      const resolved = router.resolve({ name: activeMenu })
      if (resolved.matched.length) {
        // Keep the resolved parent chain VERBATIM (drop only the bare `/admin`
        // shell root). Do NOT re-apply the `hideInMenu` filter here: the
        // activeMenu target is frequently hidden from the sidebar on purpose
        // (a list surfaced only via a parent record, e.g. a client's Files) yet
        // is a legitimate breadcrumb ancestor. Filtering it collapsed the whole
        // trail down to just the leaf ("Files") - the bug this branch fixes.
        const crumbs = (resolved.matched as readonly MatchedRecord[])
          .filter((r) => r.path !== '/admin')
          .map(toCrumb)
        const self = route.matched[route.matched.length - 1]
        if (self) crumbs.push({ ...toCrumb(self), to: undefined })
        return crumbs
      }
    } catch {
      // Unknown activeMenu name - fall back to the plain matched walk.
    }
  }
  // Plain matched walk: drop the shell root and intermediate hidden branch nodes.
  return (route.matched as readonly MatchedRecord[])
    .filter((r) => {
      if (r.path === '/admin') return false
      if (r.meta?.hideInMenu) return false
      return true
    })
    .map(toCrumb)
}

const items = computed<TAdminBreadcrumbItem[]>(() => {
  // 1) A full runtime trail contributed by the page (cross-entity drill that the
  //    flat route tree cannot express - e.g. Clients / <name> / File / <number>).
  const trail = breadcrumbStore?.trailFor(currentKey.value)
  if (trail && trail.length) {
    return trail.map((c) => ({
      label: resolveContributedLabel(c.label),
      to: c.to,
      icon: c.icon,
    }))
  }
  // 2) Otherwise the route-derived walk, with an optional leaf-label override
  //    (the record's name in place of the inherited static route title).
  const crumbs = buildStaticCrumbs()
  const leaf = breadcrumbStore?.leafLabelFor(currentKey.value)
  if (leaf && crumbs.length) {
    const last = crumbs[crumbs.length - 1]
    crumbs[crumbs.length - 1] = { ...last, label: resolveContributedLabel(leaf) }
  }
  return crumbs
})

function onItemClick(item: TAdminBreadcrumbItem): void {
  if (item.to) void router.push(item.to)
}
</script>

<template>
  <TAdminBreadcrumb :items="items" :translate="translate" @item-click="onItemClick">
    <template #icon="{ item }">
      <TSvgIcon
        v-if="showIcon && item.icon"
        :icon="item.icon"
        :size="14"
        class="mr-4px"
      />
    </template>
  </TAdminBreadcrumb>
</template>
