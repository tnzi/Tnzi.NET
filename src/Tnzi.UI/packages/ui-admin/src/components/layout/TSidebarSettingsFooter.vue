<script setup lang="ts">
import { computed, getCurrentInstance } from 'vue'
import { useRoute, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminRouteStore, type AdminMenuItem } from '../../stores/useAdminRouteStore'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { translatePageKey } from '../../i18n/translate'

/**
 * `TSidebarSettingsFooter` - the sidebar's built-in bottom actions (Settings
 * entry + the super-admin "Built-in menus" toggle), extracted so BOTH the
 * full sidebar (`TAdminSidebar`) and the vertical-mix nav rail
 * (`TAdminMixNavRail`, via its `#footer` slot) render the same footer instead
 * of the rail silently dropping it.
 *
 * Adaptive layout (container query on the root): with enough width the actions
 * sit on a single row - Settings (first) shows icon + label left-aligned, the
 * icon-only built-in toggle hugs the right; when the container is too narrow
 * (collapsed rail / vertical-mix rail / a custom narrow sider) they stack as
 * centered icon-only buttons.
 */

function resolveLabel(label: string): string {
  if (!label) return ''
  if (label.startsWith('admin.') || label.startsWith('tnzi.')) {
    return translatePageKey('', label)
  }
  return label
}

interface Props {
  /**
   * Render the built-in Settings entry (gear → `router.push({ name: 'settings' })`).
   * Mirrors `TAdminSidebar`'s prop of the same name.
   */
  showSettingsEntry?: boolean
  /** Paint the scroll-aware upward elevation shadow (driven by the host's scroll state). */
  elevated?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  showSettingsEntry: true,
  elevated: false,
})

const emit = defineEmits<{
  menuSelect: [menu: AdminMenuItem]
}>()

const routeStore = useAdminRouteStore()
const appStore = useAdminAppStore()
const authStore = useAdminAuthStore()

// `useRoute`/`$router` throw / are absent when no router is installed (unit
// tests mounting in isolation). Detect via getCurrentInstance and fall back.
function safeUseRoute(): RouteLocationNormalizedLoaded | null {
  const instance = getCurrentInstance()
  const hasRouter = !!instance?.appContext.config.globalProperties.$router
  if (!hasRouter) return null
  try {
    return useRoute()
  } catch {
    return null
  }
}
const route = safeUseRoute()

function safeUseRouter(): Router | null {
  const instance = getCurrentInstance()
  const router = instance?.appContext.config.globalProperties.$router as Router | undefined
  return router ?? null
}
const router = safeUseRouter()

const { can, canAny, canAnySettings } = usePermissionGuard()

// Built-in-menus toggle (super admin only): show/hide the framework's preset
// admin menus. Gated on isSuperUser - mirrors `useAdminRouteStore.hideBuiltIn`.
const showBuiltInToggle = computed(() => authStore.isSuperUser)
const builtInTip = computed(() => resolveLabel('admin.common.builtInMenusTip'))

// The built-in gear obeys the settings ROUTE's reachability with the same
// fail-open semantics as the menu filter (see the long-form note that used to
// live in TAdminSidebar): bundled route uses `meta.anySettingsPermission`; older
// custom routes with a plain `meta.permission` / `meta.permissions` still work;
// it also obeys module availability via `unavailableRouteNames`.
const hasSettingsRoute = computed(() => {
  if (!props.showSettingsEntry) return false
  if (!router?.hasRoute('settings')) return false
  if (routeStore.unavailableRouteNames.has('settings')) return false
  if (typeof router.resolve !== 'function') return true
  const meta = (router.resolve({ name: 'settings' }).meta ?? {}) as {
    permission?: unknown
    permissions?: unknown
    anySettingsPermission?: unknown
  }
  if (meta.anySettingsPermission === true) return canAnySettings()
  if (typeof meta.permission === 'string' && meta.permission) return can(meta.permission)
  if (Array.isArray(meta.permissions)) {
    const plural = meta.permissions.filter((p): p is string => typeof p === 'string' && p !== '')
    if (plural.length > 0) return canAny(plural)
  }
  return true
})
const isSettingsActive = computed(() => route?.name === 'settings')
const settingsLabel = computed(() => resolveLabel('admin.common.settings'))

const hasContent = computed(() => showBuiltInToggle.value || hasSettingsRoute.value)

function goSettings(): void {
  if (!router) return
  void router.push({ name: 'settings' })
  // Route through menuSelect so the host's mobile-drawer collapse logic fires.
  const path = typeof router.resolve === 'function' ? router.resolve({ name: 'settings' }).path : '/settings'
  emit('menuSelect', { key: 'settings', label: settingsLabel.value, path })
}
</script>

<template>
  <div
    v-if="hasContent"
    class="t-sidebar-settings-footer"
    :class="{ 't-sidebar-settings-footer--elevated': elevated }"
  >
    <!-- Adaptive footer: with enough width the actions sit on a single row -
         Settings (first) shows its icon + label (left), every following one is
         an icon-only button hugging the right. A container query stacks them as
         centered icon-only buttons when too narrow (mini-nav footer). Labels
         are always in the DOM; CSS shows the first and hides the rest. -->
    <div class="t-sidebar-settings-footer__actions">
      <button
        v-if="hasSettingsRoute"
        type="button"
        class="t-sidebar-settings-footer__btn"
        :class="{ 'is-active': isSettingsActive }"
        :title="settingsLabel"
        @click="goSettings"
      >
        <TSvgIcon icon="mdi:cog-outline" :size="18" />
        <span class="t-sidebar-settings-footer__label">{{ settingsLabel }}</span>
      </button>
      <!-- Built-in-menus toggle: an icon-only switch (no label / no NSwitch).
           The cube's `is-active` tint carries the on state; the whole button
           is the toggle. -->
      <button
        v-if="showBuiltInToggle"
        type="button"
        class="t-sidebar-settings-footer__btn t-sidebar-settings-footer__ops"
        :class="{ 'is-active': appStore.showBuiltInMenus }"
        :title="builtInTip"
        @click="appStore.toggleBuiltInMenus()"
      >
        <TSvgIcon icon="mdi:cube-outline" :size="18" />
      </button>
    </div>
  </div>
</template>

<style scoped>
.t-sidebar-settings-footer {
  flex-shrink: 0;
  padding: 8px;
  position: relative;
  z-index: 2;
  transition: box-shadow 0.2s ease;
  /* Query container so the action row flips between a single horizontal row
     (enough width) and a stacked icon-only rail (too narrow). */
  container-type: inline-size;
}
.t-sidebar-settings-footer--elevated {
  box-shadow: 0 -6px 8px -6px var(--tnzi-admin-sider-edge-shadow, rgba(0, 0, 0, 0.06));
}

/* Enough space → single horizontal row. `stretch` keeps every action the same
   height (the labeled first one sets it) so an icon-only button's active tint
   box lines up with the Settings entry instead of sitting shorter. */
.t-sidebar-settings-footer__actions {
  display: flex;
  align-items: stretch;
  gap: 6px;
}
.t-sidebar-settings-footer__btn {
  flex: 0 0 auto;
  width: auto;
  display: flex;
  align-items: center;
  justify-content: flex-start;
  gap: 8px;
  padding: 8px 10px;
  border: none;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: transparent;
  color: var(--tnzi-base-text);
  font-size: 14px;
  cursor: pointer;
  transition: color 0.15s ease, background-color 0.15s ease;
}
/* First action anchors the row: icon + label, grows, left-aligned. */
.t-sidebar-settings-footer__actions > .t-sidebar-settings-footer__btn:first-child {
  flex: 1 1 auto;
  min-width: 0;
}
/* Every following action is icon-only, centered, pushed to the right edge. */
.t-sidebar-settings-footer__actions > .t-sidebar-settings-footer__btn:not(:first-child) {
  justify-content: center;
  padding: 8px;
}
.t-sidebar-settings-footer__actions > .t-sidebar-settings-footer__btn:not(:first-child) .t-sidebar-settings-footer__label {
  display: none;
}
.t-sidebar-settings-footer__btn:hover {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.06);
  color: var(--tnzi-primary);
}
/* Built-in toggle ON / settings route active: a primary tint. For the icon-only
   built-in toggle this tint is its sole on-state indicator (both layouts). */
.t-sidebar-settings-footer__btn.is-active {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.1);
  color: var(--tnzi-primary);
  font-weight: 500;
}

/* Not enough space → stack vertically as centered icon-only buttons (the
   mini-nav footer). Covers the collapsed rail, the vertical-mix rail, and any
   custom sider too narrow to fit the row. */
@container (max-width: 150px) {
  .t-sidebar-settings-footer__actions {
    flex-direction: column;
    align-items: stretch;
    gap: 4px;
  }
  .t-sidebar-settings-footer__actions > .t-sidebar-settings-footer__btn,
  .t-sidebar-settings-footer__actions > .t-sidebar-settings-footer__btn:first-child {
    flex: 0 0 auto;
    justify-content: center;
    padding: 8px 0;
  }
  .t-sidebar-settings-footer__actions .t-sidebar-settings-footer__label {
    display: none;
  }
}
</style>
