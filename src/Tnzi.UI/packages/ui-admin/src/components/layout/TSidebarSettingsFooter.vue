<script setup lang="ts">
import { computed } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import type { AdminMenuItem } from '../../stores/useAdminRouteStore'
import { useSettingsEntry } from '../../headless/useSettingsEntry'
import { useChromeActions } from '../../headless/useChromeActions'

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
 * The host's own chrome actions (`defineAdminApp({ shell: { actions } })`)
 * render here too, added to the strip rather than replacing anything - the
 * reason the `#footer` slot on `TAdminSidebar` was not the answer: that slot
 * replaces the whole footer, so a host reaching for it silently loses its own
 * Settings gear.
 *
 * Adaptive layout (container query on the root): with enough width the actions
 * sit on a single row - the FIRST one shows icon + label left-aligned, every
 * one after it is icon-only and hugs the right; when the container is too
 * narrow (collapsed rail / vertical-mix rail / a custom narrow sider) they
 * stack as centered icon-only buttons.
 *
 * ★ The anchor is positional, not "Settings". Settings is gated on a
 * settings-view permission, and an app that grants it to owners only leaves
 * every other user a strip whose sole entry is a host action. Pinning the label
 * to Settings made that user's one affordance an unlabelled square, decodable
 * only by hovering for the native `title` - and the people who use the tool all
 * day are exactly the ones without the permission. "An icon among icons should
 * not be the odd labelled one out" is true while the built-ins are there and
 * inverts when they are not: a lone glyph is not blending into a row, it IS the
 * row.
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

const hostActions = useChromeActions()

const hasContent = computed(
  () => showBuiltInToggle.value || hasSettingsRoute.value || hostActions.value.length > 0,
)

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
         whichever action comes FIRST shows its icon + label (left), every
         following one is an icon-only button hugging the right. A container
         query stacks them as centered icon-only buttons when too narrow
         (mini-nav footer). Labels are always in the DOM; CSS shows the first
         and hides the rest, so the anchor role follows whoever survives the
         gates rather than being pinned to one particular button. -->
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
      <!-- Host actions sit between Settings and the built-in-menus toggle,
           NOT after both. Two reasons, and the second one is load-bearing:
           the cube is a super-admin developer switch and an ordinary app tool
           belongs nearer the Settings entry; and the cube is deliberately
           label-less, so if it came first it would take the anchor role and
           render as a stretched, wordless glyph. Ordering the strip so the
           label-carrying buttons come first is what lets the positional rule
           stay positional.

           They carry a label span like Settings does. Settings is gated on a
           settings-view permission, so in an app that grants it narrowly the
           common case is a strip whose ONLY entry is a host action - and a
           lone glyph is not blending into a row of glyphs, it IS the row. -->
      <button
        v-for="action in hostActions"
        :key="action.key"
        type="button"
        class="t-sidebar-settings-footer__btn t-sidebar-settings-footer__btn--host"
        :class="{ 'is-active': action.active }"
        :title="action.label"
        :aria-label="action.label"
        @click="action.run()"
      >
        <TSvgIcon :icon="action.icon" :size="18" />
        <span class="t-sidebar-settings-footer__label">{{ action.label }}</span>
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
   box lines up with the Settings entry instead of sitting shorter.
   `wrap` is inert for the built-ins alone (two actions always fit) and is what
   keeps a host that contributes several actions from having them clipped by the
   sider's `overflow: hidden` - they drop to a second line instead of vanishing. */
.t-sidebar-settings-footer__actions {
  display: flex;
  flex-wrap: wrap;
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
/* First action anchors the row: icon + label, grows, left-aligned. Whichever
   button that is - Settings when the user can reach it, otherwise the host's
   first action. */
.t-sidebar-settings-footer__actions > .t-sidebar-settings-footer__btn:first-child {
  flex: 1 1 auto;
  min-width: 0;
}
/* Truncate rather than push the trailing icons onto a second line. Inert for
   the built-in "Settings" string; it earns its keep now that arbitrary
   host-supplied text renders here. */
.t-sidebar-settings-footer__label {
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
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
