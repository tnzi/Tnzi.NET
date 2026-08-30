<script setup lang="ts">
/**
 * `TDesktopTaskbar` - brand, start menu, search, one button per open window,
 * system tray.
 *
 * Left-anchored: the start button and search, then one button per open window -
 * all hugging the left edge, with the system tray pushed to the right. The
 * start PANEL rises from the same corner as its button; a panel that opens
 * somewhere other than where it was clicked reads as a different surface.
 *
 * Window buttons carry their title when they fit and drop to icon-only when
 * they do not - see `labelled`. The brand is NOT here: it belongs at the top of
 * the start menu, where it labels the system surface instead of competing with
 * the dock for the corner.
 *
 * The window buttons render in OPEN order (`taskbarWindows`), not stacking
 * order. Sorting by z would make the buttons rearrange themselves every time
 * focus changed, which is exactly when the user is reaching for one.
 *
 * All the state and every operation comes from `useAdminDesktopStore`. The tab
 * bar's data model (`useAdminTabStore`) is a sibling, not a base: a tab is one
 * route, a taskbar button is one window, and two buttons can share a route.
 */
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { NDropdown, NTooltip } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import {
  useAdminDesktopStore,
  type AdminDesktopWindow,
} from '../../stores/useAdminDesktopStore'
import { useAdminShellActions } from '../../headless/admin-shell-actions'
import { useAdminThemeStore } from '../../stores/useAdminThemeStore'
import { resolveWindowIcon, resolveWindowTitle } from './desktop-labels'
import { getDesktopPanelTint } from '../../headless/desktop-panels'
import { desktopTileStyle, useDesktopTint } from './desktop-tints'
import TDesktopStartMenu from './TDesktopStartMenu.vue'
import TDesktopTray from './TDesktopTray.vue'
import type { AdminMenuItem } from '../../stores/useAdminRouteStore'

const props = defineProps<{
  /** Product name, shown at the left edge. */
  brand?: string
  /** Iconify name for the brand mark. */
  brandIcon?: string
  /** Forwarded to the tray - the shell's own theme-button gate. */
  showThemeBtn?: boolean
  translate?: (key: string, fallback?: string) => string
}>()

const emit = defineEmits<{ open: [item: AdminMenuItem] }>()

const desktop = useAdminDesktopStore()
const themeStore = useAdminThemeStore()
const shell = useAdminShellActions()
const tintFor = useDesktopTint()

const t = (key: string, fallback: string): string => props.translate?.(key, fallback) ?? fallback

const startOpen = ref(false)

// ── Icon + title, or icon only ──────────────────────────────────────────────
/** Below this a button cannot show its icon plus a useful stub of the title. */
const MIN_LABELLED_WIDTH = 116

const windowsEl = ref<HTMLElement | null>(null)
const availableWidth = ref(0)
let observer: ResizeObserver | undefined

function measure(): void {
  if (windowsEl.value) availableWidth.value = windowsEl.value.clientWidth
}

onMounted(() => {
  measure()
  if (typeof ResizeObserver === 'undefined') {
    window.addEventListener('resize', measure)
    return
  }
  observer = new ResizeObserver(measure)
  if (windowsEl.value) observer.observe(windowsEl.value)
})

onBeforeUnmount(() => {
  observer?.disconnect()
  window.removeEventListener('resize', measure)
})

/**
 * Whether the buttons show their title.
 *
 * Compared against the strip's own width, which is the flex leftover and so does
 * NOT depend on what the buttons currently render - that independence is what
 * keeps this from oscillating (drop the labels, they fit, put them back, they
 * do not). Labels are the default before the first measurement: a title is the
 * only thing that tells two windows of the same module apart.
 */
const labelled = computed(() => {
  const count = desktop.taskbarWindows.length
  if (count === 0 || availableWidth.value <= 0) return true
  return count * MIN_LABELLED_WIDTH <= availableWidth.value
})

// ── Window buttons ──────────────────────────────────────────────────────────
function titleOf(win: AdminDesktopWindow): string {
  return resolveWindowTitle(win.title, props.translate)
}

function routeNameOf(win: AdminDesktopWindow): string | undefined {
  return win.stack[win.stackIndex]?.name
}

function iconOf(win: AdminDesktopWindow): string {
  return resolveWindowIcon(win.icon, routeNameOf(win))
}

function tileStyleOf(win: AdminDesktopWindow): Record<string, string> {
  const panel = win.stack[win.stackIndex]?.panel
  return desktopTileStyle(
    tintFor(routeNameOf(win), panel ? (getDesktopPanelTint(panel) ?? undefined) : undefined),
    26,
    6,
  )
}

/**
 * Width of the running-indicator under a button: wide when this window has
 * focus, narrow when it is merely open.
 *
 * A binary present/absent dot would answer "is it open" but not "which one am I
 * looking at", and with eight identical-length buttons that second question is
 * the one being asked.
 */
function indicatorWidth(win: AdminDesktopWindow): string {
  if (desktop.activeId === win.id && !win.minimized) return '16px'
  return '8px'
}

// ── Context menu ────────────────────────────────────────────────────────────
const menuTarget = ref<string | null>(null)
const menuPosition = ref({ x: 0, y: 0 })
const menuShow = ref(false)

const menuOptions = computed(() => {
  const win = menuTarget.value ? desktop.find(menuTarget.value) : undefined
  return [
    {
      key: win?.maximized ? 'restore' : 'maximize',
      label: win?.maximized
        ? t('admin.desktop.window.restore', 'Restore')
        : t('admin.desktop.window.maximize', 'Maximize'),
    },
    { key: 'minimize', label: t('admin.desktop.window.minimize', 'Minimize') },
    { key: 'close', label: t('admin.desktop.window.close', 'Close') },
    { key: 'closeOthers', label: t('admin.desktop.taskbar.closeOthers', 'Close others') },
  ]
})

function onContextMenu(e: MouseEvent, win: AdminDesktopWindow): void {
  e.preventDefault()
  menuTarget.value = win.id
  menuPosition.value = { x: e.clientX, y: e.clientY }
  menuShow.value = true
}

function onContextSelect(key: string): void {
  const id = menuTarget.value
  menuShow.value = false
  if (!id) return
  switch (key) {
    case 'maximize':
    case 'restore':
      desktop.toggleMaximize(id)
      break
    case 'minimize':
      desktop.minimize(id)
      break
    case 'close':
      desktop.close(id)
      break
    case 'closeOthers':
      for (const w of [...desktop.windows]) if (w.id !== id) desktop.close(w.id)
      break
  }
}

function onStartOpen(item: AdminMenuItem): void {
  startOpen.value = false
  emit('open', item)
}
</script>

<template>
  <footer class="t-desktop-taskbar">
    <div class="t-desktop-taskbar__dock">
      <TDesktopStartMenu
        v-model:show="startOpen"
        :brand="brand"
        :brand-icon="brandIcon"
        :translate="translate"
        @open="onStartOpen"
      />

      <!-- Same gate the header's search button uses: an application that turned
           global search off must not get it back through the dock. -->
      <NTooltip v-if="themeStore.globalSearchVisible" placement="top" trigger="hover">
        <template #trigger>
          <button
            type="button"
            class="t-desktop-taskbar__search"
            :aria-label="t('admin.desktop.taskbar.search', 'Search')"
            @click="shell.openSearch()"
          >
            <TSvgIcon icon="mdi:magnify" :size="18" aria-hidden="true" />
          </button>
        </template>
        {{ t('admin.desktop.taskbar.search', 'Search') }} (Ctrl+K)
      </NTooltip>

      <span v-if="desktop.taskbarWindows.length > 0" class="t-desktop-taskbar__divider" />

      <div ref="windowsEl" class="t-desktop-taskbar__windows">
        <NTooltip
          v-for="win in desktop.taskbarWindows"
          :key="win.id"
          placement="top"
          trigger="hover"
        >
          <template #trigger>
            <button
              type="button"
              class="t-desktop-taskbar__win"
              :class="{
                't-desktop-taskbar__win--active': desktop.activeId === win.id && !win.minimized,
                't-desktop-taskbar__win--labelled': labelled,
              }"
              :aria-label="titleOf(win)"
              @click="desktop.toggleMinimize(win.id)"
              @contextmenu="onContextMenu($event, win)"
            >
              <span class="t-desktop-taskbar__win-tile" :style="tileStyleOf(win)">
                <TSvgIcon :icon="iconOf(win)" :size="14" aria-hidden="true" />
              </span>
              <span v-if="labelled" class="t-desktop-taskbar__win-label">{{ titleOf(win) }}</span>
              <span
                class="t-desktop-taskbar__indicator"
                :style="{ width: indicatorWidth(win) }"
              />
            </button>
          </template>
          {{ titleOf(win) }}
        </NTooltip>
      </div>
    </div>

    <TDesktopTray :show-theme-btn="showThemeBtn" :translate="translate">
      <slot name="tray" />
    </TDesktopTray>

    <NDropdown
      trigger="manual"
      placement="top-start"
      :show="menuShow"
      :x="menuPosition.x"
      :y="menuPosition.y"
      :options="menuOptions"
      @select="onContextSelect"
      @clickoutside="menuShow = false"
    />
  </footer>
</template>

<style scoped>
.t-desktop-taskbar {
  position: relative;
  z-index: var(--tnzi-desktop-z-taskbar);
  display: flex;
  flex: 0 0 auto;
  gap: 8px;
  align-items: center;
  height: var(--tnzi-desktop-taskbar-height);
  padding: 0 6px;
  color: var(--tnzi-desktop-taskbar-fg);
  /* Shared glass recipe - see `--tnzi-desktop-solidity` in variables.css. The
     blur used to be hardcoded here and nowhere else, so the dock was the only
     frosted surface on the desktop while the title bar's own 96% alpha sat
     inert over an opaque window. */
  background: color-mix(
    in srgb,
    var(--tnzi-desktop-taskbar-bg) var(--tnzi-desktop-solidity),
    transparent
  );
  border-top: 1px solid var(--tnzi-desktop-taskbar-border);
  backdrop-filter: var(--tnzi-desktop-backdrop);
}

/* Takes the slack, so the tray sits at the right edge. */
.t-desktop-taskbar__dock {
  display: flex;
  flex: 1 1 auto;
  gap: 2px;
  align-items: center;
  min-width: 0;
}

.t-desktop-taskbar__divider {
  flex: 0 0 auto;
  width: 1px;
  height: 18px;
  margin: 0 5px;
  background: var(--tnzi-desktop-taskbar-border);
}

/**
 * `flex: 1 1 auto` is load-bearing: it makes this strip the flex LEFTOVER, so
 * its width is the space available to the buttons rather than the space the
 * buttons currently take. `labelled` measures it, and measuring a
 * content-sized box would be circular - drop the labels, the box shrinks, the
 * labels "fit" again. (Measured while it was the default `0 1 auto`: three
 * windows on a 1200px dock reported 106px available and went icon-only.)
 */
.t-desktop-taskbar__windows {
  display: flex;
  flex: 1 1 auto;
  gap: 2px;
  align-items: center;
  min-width: 0;
  overflow-x: auto;
  /* The bar has no room for a scrollbar - it would eat the indicator strip. */
  scrollbar-width: none;
}

.t-desktop-taskbar__windows::-webkit-scrollbar {
  display: none;
}

.t-desktop-taskbar__search {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  padding: 0;
  color: inherit;
  cursor: default;
  background: transparent;
  border: none;
  border-radius: 5px;
  transition: background-color 0.12s ease;
}

/**
 * Icon-only by default; `--labelled` widens it and lets the title in. The
 * running indicator is absolutely positioned so the two modes share one box
 * model - as a flow child it would push the row 3px taller in one of them.
 */
.t-desktop-taskbar__win {
  position: relative;
  display: flex;
  flex: 0 1 auto;
  gap: 7px;
  align-items: center;
  justify-content: center;
  width: 34px;
  min-width: 34px;
  height: 34px;
  padding: 0;
  color: inherit;
  cursor: default;
  background: transparent;
  border: none;
  border-radius: 5px;
  transition: background-color 0.12s ease;
}

.t-desktop-taskbar__win--labelled {
  justify-content: flex-start;
  width: auto;
  max-width: 180px;
  padding: 0 10px 0 8px;
}

.t-desktop-taskbar__win-label {
  overflow: hidden;
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-desktop-taskbar__search:hover,
.t-desktop-taskbar__search:focus-visible,
.t-desktop-taskbar__win:hover,
.t-desktop-taskbar__win:focus-visible {
  background: var(--tnzi-desktop-taskbar-hover);
  outline: none;
}

.t-desktop-taskbar__win--active {
  background: var(--tnzi-desktop-taskbar-hover);
}

.t-desktop-taskbar__win-tile {
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
}

.t-desktop-taskbar__indicator {
  position: absolute;
  bottom: 2px;
  left: 50%;
  height: 2.5px;
  background: var(--tnzi-primary, #646cff);
  border-radius: 2px;
  transform: translateX(-50%);
  transition: width 0.14s ease;
}
</style>
