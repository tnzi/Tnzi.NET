<script setup lang="ts">
/**
 * `TDesktopHost` - the desktop surface: wallpaper, icon grid, windows, taskbar.
 *
 * Mounted by `router/AdminShellRoot.vue` in place of `TAdminRouterView` when the
 * layout mode is `desktop`, and lazily so an application that never turns the
 * mode on pays nothing for it.
 *
 * It owns exactly two things the pieces below it cannot:
 *
 *  1. **Surface measurement.** Windows live in absolute coordinates relative to
 *     this element, not the viewport, so drag bounds and maximise need its real
 *     size. Measured with a ResizeObserver rather than `window.innerWidth` -
 *     the taskbar is outside it.
 *  2. **Restoring persisted windows into the current surface.** Geometry is
 *     persisted, viewports are not: a window saved at x=1800 on a wide monitor
 *     is entirely off-canvas on a laptop, with no affordance left to drag it
 *     back. The store cannot clamp at hydrate time because nothing has been
 *     measured yet, so it happens here on the first measurement.
 */
import { computed, inject, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NDropdown } from 'naive-ui'
import { THEME_CONTEXT_KEY, type ThemeContext } from '@tnzi/ui'
import { useAdminDesktopStore } from '../../stores/useAdminDesktopStore'
import type { AdminMenuItem } from '../../stores/useAdminRouteStore'
import { firstOpenableLeaf, menuItemToWindowInput } from './desktop-labels'
import TDesktopIcons from './TDesktopIcons.vue'
import TDesktopWindow from './TDesktopWindow.vue'
import TDesktopTaskbar from './TDesktopTaskbar.vue'

const props = defineProps<{
  /** Product name for the taskbar's left edge and the start-menu footer. */
  brand?: string
  brandIcon?: string
  /** The shell's own theme-button gate, forwarded to the tray. */
  showThemeBtn?: boolean
  translate?: (key: string, fallback?: string) => string
}>()

const t = (key: string, fallback: string): string => props.translate?.(key, fallback) ?? fallback

const router = useRouter()
const desktop = useAdminDesktopStore()

const surfaceEl = ref<HTMLElement | null>(null)
const surface = ref({ width: 0, height: 0 })

let observer: ResizeObserver | undefined
/** The full fit (size included) is a one-off for geometry restored from a
 *  previous session; after that only the reachability rescue runs. */
let restored = false

function measure(): void {
  const el = surfaceEl.value
  if (!el) return
  surface.value = { width: el.clientWidth, height: el.clientHeight }
  if (surface.value.width <= 0 || surface.value.height <= 0) return
  if (!restored) {
    restored = true
    desktop.clampAll(surface.value)
    return
  }
  // Every later resize, not just the first. Windows keep their place relative
  // to the surface, so the layout follows the browser instead of drifting; the
  // old code tracked only maximised windows and left the rest stranded off the
  // edge with nothing to grab. Position only - see the store.
  desktop.reflowToSurface(surface.value)
}

onMounted(() => {
  measure()
  if (typeof ResizeObserver === 'undefined') {
    window.addEventListener('resize', measure)
    return
  }
  observer = new ResizeObserver(measure)
  if (surfaceEl.value) observer.observe(surfaceEl.value)
})

onBeforeUnmount(() => {
  observer?.disconnect()
  window.removeEventListener('resize', measure)
})

/**
 * Open a menu entry.
 *
 * Both halves of the decision live in the store - the one-window-per-module
 * rule (`openOrFocusRoute`) so that chrome outside the desktop reaches the same
 * behaviour, and sizing/placement (`headless/window-sizing`) so a remembered
 * size survives. What is left here is the one thing only this component knows:
 * that a BRANCH node means "the module" rather than a page.
 */
function openMenuItem(item: AdminMenuItem): void {
  const target = firstOpenableLeaf(item)
  // A branch of pure labels: nothing behind it to mount.
  if (!target) return

  desktop.openOrFocusRoute(menuItemToWindowInput(target, router), {
    focusOnly: !!item.children?.length,
  })
}

/**
 * Whether an event landed on bare wallpaper.
 *
 * `.self` is not usable here: the icon grid is `inset: 0`, so it covers the
 * whole surface and every wallpaper click has the grid - not the surface - as
 * its target. (That is why the old `@mousedown.self` handler never fired.) Ask
 * what the event did NOT land on instead.
 */
function isBareWallpaper(e: Event): boolean {
  const el = e.target as HTMLElement | null
  return !el?.closest('.t-desktop-window, .t-desktop-icons__item')
}

/** Clicking bare wallpaper blurs the focused window, like a real desktop. */
function onSurfaceMouseDown(e: MouseEvent): void {
  contextShow.value = false
  if (!isBareWallpaper(e)) return
  desktop.activeId = null
}

// ── Wallpaper context menu ──────────────────────────────────────────────────
// The commands a desktop is expected to answer a right-click with, minus the
// ones this shell has no concept of (new file, arrange icons - the grid order
// is the menu order and is not the user's to rearrange).
const themeContext = inject<ThemeContext | undefined>(THEME_CONTEXT_KEY, undefined)
const contextShow = ref(false)
const contextPosition = ref({ x: 0, y: 0 })

const isDarkSchema = computed(() => themeContext?.settings.value.mode === 'dark')

const contextOptions = computed(() => {
  const hasWindows = desktop.windows.length > 0
  return [
    {
      key: 'minimizeAll',
      label: t('admin.desktop.context.minimizeAll', 'Minimize all windows'),
      disabled: !hasWindows,
    },
    {
      key: 'closeAll',
      label: t('admin.desktop.context.closeAll', 'Close all windows'),
      disabled: !hasWindows,
    },
    {
      key: 'resetLayout',
      label: t('admin.desktop.context.resetLayout', 'Reset window sizes'),
      disabled: !hasWindows,
    },
    { key: 'divider', type: 'divider' },
    {
      key: 'theme',
      label: isDarkSchema.value
        ? t('admin.desktop.context.useLight', 'Switch to light')
        : t('admin.desktop.context.useDark', 'Switch to dark'),
      disabled: !themeContext,
    },
  ]
})

function onSurfaceContextMenu(e: MouseEvent): void {
  // A right-click inside a window belongs to that window (and to the page it
  // hosts, which may run its own menu). Icons have no menu of their own yet, so
  // they fall through to the desktop's - better than handing the user the
  // browser's "Reload / View source" list on top of an app shell.
  if ((e.target as HTMLElement | null)?.closest('.t-desktop-window')) return
  e.preventDefault()
  contextPosition.value = { x: e.clientX, y: e.clientY }
  contextShow.value = true
}

function onContextSelect(key: string): void {
  contextShow.value = false
  switch (key) {
    case 'minimizeAll':
      for (const win of desktop.windows) desktop.minimize(win.id)
      break
    case 'closeAll':
      desktop.closeAll()
      break
    case 'resetLayout':
      // The escape hatch: forget remembered geometry and lay everything out
      // fresh. No amount of automatic clamping fully covers a screen that keeps
      // changing, so there has to be a way back by hand.
      desktop.resetLayout()
      break
    case 'theme':
      themeContext?.setMode(isDarkSchema.value ? 'light' : 'dark')
      break
  }
}

// A window closed while minimised must not leave the taskbar holding focus.
watch(
  () => desktop.windows.length,
  (n) => {
    if (n === 0) desktop.activeId = null
  },
)
</script>

<template>
  <div class="t-desktop">
    <!-- The wallpaper spans the WHOLE desktop, taskbar strip included, rather
         than living inside the surface above it. Both reference OSes put the
         dock ON the wallpaper, and there is a mechanical reason too: these
         layers are what the taskbar's `backdrop-filter` has to work with. While
         they stopped at the surface's bottom edge the bar was frosting the root
         element's flat fill - and a blurred flat colour is the same flat colour,
         so the glass did nothing at all no matter how it was tuned. -->
    <!-- Two bloom layers over the base colour. Separate elements rather than
         one multi-stop background so a consumer can retint or drop either
         half through its own token without restating the whole stack. -->
    <div class="t-desktop__glow t-desktop__glow--1" aria-hidden="true" />
    <div class="t-desktop__glow t-desktop__glow--2" aria-hidden="true" />
    <!-- Optional photo, and the scrim that keeps the icon labels readable on
         it. Both collapse to nothing when no image is set: the token is unset,
         so `background-image` is `none` and the scrim is fully transparent. -->
    <div class="t-desktop__photo" aria-hidden="true" />
    <div class="t-desktop__scrim" aria-hidden="true" />
    <div
      ref="surfaceEl"
      class="t-desktop__surface"
      @mousedown="onSurfaceMouseDown"
      @contextmenu="onSurfaceContextMenu"
    >

      <TDesktopIcons @open="openMenuItem" />

      <!-- Every open window stays mounted for as long as it is open; minimising
           only hides it (see TDesktopWindow). Rendering in stacking order keeps
           z-index and DOM order in agreement. -->
      <TDesktopWindow
        v-for="win in desktop.windows"
        :key="win.id"
        :win="win"
        :surface="surface"
        :active="desktop.activeId === win.id"
        :translate="translate"
      />

      <NDropdown
        trigger="manual"
        placement="bottom-start"
        :show="contextShow"
        :x="contextPosition.x"
        :y="contextPosition.y"
        :options="contextOptions"
        @select="onContextSelect"
        @clickoutside="contextShow = false"
      />
    </div>

    <TDesktopTaskbar
      :brand="brand"
      :brand-icon="brandIcon"
      :show-theme-btn="showThemeBtn"
      :translate="translate"
      @open="openMenuItem"
    >
      <template #tray>
        <slot name="tray" />
      </template>
    </TDesktopTaskbar>
  </div>
</template>

<style scoped>
.t-desktop {
  position: relative;
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
  /* Wallpaper is a token so a consumer can drop in a brand image or gradient
     without forking the component. */
  background: var(--tnzi-desktop-wallpaper);
}

.t-desktop__surface {
  position: relative;
  flex: 1 1 auto;
  min-height: 0;
  /* Deliberately NOT `overflow: hidden`. This box stops at the taskbar's top
     edge, so clipping here means a window can never paint into the taskbar
     strip - and "a window sliding under the bar and showing through it blurred"
     is the single most recognisable thing about this material. The outer
     `.t-desktop` still clips at the viewport, and the icon layer clips itself,
     so nothing else changes. Windows stay reachable because the drag clamp
     keeps `MIN_VISIBLE` of the title bar above this box's bottom edge. */
  overflow: visible;
}

/* Bloom layers. `pointer-events: none` matters: without it they would swallow
   the wallpaper mousedown that blurs the focused window and the right-click
   that opens the context menu, because they cover the full surface. */
.t-desktop__glow {
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.t-desktop__glow--1 {
  background: var(--tnzi-desktop-glow-1);
}

.t-desktop__glow--2 {
  background: var(--tnzi-desktop-glow-2);
}

.t-desktop__photo {
  position: absolute;
  inset: 0;
  pointer-events: none;
  background-image: var(--tnzi-desktop-wallpaper-image, none);
  background-repeat: no-repeat;
  background-position: center;
  background-size: cover;
}

/* Sits between the photo and the icons. Its opacity is the only thing standing
   between a bright photo and unreadable icon labels. */
.t-desktop__scrim {
  position: absolute;
  inset: 0;
  pointer-events: none;
  background: rgb(0 0 0 / calc(var(--tnzi-desktop-wallpaper-scrim, 0) * 100%));
}
</style>
