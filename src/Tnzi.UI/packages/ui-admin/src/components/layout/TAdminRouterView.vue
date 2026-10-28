<script setup lang="ts">
/**
 * `TAdminRouterView` - drop-in `<router-view>` replacement that wires up the
 * pieces an admin shell needs but that `<router-view>` doesn't give out of
 * the box:
 *
 *   1. `<KeepAlive>` caching of tab pages. A route opts in with
 *      `meta.keepAlive: true`; its page then keeps its state (filters, page,
 *      scroll, a half-filled form) while its tab is open, and is dropped when
 *      the tab closes. A page that is not a tab (`meta.hideInTab`) is kept only
 *      while it is on screen. `cache` turns caching off altogether (the admin
 *      shell binds it to the theme's "Cache tab pages" switch); `max` bounds
 *      the cache (least recently used first); `exclude` lists route names that
 *      are never cached.
 *   2. `appStore.reloadFlag` reactivity - the header reload button tears the
 *      current page down and mounts it afresh, cached or not.
 *
 * A kept page sees its own route, not whichever route is on screen: its
 * `useRoute()` stands still while another page is shown and catches up when
 * it is shown again (see `kept-page.ts`). A page that reads the route only in
 * `onMounted` (a `?party=` hand-off, a `?roleId=` deep link) must also watch
 * it, or a hand-off into its already-open tab is missed.
 *
 * Use inside TAdminContent's default slot:
 *
 *     <TAdminContent>
 *       <TAdminRouterView :exclude="['login', '403', '404']" />
 *     </TAdminContent>
 */
import { computed, reactive, watch, type Component as PageHolder } from 'vue'
import { RouterView, useRoute, type RouteLocationNormalizedLoaded } from 'vue-router'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { useAdminTabStore } from '../../stores/useAdminTabStore'
import { defineKeptPage, pageKeyOf } from './kept-page'

interface Props {
  /**
   * Accepted for compatibility; has no effect. Page transitions are
   * deliberately not rendered here - see the comment above the template.
   */
  transitionName?: string
  /**
   * Cache pages at all. Default true. The admin shell binds the theme's
   * "Cache tab pages" switch (`themeStore.tabCache`) here.
   */
  cache?: boolean
  /** Maximum number of cached pages. Default 20. */
  max?: number
  /**
   * Route names never cached, whatever their `meta.keepAlive`. Anonymous
   * routes (no name) are never cached either.
   */
  exclude?: string[]
}

const props = withDefaults(defineProps<Props>(), {
  transitionName: undefined,
  cache: true,
  max: 20,
  exclude: () => [],
})

const route = useRoute()
const appStore = useAdminAppStore()
const tabStore = useAdminTabStore()

/** The page of `r` may be cached: opted in, named and not excluded. */
function isCacheable(r: RouteLocationNormalizedLoaded): boolean {
  const name = typeof r.name === 'string' ? r.name : null
  if (!name || props.exclude.includes(name)) return false
  return r.meta?.keepAlive === true
}

const currentKey = computed(() => pageKeyOf(route))

/**
 * Keys that were on screen while cacheable - the only pages the cache can
 * hold. Remembered here because a tab carries no reliable copy of its route's
 * `meta.keepAlive`; a key leaves the set once it is neither a tab nor on screen.
 */
const keptKeys = reactive(new Set<string>())
watch(
  [currentKey, () => isCacheable(route)],
  ([key, cacheable]) => {
    if (cacheable) keptKeys.add(key)
  },
  { immediate: true, flush: 'sync' },
)

/**
 * The keys allowed to stay alive: kept keys whose tab is still open, plus the
 * page on screen when it may be cached. A key dropping out of this list is
 * what makes `<KeepAlive>` destroy its page (closing a tab, leaving a page
 * that has none, switching caching off). During a reload the page on screen
 * is left out, so the next render mounts it afresh instead of re-showing the
 * cached instance.
 */
const includedKeys = computed<string[]>(() => {
  if (!props.cache) return []
  const openTabs = new Set(tabStore.allTabs.map((tab) => tab.id))
  const keys = new Set<string>()
  for (const key of keptKeys) {
    if (openTabs.has(key)) keys.add(key)
  }
  if (isCacheable(route) && appStore.reloadFlag) keys.add(currentKey.value)
  else keys.delete(currentKey.value)
  return [...keys]
})

/**
 * One holder component per cache key, named for KeepAlive's `include`. Plain
 * (non-reactive) map: a holder must stay the same component for as long as
 * KeepAlive may hold its key, because the cache reuses an instance by key
 * without checking its type.
 */
const holders = new Map<string, PageHolder>()
let holderSeq = 0
const holderNames = new Map<string, string>()

function holderFor(key: string): PageHolder {
  let holder = holders.get(key)
  if (!holder) {
    const name = `TnziKeptPage${++holderSeq}`
    holder = defineKeptPage(name, key)
    holders.set(key, holder)
    holderNames.set(key, name)
  }
  return holder
}

const includedNames = computed<string[]>(() =>
  includedKeys.value.map((key) => {
    holderFor(key)
    return holderNames.get(key)!
  }),
)

// Forget what can no longer be on screen. Post-flush, with the final include
// list, so a key that left and came back within one tick keeps its holder in
// step with KeepAlive (which prunes from the same final list).
watch(
  includedKeys,
  (keys) => {
    const live = new Set(keys)
    live.add(currentKey.value)
    for (const key of [...keptKeys]) {
      if (!live.has(key)) keptKeys.delete(key)
    }
    for (const key of [...holders.keys()]) {
      if (live.has(key)) continue
      holders.delete(key)
      holderNames.delete(key)
    }
  },
  { flush: 'post' },
)
</script>

<template>
  <!-- No page Transition, on purpose. Wrapping the lazy route components in
       `<Transition mode="out-in"><KeepAlive>` stranded navigation: the leave
       waited while the new route was still resolving its async chunk, the
       old DOM stayed mounted and the new one never appeared although the URL
       had changed (without `mode`, stale page siblings piled up instead). A
       Suspense + KeepAlive + v-if wrapper left the router permanently
       loading. The `:key` already drives the right mount on navigation. -->
  <!-- Each page renders inside a holder named for its cache key (see
       `kept-page.ts`): KeepAlive matches `include` against component names,
       and a page component's own name says nothing about its route. -->
  <RouterView v-slot="{ Component, route: r }">
    <KeepAlive :include="includedNames" :max="max">
      <component
        :is="holderFor(pageKeyOf(r))"
        v-if="Component && appStore.reloadFlag"
        :key="pageKeyOf(r)"
        :page="Component"
      />
    </KeepAlive>
  </RouterView>
</template>

<style scoped>
.t-admin-router-view__loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-height: 200px;
  padding: 24px;
}
.t-admin-router-view__loading-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--tnzi-primary, #646cff);
  animation: t-admin-router-view-bounce 1.2s ease-in-out infinite;
}
.t-admin-router-view__loading-dot:nth-child(2) {
  animation-delay: 0.15s;
}
.t-admin-router-view__loading-dot:nth-child(3) {
  animation-delay: 0.3s;
}
@keyframes t-admin-router-view-bounce {
  0%, 80%, 100% { transform: scale(0.6); opacity: 0.4; }
  40% { transform: scale(1); opacity: 1; }
}
</style>
