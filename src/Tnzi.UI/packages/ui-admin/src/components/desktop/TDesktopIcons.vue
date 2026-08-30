<script setup lang="ts">
/**
 * `TDesktopIcons` - the icon grid on the desktop surface.
 *
 * The source is `useAdminRouteStore().menus`, which is already filtered by
 * permission and module availability - so the desktop shows exactly what the
 * sidebar would, and a user never sees an icon they cannot open. Nothing here
 * needs its own access rules.
 *
 * **One icon per TOP-LEVEL entry**, not one per page. A module is a single tile
 * whose pages live in the window's own left nav once it is open - the same way
 * an application icon opens an application, not one icon per screen inside it.
 * Flattening instead put a hundred-odd tiles on the wallpaper, which is a
 * launcher, not a desktop.
 *
 * A top-level entry with no openable page anywhere beneath it renders nothing -
 * it is a label, and opening it would mount an empty window.
 *
 * Each icon is a colour-filled app tile rather than the sidebar's monochrome
 * glyph - see `desktop-tints.ts` for why, and for where the colour comes from.
 *
 * Layout is a fixed row-major grid - see the note on `.t-desktop-icons` for why
 * it is not the column-major fill the metaphor would suggest.
 */
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminRouteStore, type AdminMenuItem } from '../../stores/useAdminRouteStore'
import { normalizeNavBadge } from '../../utils/nav-badge'
import { firstOpenableLeaf, resolveWindowIcon } from './desktop-labels'
import { desktopTileStyle, useDesktopTint } from './desktop-tints'

/** Cell box, kept in sync with the CSS below. */
const CELL_HEIGHT = 92
const CELL_GAP = 2
const GRID_PADDING_Y = 10
/**
 * Height held back so the bottom row is never clipped by a horizontal
 * scrollbar. Generous against the 8px bar styled below, because a non-WebKit
 * engine draws its own.
 */
const SCROLLBAR_RESERVE = 12

// No `translate` prop: labels arrive pre-resolved on the menu items and the
// icon grid has no chrome copy of its own.
const emit = defineEmits<{ open: [item: AdminMenuItem] }>()

const routeStore = useAdminRouteStore()
const tintFor = useDesktopTint()

// ── Row count ───────────────────────────────────────────────────────────────
const gridEl = ref<HTMLElement | null>(null)
const gridHeight = ref(0)
let observer: ResizeObserver | undefined

/**
 * `offsetHeight`, NOT `clientHeight` - this is the whole guard.
 *
 * A horizontal scrollbar is drawn inside the padding box, so `clientHeight`
 * DROPS by the bar's height the moment one appears. Deriving the row count from
 * it means: bar appears -> one fewer row -> one more column -> every icon
 * moves. `offsetHeight` is the border box and ignores the bar entirely, so the
 * row count depends on the container's size alone and the bar can no longer
 * feed back into the layout that produced it.
 */
function measure(): void {
  if (gridEl.value) gridHeight.value = gridEl.value.offsetHeight
}

onMounted(() => {
  measure()
  if (typeof ResizeObserver === 'undefined') {
    window.addEventListener('resize', measure)
    return
  }
  observer = new ResizeObserver(measure)
  if (gridEl.value) observer.observe(gridEl.value)
})

onBeforeUnmount(() => {
  observer?.disconnect()
  window.removeEventListener('resize', measure)
})

/**
 * How many icons fit in one column.
 *
 * Falls back to a sane column before the first measurement (and under
 * happy-dom, where layout metrics are 0) rather than collapsing to one row.
 */
const rowCount = computed(() => {
  const usable = gridHeight.value - GRID_PADDING_Y * 2 - SCROLLBAR_RESERVE
  if (usable <= 0) return 6
  return Math.max(1, Math.floor((usable + CELL_GAP) / (CELL_HEIGHT + CELL_GAP)))
})

const gridStyle = computed(() => ({
  gridTemplateRows: `repeat(${rowCount.value}, ${CELL_HEIGHT}px)`,
}))

/** Top-level entries that have something to open, in menu order. */
const icons = computed<AdminMenuItem[]>(() =>
  routeStore.menus.filter((item) => firstOpenableLeaf(item) !== null),
)

/**
 * The count to paint on a module tile.
 *
 * Its own badge wins; otherwise the numeric badges of everything beneath it are
 * summed. The sidebar does not aggregate because it shows the children inline -
 * here they are HIDDEN behind one tile, so a count that only becomes visible
 * after you open the module answers the question too late.
 *
 * Non-numeric badges (a short marker like "NEW") are not summable, so a module
 * carrying only those shows nothing rather than a wrong number.
 */
function badgeSourceOf(item: AdminMenuItem): string | number | null | undefined {
  if (item.badge !== undefined && item.badge !== null) return item.badge
  if (!item.children?.length) return item.badge
  let total = 0
  const walk = (nodes: AdminMenuItem[]): void => {
    for (const node of nodes) {
      const n = Number(node.badge)
      if (Number.isFinite(n)) total += n
      if (node.children?.length) walk(node.children)
    }
  }
  walk(item.children)
  return total > 0 ? total : undefined
}

const selected = ref<string | null>(null)

/** `AdminMenuItem.label` is already resolved by the route store
 *  (`resolveI18nKey`), so it goes straight to the screen. Running it back
 *  through the window-title resolver would hand an already-translated string
 *  to `humanise` as if it were a key. */
function labelOf(item: AdminMenuItem): string {
  return item.label
}

function iconOf(item: AdminMenuItem): string {
  return resolveWindowIcon(item.icon, item.key)
}

/** `meta.color` overrides; otherwise the module the route belongs to decides. */
function tileStyleOf(item: AdminMenuItem): Record<string, string> {
  const tint = tintFor(item.key, (item.meta as { color?: string } | undefined)?.color)
  return desktopTileStyle(tint, 44, 10)
}

/** Same display rule the sidebar uses: zero / blank paint nothing, counts
 *  cap at 99+. One rule, one place. */
function badgeOf(item: AdminMenuItem): string | null {
  return normalizeNavBadge(badgeSourceOf(item))
}

/**
 * Whether to paint hover at all.
 *
 * A window opening does not move the mouse, and a browser only re-evaluates
 * `:hover` when the pointer moves - so the icon you just double-clicked keeps
 * its highlight while you are already working in the window that covers it.
 * Suppressed on activate, released the moment the pointer moves again, which is
 * also the moment hover means something again.
 */
const hoverSuppressed = ref(false)

function onActivate(item: AdminMenuItem): void {
  // Drop the selection: the window that is about to open takes the focus, and
  // a highlight left behind on the desktop claims something is still selected
  // there. (Single click still selects - `click` fires before `dblclick`, so
  // this clears the selection that opening it just made.)
  selected.value = null
  hoverSuppressed.value = true
  emit('open', item)
}

/** Keyboard parity: Enter/Space opens the focused icon. */
function onKeydown(e: KeyboardEvent, item: AdminMenuItem): void {
  if (e.key !== 'Enter' && e.key !== ' ') return
  e.preventDefault()
  onActivate(item)
}
</script>

<template>
  <div
    ref="gridEl"
    class="t-desktop-icons"
    :class="{ 't-desktop-icons--no-hover': hoverSuppressed }"
    :style="gridStyle"
    @click.self="selected = null"
    @mousemove="hoverSuppressed = false"
  >
    <button
      v-for="item in icons"
      :key="item.key"
      type="button"
      class="t-desktop-icons__item"
      :class="{ 't-desktop-icons__item--selected': selected === item.key }"
      :title="labelOf(item)"
      @click="selected = item.key"
      @dblclick="onActivate(item)"
      @keydown="onKeydown($event, item)"
    >
      <span class="t-desktop-icons__tile" :style="tileStyleOf(item)">
        <TSvgIcon :icon="iconOf(item)" :size="24" aria-hidden="true" />
        <span v-if="badgeOf(item) !== null" class="t-desktop-icons__badge">{{ badgeOf(item) }}</span>
      </span>
      <span class="t-desktop-icons__label">{{ labelOf(item) }}</span>
    </button>
  </div>
</template>

<style scoped>
/**
 * Column-major, anchored left: icons fill top-to-bottom, then start a new
 * column to the right - the desktop fill order.
 *
 * The row count comes from JS, NOT from `repeat(auto-fill, …)`, and that is the
 * whole trick. `auto-fill` derives the row count from the container's CONTENT
 * height while the grid scrolls horizontally, and a horizontal scrollbar is
 * drawn inside that box: a circular dependency. It was measured biting here
 * once - opening a window brought the bar in, the grid went 7 rows to 6, and
 * every icon moved; a second double-click at the same coordinates launched a
 * different feature. `scrollbar-gutter` cannot fix it, it only reserves space
 * for the BLOCK-direction (vertical) bar.
 *
 * Measuring the BORDER box (`offsetHeight`) cuts the loop: it ignores the bar,
 * so the row count is a function of the container's size alone and is identical
 * with or without one. See the note on `measure()`.
 *
 * Position stability matters more here than anywhere else in the package -
 * people navigate a desktop by muscle memory, and a grid that reshuffles under
 * them hands the wrong page to a double-click.
 */
.t-desktop-icons {
  position: absolute;
  inset: 0;
  display: grid;
  grid-auto-flow: column;
  grid-auto-columns: 84px;
  gap: 2px;
  align-content: start;
  justify-content: start;
  padding: 10px 8px;
  overflow-x: auto;
  overflow-y: hidden;
}

/* A thin, wallpaper-toned bar - the desktop has no chrome for a default one to
   sit against. */
.t-desktop-icons::-webkit-scrollbar {
  height: 8px;
}

.t-desktop-icons::-webkit-scrollbar-thumb {
  background: rgb(255 255 255 / 28%);
  border-radius: 4px;
}

.t-desktop-icons::-webkit-scrollbar-track {
  background: transparent;
}

.t-desktop-icons__item {
  display: flex;
  flex-direction: column;
  gap: 6px;
  align-items: center;
  justify-content: flex-start;
  /* Narrower than its 84px grid track, and centred in it: the TRACK geometry is
     what keeps icon positions and the row count stable, while this box is only
     the hover/selection highlight. At full track width the highlight ran 20px
     clear of the 44px tile on each side and read as a panel around the icon
     rather than a highlight of it. The height still matches the row - a
     two-line label needs all of it. */
  justify-self: center;
  width: 72px;
  height: 92px;
  padding: 7px 2px;
  color: var(--tnzi-desktop-icon-color, #fff);
  cursor: default;
  background: transparent;
  /* The selection rectangle is a 1px inset border, the Windows idiom: it has
     to be present-but-transparent at rest or hovering an icon would shift the
     tile by a pixel. */
  border: 1px solid transparent;
  border-radius: 4px;
  transition: background-color 0.12s ease, border-color 0.12s ease;
}

/* Hover is meaningless until the pointer moves again - see `hoverSuppressed`.
   Painting the base values rather than dropping the rule keeps the transition,
   so the highlight fades out instead of vanishing mid-animation. */
.t-desktop-icons--no-hover .t-desktop-icons__item:hover {
  background: transparent;
  border-color: transparent;
  backdrop-filter: none;
}

/* The glass goes on the SELECTION CHIP, not on the tile. The tile's colour is
   how you tell one module from another at a glance; frosting it would mute the
   one signal it carries. Only hover/selected get a backdrop, so the wallpaper
   is not being blurred behind fifteen invisible rectangles at rest. */
.t-desktop-icons__item:hover {
  background: rgb(140 180 255 / 18%);
  border-color: rgb(170 205 255 / 32%);
  backdrop-filter: var(--tnzi-desktop-backdrop);
}

.t-desktop-icons__item--selected,
.t-desktop-icons__item:focus-visible {
  background: rgb(140 180 255 / 30%);
  border-color: rgb(180 212 255 / 55%);
  outline: none;
  backdrop-filter: var(--tnzi-desktop-backdrop);
}

.t-desktop-icons__tile {
  position: relative;
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  color: #fff;
}

.t-desktop-icons__badge {
  position: absolute;
  top: -5px;
  right: -6px;
  min-width: 17px;
  padding: 0 4px;
  font-size: 10px;
  font-weight: 600;
  line-height: 17px;
  color: #fff;
  text-align: center;
  background: var(--tnzi-error, #d03050);
  border: 1.5px solid rgb(255 255 255 / 55%);
  border-radius: 9px;
}

.t-desktop-icons__label {
  display: -webkit-box;
  overflow: hidden;
  font-size: 11.5px;
  line-height: 1.25;
  text-align: center;
  overflow-wrap: anywhere;
  text-shadow: 0 1px 3px rgb(0 0 0 / 75%);
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}
</style>
