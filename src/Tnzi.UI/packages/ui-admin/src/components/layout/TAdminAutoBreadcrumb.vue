<script setup lang="ts">
/**
 * `TAdminAutoBreadcrumb` — route-driven wrapper around `TAdminBreadcrumb`.
 *
 * Walks `route.matched`, skips the bare `/admin` shell root and any
 * route flagged `hideInMenu`, and emits the resulting items into the
 * existing items-driven `TAdminBreadcrumb`. Icons are pulled from the
 * same `DEFAULT_ROUTE_ICONS` map the sidebar uses, so the breadcrumb
 * stays visually consistent with the active menu row.
 *
 * Mirrors `soybean-admin-example/src/layouts/modules/global-breadcrumb/index.vue`
 * in spirit — soybean drives its breadcrumb from a separate
 * `routeStore.breadcrumbs` builder, but the matched-array walk
 * produces the same result for a standard parent→child route tree.
 */
import { computed } from 'vue'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded } from 'vue-router'
import TAdminBreadcrumb, { type TAdminBreadcrumbItem } from './TAdminBreadcrumb.vue'
import { TSvgIcon } from '@tnzi/ui'
import { DEFAULT_ROUTE_ICONS } from '../../router/routeIcons'
import { humanise, translatePageKey } from '../../pages/_shared/translate'

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

function resolveLabel(r: RouteLocationNormalizedLoaded['matched'][number]): string {
  const raw =
    (r.meta?.title as string | undefined) ??
    (typeof r.name === 'string' ? r.name : r.path)
  if (props.translate) {
    const translated = props.translate(raw)
    // When translate returns the key unchanged it missed — fall back to
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

const items = computed<TAdminBreadcrumbItem[]>(() => {
  // Hidden detail/sub routes (e.g. `ai.agents.detail`) are NOT part of their
  // list page's `matched` chain (they're siblings under the module branch) and
  // carry `hideInMenu`, so a plain matched-walk would collapse the breadcrumb to
  // just the module ("AI"). When such a route declares `meta.activeMenu`, build
  // the trail from the PARENT list route's matched chain (→ "AI / Agents") and
  // append the current page's own title as a trailing, non-navigable crumb
  // (→ "AI / Agents / Agent Detail").
  const activeMenu = (route.meta as { activeMenu?: string } | undefined)?.activeMenu
  let chain = route.matched as readonly MatchedRecord[]
  let trailing: TAdminBreadcrumbItem | null = null
  if (activeMenu) {
    try {
      const resolved = router.resolve({ name: activeMenu })
      if (resolved.matched.length) {
        chain = resolved.matched as readonly MatchedRecord[]
        const self = route.matched[route.matched.length - 1]
        if (self) trailing = { ...toCrumb(self), to: undefined }
      }
    } catch {
      // Unknown activeMenu name — fall back to the plain matched walk.
    }
  }
  const crumbs = chain
    .filter((r) => {
      if (r.path === '/admin') return false
      if (r.meta?.hideInMenu) return false
      return true
    })
    .map(toCrumb)
  if (trailing) crumbs.push(trailing)
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
