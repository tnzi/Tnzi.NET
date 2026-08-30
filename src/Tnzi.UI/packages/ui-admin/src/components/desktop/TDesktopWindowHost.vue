<script setup lang="ts">
/**
 * `TDesktopWindowHost` - mounts one page inside one desktop window.
 *
 * This is the piece that replaces `<RouterView>` for the desktop layout. It
 * does three things `<RouterView>` cannot:
 *
 *  1. **Mounts N pages at once.** A router has one `currentRoute`, so
 *     `<RouterView>` renders one component. Here each window resolves its own
 *     component and mounts it independently.
 *  2. **Scopes the route.** `useWindowRoute` provides this window's own
 *     `routerKey` / `routeLocationKey`, so the page inside sees its own URL
 *     state instead of the single global one. Without it every open window
 *     would read and write the same `?detail=` key.
 *  3. **Re-checks authorisation on mount.** A window is restored from
 *     localStorage, which is user-writable and outlives a permission change.
 *     The route guard never runs for it - so the check has to happen here or
 *     a stale persisted window becomes a way around it.
 *
 * Components are resolved from the REAL router's records, never from
 * `useAdminRouteStore`: `toAdminRouteRecords()` deliberately drops the
 * `component` field ("which the store doesn't care about"), so every menu entry
 * reports `component: undefined`. Going through the router records also keeps
 * this file free of any `pages/` import, which the layering gate requires.
 */
import { computed, defineAsyncComponent, type Component } from 'vue'
import { useRouter } from 'vue-router'
import { NResult, NSpin } from 'naive-ui'
import { TOverlayTheme } from '@tnzi/ui'
import { getDesktopPanel } from '../../headless/desktop-panels'
import { useWindowRoute } from '../../headless/useWindowRoute'
import { useAdminRouteStore } from '../../stores/useAdminRouteStore'
import { useAdminDesktopStore } from '../../stores/useAdminDesktopStore'

const props = defineProps<{
  /** Which window this host is rendering. */
  windowId: string
  /** Translator for the failure states. */
  translate?: (key: string, fallback?: string) => string
}>()

// MUST run before anything reads a route in this subtree - it is what installs
// the window-local router/route for every descendant. The returned route is
// also what a function-form `props` option receives, exactly as `RouterView`
// hands it the real one.
const windowRoute = useWindowRoute(props.windowId)

const realRouter = useRouter()
const routeStore = useAdminRouteStore()
const desktop = useAdminDesktopStore()

const t = (key: string, fallback: string): string => props.translate?.(key, fallback) ?? fallback

const location = computed(() => desktop.locationOf(props.windowId))

/**
 * `defineAsyncComponent` mints a new component definition on every call, and a
 * new definition is a different component *type* to Vue - so calling it inline
 * would remount the page on every unrelated re-render, and two windows on the
 * same route would each pay for their own definition. Cache by route name.
 *
 * Module scope, not a store: this is a pure function of the route table, has no
 * user state in it, and must not be persisted.
 */
const asyncComponentCache = new Map<string, Component>()

function resolveComponent(name: string): Component | null {
  const cached = asyncComponentCache.get(name)
  if (cached) return cached

  const record = realRouter.getRoutes().find((r) => r.name === name)
  const raw = record?.components?.default
  if (!raw) return null

  // Route tables use `() => import(...)`; a consumer may register a plain
  // component object instead. Vue treats a bare function as a functional
  // component, so a loader has to be wrapped rather than passed through.
  const component: Component =
    typeof raw === 'function'
      ? defineAsyncComponent(raw as () => Promise<Component>)
      : (raw as Component)

  asyncComponentCache.set(name, component)
  return component
}

/**
 * A shell surface rather than a page (chat). Panels skip the route machinery
 * entirely: they have no record, no params and no permission of their own -
 * whoever owns the surface already decided the user may open it.
 */
const panelComponent = computed<Component | null>(() => {
  const key = location.value?.panel
  return key ? getDesktopPanel(key) : null
})

/** Why this window cannot render, or null when it can. */
const blocker = computed<'missing' | 'denied' | 'unavailable' | null>(() => {
  const loc = location.value
  if (!loc) return 'missing'
  // A panel that nothing registered is missing in exactly the sense the copy
  // below describes; a registered one is always renderable.
  if (loc.panel) return panelComponent.value ? null : 'missing'
  if (routeStore.deniedRouteNames.has(loc.name)) return 'denied'
  if (routeStore.unavailableRouteNames.has(loc.name)) return 'unavailable'
  if (!realRouter.getRoutes().some((r) => r.name === loc.name)) return 'missing'
  return null
})

const pageComponent = computed<Component | null>(() => {
  if (blocker.value) return null
  if (panelComponent.value) return panelComponent.value
  const loc = location.value
  return loc ? resolveComponent(loc.name) : null
})

/**
 * Props to hand the page, honouring the route record's `props` option.
 *
 * `<RouterView>` does this and it is not optional: a page declared with
 * `props: true` reads its route params as PROPS, not off `useRoute()`. Mounting
 * such a component bare gives it `undefined` for every param - measured as
 * `GET /api/admin/users/undefined` returning 400 while the window sat on
 * "Loading..." forever.
 *
 * All three forms vue-router accepts are covered: `true` (params as props), a
 * function (receives the route), and a static object.
 */
const pageProps = computed<Record<string, unknown>>(() => {
  const loc = location.value
  if (!loc) return {}
  // Panels are not routes: they take no params, and `embedded` tells the
  // surface the window frame is providing the chrome (title bar, close,
  // maximise, dragging) so it must not draw a second set of its own.
  if (loc.panel) return { embedded: true }
  const record = realRouter.getRoutes().find((r) => r.name === loc.name)
  const cfg = record?.props?.default
  if (cfg === true) return { ...loc.params }
  if (typeof cfg === 'function') {
    return (cfg as (r: unknown) => Record<string, unknown>)(windowRoute.route) ?? {}
  }
  if (cfg && typeof cfg === 'object') return cfg as Record<string, unknown>
  return {}
})

const blockerCopy = computed(() => {
  switch (blocker.value) {
    case 'denied':
      return {
        status: '403' as const,
        title: t('admin.desktop.window.deniedTitle', 'No access'),
        description: t(
          'admin.desktop.window.deniedDescription',
          'You no longer have permission to open this window.',
        ),
      }
    case 'unavailable':
      return {
        status: '404' as const,
        title: t('admin.desktop.window.unavailableTitle', 'Module not loaded'),
        description: t(
          'admin.desktop.window.unavailableDescription',
          'The module behind this window is not available in this application.',
        ),
      }
    default:
      return {
        status: '404' as const,
        title: t('admin.desktop.window.missingTitle', 'Page not found'),
        description: t(
          'admin.desktop.window.missingDescription',
          'This window points at a page that no longer exists.',
        ),
      }
  }
})
</script>

<template>
  <!-- The page inside a window is content-area material rendered on a floating
       surface. Naive's config provider crosses that boundary through
       provide/inject, so without this reset the window inherits whatever theme
       the content area computed for itself - the "light app, dark cards inside
       the window" failure `useOverlayTheme` exists to prevent. -->
  <TOverlayTheme>
    <div class="t-desktop-window-host">
      <component :is="pageComponent" v-if="pageComponent" v-bind="pageProps" />
      <NResult
        v-else-if="blocker"
        class="t-desktop-window-host__blocked"
        :status="blockerCopy.status"
        :title="blockerCopy.title"
        :description="blockerCopy.description"
      />
      <div v-else class="t-desktop-window-host__loading">
        <NSpin size="small" />
      </div>
    </div>
  </TOverlayTheme>
</template>

<style scoped>
/* The page inside expects the same height contract the content area gives it:
   a parent that is already the right size and a flex column to grow into.

   A hairline gutter, not the content area's 12px: its job here is only to keep
   the page's own card border off the window frame so the two do not read as one
   heavy double edge. A window is already a tight space with its own chrome, and
   every pixel spent on an outer margin is taken from the table inside it.
   `border-box` so the padding comes out of the height rather than adding to it
   and breaking the flex chain. */
.t-desktop-window-host {
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  padding: 2px;
  overflow: hidden;
}

.t-desktop-window-host__blocked,
.t-desktop-window-host__loading {
  display: flex;
  flex: 1 1 auto;
  align-items: center;
  justify-content: center;
  padding: 24px;
}
</style>
