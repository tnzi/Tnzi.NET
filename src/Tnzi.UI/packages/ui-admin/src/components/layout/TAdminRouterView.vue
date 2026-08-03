<script setup lang="ts">
/**
 * `TAdminRouterView` - drop-in `<router-view>` replacement that wires up the
 * three pieces an admin shell needs but that `<router-view>` doesn't give
 * out of the box:
 *
 *   1. `<KeepAlive>` caching - toggled per-route via `meta.keepAlive`,
 *      with a max-cache prop (`max`) so the cache doesn't grow forever.
 *      Excluded route names (e.g. login, 404) can be passed in `exclude`.
 *   2. `<Transition>` page transitions - uses the same `tnzi-` keyframe
 *      family as TAdminContent (fade-slide default, six built-in).
 *   3. `appStore.reloadFlag` reactivity - when the consumer clicks the
 *      header reload button, this view tears down and re-mounts the
 *      current route, blowing the keep-alive cache for that slot.
 *
 * Use inside TAdminContent's default slot to get the soybean-style
 * "every tab keeps its state until you close it" feel:
 *
 *     <TAdminContent>
 *       <TAdminRouterView :exclude="['login', '403', '404']" />
 *     </TAdminContent>
 */
import { computed, watchEffect } from 'vue'
import { RouterView, useRoute, type RouteLocationNormalizedLoaded } from 'vue-router'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { isMultiInstanceRoute, multiInstanceKey } from '../../stores/useAdminTabStore'

interface Props {
  /**
   * Override the transition name. Defaults to whatever
   * `useAdminThemeStore().pageTransition` reports.
   */
  transitionName?: string
  /** Maximum number of cached components. Default 20. */
  max?: number
  /**
   * Route names (or `meta.keepAlive === false`) to skip caching for.
   * Anonymous routes (no name) are always skipped.
   */
  exclude?: string[]
}

const props = withDefaults(defineProps<Props>(), {
  transitionName: undefined,
  max: 20,
  exclude: () => [],
})

const route = useRoute()
const appStore = useAdminAppStore()

// NOTE: there is deliberately no page Transition here - see the long comment
// above the template. The `transitionName` prop and the theme's page-transition
// setting therefore have no effect in this component; re-introducing the
// wrapper would bring back the permanent leave-pending navigation bug.

// The cache include list - route names whose meta.keepAlive is not strictly
// false, are not in `exclude`, and have a name we can use as the cache key.
const includedNames = computed<string[]>(() => {
  const list: string[] = []
  const matched = route.matched
  for (const r of matched) {
    const name = typeof r.name === 'string' ? r.name : null
    if (!name) continue
    if (props.exclude.includes(name)) continue
    const meta = r.meta as Record<string, unknown> | undefined
    if (meta?.keepAlive === false) continue
    list.push(name)
  }
  return list
})

// Keep-alive remount on reload is driven by `v-if="appStore.reloadFlag"` in the
// template (falsy → truthy rebuilds the tree); the superseded wrapper-key
// approach that also lived here has been removed.

/**
 * Component identity key. Single-instance routes key by name so re-visiting the
 * same page (incl. query-only changes on a list) reuses the cached instance.
 * Multi-instance routes (detail pages with dynamic params, or `meta.multiTab`
 * deep-links) key by their per-instance key (param routes → `path`, so volatile
 * `?section=` deep-links don't remount; multiTab+query → `fullPath`) so customer
 * A's detail and customer B's detail mount as SEPARATE component instances -
 * without this they share one instance (same route name) and B's page would
 * render A's stale data. This stays in lockstep with the tab store's id so a tab
 * and its component instance map 1:1.
 */
function keyOf(r: RouteLocationNormalizedLoaded): string {
  if (isMultiInstanceRoute(r)) return multiInstanceKey(r)
  return (typeof r.name === 'string' ? r.name : undefined) ?? r.fullPath
}

// Defensive: log a hint if no name attached to current route - keep-alive
// silently skips those entries.
watchEffect(() => {
  if (typeof route.name === 'string') return
  // No-op for prod; the silent skip is the documented behavior.
})
</script>

<template>
  <!-- Phase H3 L2 reverted (page-render regression): wrapping the
       lazy `defineAsyncComponent` routes with Suspense + KeepAlive
       caused the router to stay in a permanent loading state - the
       async component setup never resolved inside the nested
       Suspense + KeepAlive + v-if combination. The route progress
       bar stayed at "on" forever and the content area rendered an
       empty `.t-admin-content__page`. Reverted to the soybean-
       parity simple shell: RouterView → Transition → KeepAlive →
       component. First-visit splash can come back later as a
       targeted enhancement once we move the route definitions away
       from `defineAsyncComponent()` (Vue Router also warns about
       this - see the routes.ts cleanup follow-up). -->
  <!-- Phase: route-async + Transition + KeepAlive interplay (P0).
       The previous shape (`<Transition mode="out-in"><KeepAlive
       :include><component :is :v-if :key></KeepAlive></Transition>`)
       strands navigation when the new route is a vue-router
       async-wrapped component (`() => import(...)`): the Transition
       waits for leave to finish before enter, but the new entry is
       still resolving its async chunk while Transition's leave/enter
       state machine and KeepAlive's :include matching race each other,
       producing a permanent leave-pending state - old DOM stays
       mounted, new DOM never appears, URL is already the new path.

       Dropping `mode` (so enter/leave run together) only made it
       worse: the page DIV accumulated 3-4 stale `t-crud-page` siblings
       because leave never resolved its class transitions.

       Fix: split Transition and KeepAlive cleanly. KeepAlive lives
       inside RouterView's slot but Transition is dropped - the
       `:key="r.fullPath"` already drives the right unmount/mount on
       navigation, and KeepAlive's `:include` reactivates cached
       instances by component name. Without the Transition wrapper the
       new component mounts instantly, no enter/leave race, and the
       async chunk loader works the way vue-router intends. -->
  <RouterView v-slot="{ Component, route: r }">
    <KeepAlive :include="includedNames" :max="max">
      <component
        :is="Component"
        v-if="appStore.reloadFlag"
        :key="keyOf(r)"
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
