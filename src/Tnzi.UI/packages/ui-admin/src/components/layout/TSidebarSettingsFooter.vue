<script setup lang="ts">
import { computed } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import type { AdminMenuItem } from '../../stores/useAdminRouteStore'
import { useSettingsEntry } from '../../headless/useSettingsEntry'

/**
 * `TSidebarSettingsFooter` - the sidebar's built-in bottom actions (Settings
 * entry + the super-admin "Built-in menus" toggle), extracted so BOTH the
 * full sidebar (`TAdminSidebar`) and the vertical-mix nav rail
 * (`TAdminMixNavRail`, via its `#footer` slot) render the same footer instead
 * of the rail silently dropping it.
 *
 * The gating and the navigation come from `useSettingsEntry`, shared with the
 * desktop layout's start menu - which has no sidebar to put these in, and where
 * a plain `router.push` would go nowhere.
 *
 * Adaptive layout (container query on the root): with enough width the actions
 * sit on a single row - Settings (first) shows icon + label left-aligned, the
 * icon-only built-in toggle hugs the right; when the container is too narrow
 * (collapsed rail / vertical-mix rail / a custom narrow sider) they stack as
 * centered icon-only buttons.
 */

interface Props {
  /**
   * Render the built-in Settings entry (gear -> the settings route).
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

const settings = useSettingsEntry({ enabled: () => props.showSettingsEntry })

const hasSettingsRoute = settings.available
const settingsLabel = settings.label
const isSettingsActive = settings.isActive
const showBuiltInToggle = settings.canToggleBuiltIn
const builtInTip = settings.builtInTip

const hasContent = computed(() => showBuiltInToggle.value || hasSettingsRoute.value)

function goSettings(): void {
  settings.open()
  // Route through menuSelect so the host's mobile-drawer collapse logic fires.
  emit('menuSelect', { key: 'settings', label: settingsLabel.value, path: settings.path.value })
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
        :class="{ 'is-active': settings.builtInEnabled.value }"
        :title="builtInTip"
        @click="settings.toggleBuiltIn()"
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
