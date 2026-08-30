<script setup lang="ts">
/**
 * `TDesktopWindow` - the chrome around one page: title bar, window controls,
 * drag, eight-way resize.
 *
 * Two behaviours worth stating because getting them wrong is silent:
 *
 *  - **Minimising hides, it does not unmount.** The host inside stays mounted
 *    the whole time the window is open, so a half-filled form survives a trip
 *    to the taskbar. `v-if` here would make minimise a data-losing action.
 *  - **Transitions are suppressed mid-gesture.** Animating `left`/`top` while
 *    the pointer is moving makes the window lag behind the cursor instead of
 *    tracking it; the same trick `TChatWindow` uses.
 */
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { TSvgIcon } from '@tnzi/ui'
import {
  useAdminDesktopStore,
  type AdminDesktopWindow,
} from '../../stores/useAdminDesktopStore'
import { useAdminRouteStore, type AdminMenuItem } from '../../stores/useAdminRouteStore'
import {
  useWindowGeometry,
  RESIZE_DIRECTIONS,
  RESIZE_CURSORS,
  type ResizeDirection,
} from '../../headless/useWindowGeometry'
import { menuItemToWindowInput, resolveWindowIcon, resolveWindowTitle } from './desktop-labels'
import { getDesktopPanelTint } from '../../headless/desktop-panels'
import { desktopTileStyle, useDesktopTint } from './desktop-tints'
import TDesktopWindowHost from './TDesktopWindowHost.vue'
import TDesktopWindowNav from './TDesktopWindowNav.vue'

const props = defineProps<{
  win: AdminDesktopWindow
  /** Size of the desktop surface, for bounding drags and maximising. */
  surface: { width: number; height: number }
  /** Whether this window currently has focus. */
  active: boolean
  translate?: (key: string, fallback?: string) => string
}>()

const desktop = useAdminDesktopStore()
const routeStore = useAdminRouteStore()
const tintFor = useDesktopTint()
// The REAL router: `useWindowRoute` installs the window-local one inside
// `TDesktopWindowHost`, one level below here, so path resolution for the nav is
// unaffected by which page the window is showing.
const realRouter = useRouter()

const t = (key: string, fallback: string): string => props.translate?.(key, fallback) ?? fallback

const location = computed(() => props.win.stack[props.win.stackIndex])
const routeName = computed(() => location.value?.name)
const title = computed(() => resolveWindowTitle(props.win.title, props.translate))
const icon = computed(() => resolveWindowIcon(props.win.icon, routeName.value))
/** The same module colour the desktop icon and the taskbar button carry, so a
 *  window is recognisable as "the green one" across all three surfaces. */
const chipStyle = computed(() => {
  const panel = location.value?.panel
  return desktopTileStyle(
    tintFor(routeName.value, panel ? (getDesktopPanelTint(panel) ?? undefined) : undefined),
    18,
    5,
  )
})

// ── Module navigation ───────────────────────────────────────────────────────
/**
 * The module this window belongs to.
 *
 * The menu tree answers it for any page that appears in the menu; the dotted
 * name is the fallback for the ones that do not - a detail page reached by id
 * is `hideInMenu`, and drilling into a user must not make the Identity nav
 * vanish and then reappear on the way back.
 */
const moduleNode = computed<AdminMenuItem | null>(() => {
  const name = routeName.value
  if (!name) return null
  const key = routeStore.moduleKeyOfRoute(name)
  return routeStore.menus.find((m) => m.key === key) ?? null
})

const navItems = computed<AdminMenuItem[]>(() => moduleNode.value?.children ?? [])

/**
 * Which nav row is lit.
 *
 * A detail page points back at its list through `meta.activeMenu`, the same
 * field the shell sidebar honours - without it the nav would go blank the
 * moment you opened a record.
 */
const navActiveKey = computed(() => {
  const name = routeName.value
  if (!name) return ''
  const record = realRouter.getRoutes().find((r) => r.name === name)
  const active = record?.meta?.activeMenu
  return typeof active === 'string' && active ? active : name
})

/**
 * `null` = nobody has said, follow the width. Once the user touches the toggle
 * their choice sticks for the life of the window.
 *
 * Making the width an override instead would leave a button that visibly does
 * nothing in a narrow window - the auto behaviour is a good DEFAULT, not a rule
 * worth refusing an explicit instruction over.
 */
const navCollapsedByUser = ref<boolean | null>(null)

/**
 * Collapse by default below a width where the nav would be eating the page.
 *
 * Not a media query: what matters is THIS window's width, not the viewport's -
 * two windows side by side on a wide screen are both narrow.
 */
const NAV_AUTO_COLLAPSE_WIDTH = 620
const navCollapsed = computed(
  () => navCollapsedByUser.value ?? props.win.width < NAV_AUTO_COLLAPSE_WIDTH,
)

/** A module with a single page has nothing to navigate between. */
const showNav = computed(() => navItems.value.length > 0)

function onNavSelect(item: AdminMenuItem): void {
  // Same window, not a new one - and through the store's stack so the window's
  // own back button can return to where it was.
  desktop.navigate(props.win.id, menuItemToWindowInput(item, realRouter))
}

const geometry = useWindowGeometry({
  current: () => ({ x: props.win.x, y: props.win.y, width: props.win.width, height: props.win.height }),
  surface: () => props.surface,
  onChange: (next) => desktop.setGeometry(props.win.id, next),
  onStart: () => desktop.focus(props.win.id),
  // Only a finished USER gesture teaches the module a size. The programmatic
  // paths (maximise, the off-screen rescue, hydrate clamping) deliberately do
  // not, or a rescue position would come back as a preference.
  onEnd: () => desktop.rememberGeometry(props.win.id),
})

const frameStyle = computed(() => ({
  left: `${props.win.x}px`,
  top: `${props.win.y}px`,
  width: `${props.win.width}px`,
  height: `${props.win.height}px`,
  zIndex: String(props.win.z),
}))

/** A maximised window fills the surface and its grips are inert - there is
 *  nothing to resize into, and a stray drag would otherwise unmaximise it by
 *  side effect. */
const resizable = computed(() => !props.win.maximized)

function onTitleBarMouseDown(e: MouseEvent): void {
  // Only the primary button drags; middle/right belong to other affordances.
  if (e.button !== 0 || props.win.maximized) {
    desktop.focus(props.win.id)
    return
  }
  geometry.startDrag(e)
}

function onResize(e: MouseEvent, direction: ResizeDirection): void {
  if (!resizable.value) return
  geometry.startResize(e, direction)
}

function onToggleMaximize(): void {
  desktop.toggleMaximize(props.win.id, props.surface)
}
</script>

<template>
  <section
    v-show="!win.minimized"
    class="t-desktop-window"
    :class="{
      't-desktop-window--active': active,
      't-desktop-window--gesturing': geometry.active.value,
      't-desktop-window--maximized': win.maximized,
    }"
    :style="frameStyle"
    :aria-label="title"
    @mousedown="desktop.focus(win.id)"
  >
    <header
      class="t-desktop-window__bar"
      @mousedown="onTitleBarMouseDown"
      @dblclick="onToggleMaximize"
    >
      <span class="t-desktop-window__chip" :style="chipStyle" aria-hidden="true">
        <TSvgIcon :icon="icon" :size="11" />
      </span>
      <span class="t-desktop-window__title" :title="title">{{ title }}</span>
      <div class="t-desktop-window__controls" @mousedown.stop>
        <button
          type="button"
          class="t-desktop-window__btn"
          :aria-label="t('admin.desktop.window.minimize', 'Minimize')"
          :title="t('admin.desktop.window.minimize', 'Minimize')"
          @click="desktop.minimize(win.id)"
        >
          <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true">
            <path d="M0 5h10" stroke="currentColor" stroke-width="1" />
          </svg>
        </button>
        <button
          type="button"
          class="t-desktop-window__btn"
          :aria-label="
            win.maximized
              ? t('admin.desktop.window.restore', 'Restore')
              : t('admin.desktop.window.maximize', 'Maximize')
          "
          :title="
            win.maximized
              ? t('admin.desktop.window.restore', 'Restore')
              : t('admin.desktop.window.maximize', 'Maximize')
          "
          @click="onToggleMaximize"
        >
          <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true">
            <rect
              v-if="!win.maximized"
              x="0.5"
              y="0.5"
              width="9"
              height="9"
              fill="none"
              stroke="currentColor"
              stroke-width="1"
            />
            <g v-else fill="none" stroke="currentColor" stroke-width="1">
              <rect x="0.5" y="2.5" width="7" height="7" />
              <path d="M2.5 2.5V0.5h7v7h-2" />
            </g>
          </svg>
        </button>
        <button
          type="button"
          class="t-desktop-window__btn t-desktop-window__btn--close"
          :aria-label="t('admin.desktop.window.close', 'Close')"
          :title="t('admin.desktop.window.close', 'Close')"
          @click="desktop.close(win.id)"
        >
          <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true">
            <path d="M0 0l10 10M10 0L0 10" stroke="currentColor" stroke-width="1" />
          </svg>
        </button>
      </div>
    </header>

    <div class="t-desktop-window__body">
      <TDesktopWindowNav
        v-if="showNav"
        :items="navItems"
        :module-label="moduleNode?.label ?? ''"
        :module-icon="moduleNode?.icon"
        :active-key="navActiveKey"
        :collapsed="navCollapsed"
        :translate="translate"
        @select="onNavSelect"
        @update:collapsed="navCollapsedByUser = $event"
      />
      <div class="t-desktop-window__page">
        <TDesktopWindowHost :window-id="win.id" :translate="translate" />
      </div>
    </div>

    <!-- Eight grips. Kept out of the body so a resize never lands on the page
         inside, and pointer-events are dropped while maximised. -->
    <template v-if="resizable">
      <span
        v-for="dir in RESIZE_DIRECTIONS"
        :key="dir"
        class="t-desktop-window__grip"
        :class="`t-desktop-window__grip--${dir}`"
        :style="{ cursor: RESIZE_CURSORS[dir] }"
        @mousedown="onResize($event, dir)"
      />
    </template>
  </section>
</template>

<style scoped>
.t-desktop-window {
  position: absolute;
  display: flex;
  flex-direction: column;
  min-width: 360px;
  min-height: 240px;
  overflow: hidden;
  /* No background of its own. The PAGE below carries the opaque one, which is
     what lets the title bar and the module nav frost against the wallpaper (and
     against whatever window is behind this one) instead of against this frame's
     own white - the reason the title bar's alpha was inert before. */
  background: transparent;
  border: 1px solid var(--tnzi-desktop-window-border);
  border-radius: var(--tnzi-desktop-window-radius);
  box-shadow: var(--tnzi-desktop-window-shadow);
  transition: box-shadow 0.15s ease, left 0.12s ease, top 0.12s ease,
    width 0.12s ease, height 0.12s ease;
  animation: t-desktop-winpop 0.14s cubic-bezier(0.2, 0.8, 0.2, 1);
}

@keyframes t-desktop-winpop {
  from {
    opacity: 0;
    transform: scale(0.965) translateY(10px);
  }

  to {
    opacity: 1;
    transform: none;
  }
}

/* Animating geometry while the pointer is moving makes the window trail the
   cursor. Suppress for the duration of the gesture only. */
.t-desktop-window--gesturing {
  transition: none;
  user-select: none;
}

/* Focus reads as ELEVATION, not as a coloured outline. A primary-tinted border
   is the vocabulary this package already uses for "this input is focused"; on a
   window frame it says the wrong thing, and with four windows open the one in
   front is the one that looks lifted, not the one that looks selected. */
.t-desktop-window--active {
  border-color: var(--tnzi-desktop-window-border-active);
  box-shadow: var(--tnzi-desktop-window-shadow-active);
}

.t-desktop-window--maximized {
  border-radius: 0;
}

.t-desktop-window__bar {
  display: flex;
  flex: 0 0 auto;
  gap: 8px;
  align-items: center;
  height: 36px;
  padding-left: 11px;
  cursor: default;
  color: var(--tnzi-desktop-window-bar-fg, inherit);
  background: color-mix(
    in srgb,
    var(--tnzi-desktop-window-bar-bg) var(--tnzi-desktop-solidity),
    transparent
  );
  border-bottom: 1px solid var(--tnzi-border);
  backdrop-filter: var(--tnzi-desktop-backdrop);
}

.t-desktop-window__chip {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  color: #fff;
}

.t-desktop-window__title {
  flex: 1 1 auto;
  overflow: hidden;
  font-size: 12.5px;
  font-weight: 500;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-desktop-window__controls {
  display: flex;
  flex: 0 0 auto;
  align-self: stretch;
}

/* Full-bar-height hit areas, Windows-style: the buttons run to the top-right
   corner of the frame so the close target is reachable by throwing the pointer
   at the corner. A floating pill with a margin around it gives that corner
   back to the drag handle. */
.t-desktop-window__btn {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  width: 44px;
  padding: 0;
  color: inherit;
  cursor: default;
  background: transparent;
  border: none;
  border-radius: 0;
  opacity: 0.75;
  transition: background-color 0.12s ease, opacity 0.12s ease;
}

.t-desktop-window__btn:hover {
  background: var(--tnzi-desktop-window-hover);
  opacity: 1;
}

.t-desktop-window__btn--close:hover {
  color: #fff;
  background: #c42b1c;
  opacity: 1;
}

/* A ROW now: the module nav on the left, the page on the right. */
.t-desktop-window__body {
  display: flex;
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
}

/* The window's opaque surface lives here, not on the frame: page content is
   the one thing that must NOT sit on a live backdrop. A dense table read
   through a blurred wallpaper is the point at which translucency stops being
   decoration and starts costing legibility. */
.t-desktop-window__page {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  background: var(--tnzi-container-bg, #fff);
  /* Flex children default to `min-width: auto`; without this a wide table
     inside the page pushes the whole window instead of scrolling itself. */
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}

/* Grips: 6px hit area on edges, 12px squares at the corners so the corner wins
   over the two edges it overlaps. */
.t-desktop-window__grip {
  position: absolute;
}

.t-desktop-window__grip--n,
.t-desktop-window__grip--s {
  right: 12px;
  left: 12px;
  height: 6px;
}

.t-desktop-window__grip--n { top: -3px; }
.t-desktop-window__grip--s { bottom: -3px; }

.t-desktop-window__grip--e,
.t-desktop-window__grip--w {
  top: 12px;
  bottom: 12px;
  width: 6px;
}

.t-desktop-window__grip--e { right: -3px; }
.t-desktop-window__grip--w { left: -3px; }

.t-desktop-window__grip--ne,
.t-desktop-window__grip--nw,
.t-desktop-window__grip--se,
.t-desktop-window__grip--sw {
  width: 12px;
  height: 12px;
}

.t-desktop-window__grip--nw { top: -3px; left: -3px; }
.t-desktop-window__grip--ne { top: -3px; right: -3px; }
.t-desktop-window__grip--sw { bottom: -3px; left: -3px; }
.t-desktop-window__grip--se { bottom: -3px; right: -3px; }
</style>
