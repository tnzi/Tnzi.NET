<template>
  <div
    ref="rootRef"
    class="t-admin-tabs"
    :class="{ 't-admin-tabs--fixed': fixed }"
    :data-style="themeStore.tabStyle"
  >
    <div ref="listRef" class="t-admin-tabs__list">
      <!-- Two branches (draggable / static) instead of <component :is>
           because VueDraggable + dynamic component triggers happy-dom
           Vue-warn cascades during integration tests. -->
      <VueDraggable
        v-if="draggable"
        v-model="draggableList"
        class="t-admin-tabs__draggable"
        handle=".t-admin-tabs__tab"
        :animation="200"
      >
        <div
          v-for="tab in allTabs"
          :key="tab.id"
          :ref="(el) => onTabRef(tab.id, el)"
          class="t-admin-tabs__tab"
          :class="{
            't-admin-tabs__tab--active': tab.id === tabStore.activeTabId,
            't-admin-tabs__tab--home': tab.id === tabStore.homeTab?.id,
            't-admin-tabs__tab--pinned': tabStore.isTabPinned(tab.id),
          }"
          @click="onTabClick(tab)"
          @mousedown="onMouseDown(tab, $event)"
          @contextmenu.prevent="onContextMenu(tab, $event)"
          @touchstart.passive="onTouchStart(tab, $event)"
          @touchmove.passive="onTouchMove(tab)"
          @touchend.passive="onTouchEnd(tab)"
          @touchcancel.passive="onTouchEnd(tab)"
        >
          <span
            v-if="themeStore.tabStyle === 'chrome'"
            class="t-admin-tabs__chrome-bg"
            aria-hidden="true"
          >
            <TChromeTabBg />
          </span>
          <TSvgIcon
            :icon="tabIconOf(tab)"
            :size="16"
            class="t-admin-tabs__icon"
            aria-hidden="true"
          />
          <span class="t-admin-tabs__title" :title="renderTitle(tab.title)">{{ renderTitle(tab.title) }}</span>
          <button
            v-if="!tabStore.isTabRetain(tab.id) && themeStore.tabStyle !== 'slider'"
            class="t-admin-tabs__close"
            aria-label="Close tab"
            @click.stop="tabStore.removeTab(tab.id)"
          >
            <svg viewBox="0 0 1024 1024" width="10" height="10" aria-hidden="true">
              <path
                d="M563.8 512l229-229c14.2-14.2 14.2-37.4 0-51.6-14.2-14.2-37.4-14.2-51.6 0L512 460.4 283.4 231.4c-14.2-14.2-37.4-14.2-51.6 0-14.2 14.2-14.2 37.4 0 51.6l229 229-229 229c-14.2 14.2-14.2 37.4 0 51.6 14.2 14.2 37.4 14.2 51.6 0L512 563.6l228.6 228.6c14.2 14.2 37.4 14.2 51.6 0 14.2-14.2 14.2-37.4 0-51.6L563.8 512z"
                fill="currentColor"
              />
            </svg>
          </button>
          <span
            v-if="themeStore.tabStyle === 'chrome'"
            class="t-admin-tabs__chrome-divider"
            aria-hidden="true"
          />
        </div>
      </VueDraggable>
      <div v-else class="t-admin-tabs__draggable">
        <div
          v-for="tab in allTabs"
          :key="tab.id"
          :ref="(el) => onTabRef(tab.id, el)"
          class="t-admin-tabs__tab"
          :class="{
            't-admin-tabs__tab--active': tab.id === tabStore.activeTabId,
            't-admin-tabs__tab--home': tab.id === tabStore.homeTab?.id,
            't-admin-tabs__tab--pinned': tabStore.isTabPinned(tab.id),
          }"
          @click="onTabClick(tab)"
          @mousedown="onMouseDown(tab, $event)"
          @contextmenu.prevent="onContextMenu(tab, $event)"
          @touchstart.passive="onTouchStart(tab, $event)"
          @touchmove.passive="onTouchMove(tab)"
          @touchend.passive="onTouchEnd(tab)"
          @touchcancel.passive="onTouchEnd(tab)"
        >
          <span
            v-if="themeStore.tabStyle === 'chrome'"
            class="t-admin-tabs__chrome-bg"
            aria-hidden="true"
          >
            <TChromeTabBg />
          </span>
          <TSvgIcon
            :icon="tabIconOf(tab)"
            :size="16"
            class="t-admin-tabs__icon"
            aria-hidden="true"
          />
          <span class="t-admin-tabs__title" :title="renderTitle(tab.title)">{{ renderTitle(tab.title) }}</span>
          <button
            v-if="!tabStore.isTabRetain(tab.id) && themeStore.tabStyle !== 'slider'"
            class="t-admin-tabs__close"
            aria-label="Close tab"
            @click.stop="tabStore.removeTab(tab.id)"
          >
            <svg viewBox="0 0 1024 1024" width="10" height="10" aria-hidden="true">
              <path
                d="M563.8 512l229-229c14.2-14.2 14.2-37.4 0-51.6-14.2-14.2-37.4-14.2-51.6 0L512 460.4 283.4 231.4c-14.2-14.2-37.4-14.2-51.6 0-14.2 14.2-14.2 37.4 0 51.6l229 229-229 229c-14.2 14.2-14.2 37.4 0 51.6 14.2 14.2 37.4 14.2 51.6 0L512 563.6l228.6 228.6c14.2 14.2 37.4 14.2 51.6 0 14.2-14.2 14.2-37.4 0-51.6L563.8 512z"
                fill="currentColor"
              />
            </svg>
          </button>
          <span
            v-if="themeStore.tabStyle === 'chrome'"
            class="t-admin-tabs__chrome-divider"
            aria-hidden="true"
          />
        </div>
      </div>
    </div>

    <div class="t-admin-tabs__actions">
      <button
        v-if="showReload"
        class="t-admin-tabs__reload"
        :class="{ 't-admin-tabs__reload--loading': !appStore.reloadFlag }"
        aria-label="Reload"
        @click="appStore.reloadPage()"
      >
        <!-- Phase H2 D5: iconify reload + spin animation while
             reloadFlag is false (mirrors soybean's animate-spin). -->
        <TSvgIcon icon="mdi:refresh" :size="18" />
      </button>
      <!-- Phase H3 D4: Fullscreen button in the tab bar (soybean
           `global-tab/index.vue:220-221` places it here, not in the
           header). Toggles `appStore.fullContent`. -->
      <button
        v-if="showFullscreen"
        class="t-admin-tabs__action"
        :aria-label="appStore.fullContent ? 'Exit fullscreen' : 'Fullscreen'"
        @click="appStore.toggleFullContent()"
      >
        <TSvgIcon
          :icon="appStore.fullContent ? 'mdi:fullscreen-exit' : 'mdi:fullscreen'"
          :size="18"
        />
      </button>
    </div>

    <NDropdown
      :options="contextOptions"
      :show="contextVisible"
      :x="contextX"
      :y="contextY"
      trigger="manual"
      placement="bottom-start"
      @select="onContextSelect"
      @clickoutside="contextVisible = false"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, h, nextTick, watch, onBeforeUnmount } from 'vue'
import { NDropdown } from 'naive-ui'
import { VueDraggable } from 'vue-draggable-plus'
import { useAdminTabStore, type AdminTab } from '../../stores/useAdminTabStore'
import { useAdminAppStore } from '../../stores/useAdminAppStore'
import { useAdminThemeStore } from '../../stores/useAdminThemeStore'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { humanise, translatePageKey } from '../../pages/_shared/translate'
import { TSvgIcon } from '@tnzi/ui'
import TChromeTabBg from './TChromeTabBg.vue'

interface Props {
  /**
   * Phase G: middle-click to close defaults to reading from the theme
   * store (`themeStore.closeTabByMiddleClick`, default false to match
   * soybean). Pass an explicit boolean only if you need to hard-override.
   */
  closeByMiddleClick?: boolean
  draggable?: boolean
  showReload?: boolean
  /** Phase H3 D4: fullscreen button in the tab bar (soybean parity). */
  showFullscreen?: boolean
  fixed?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  closeByMiddleClick: undefined,
  draggable: true,
  showReload: true,
  showFullscreen: true,
  fixed: false,
  translate: undefined,
})

const emit = defineEmits<{
  tabClick: [tab: AdminTab]
}>()

const tabStore = useAdminTabStore()
const appStore = useAdminAppStore()
const themeStore = useAdminThemeStore()
const bp = useBreakpoint()

const allTabs = computed<AdminTab[]>(() => tabStore.allTabs)

// Effective close-by-middle-click: prop wins, else read from store.
const effectiveCloseByMiddleClick = computed<boolean>(() =>
  props.closeByMiddleClick ?? themeStore.closeTabByMiddleClick,
)

// VueDraggable binds the mutable tab list (excludes homeTab, which is always first and pinned).
const draggableList = computed<AdminTab[]>({
  get: () => tabStore.tabs,
  set: (value) => {
    tabStore.tabs = value
  },
})

function onTabClick(tab: AdminTab): void {
  tabStore.switchRouteByTab(tab)
  emit('tabClick', tab)
}

function renderTitle(raw: string): string {
  if (props.translate) {
    const translated = props.translate(raw)
    if (translated && translated !== raw) return translated
  }
  // Bundled-locale fallback so tab titles localise without the consumer
  // having to pass `translate`. `translatePageKey` resolves absolute
  // `(tnzi.)admin.*` keys against the active locale.
  const bundled = translatePageKey('', raw)
  if (bundled && bundled !== raw) return bundled
  return humanise(raw)
}

/** Phase H1 D2: prefix icon resolver. `tab.meta.icon` wins (matches
 *  soybean's per-route `meta.icon`); fall back to a generic file icon
 *  so every tab gets a visual anchor even if the route metadata
 *  hasn't been wired with an icon yet. */
function tabIconOf(tab: AdminTab): string {
  const metaIcon = tab.meta?.icon
  if (typeof metaIcon === 'string' && metaIcon) return metaIcon
  return 'mdi:file-document-outline'
}

function onMouseDown(tab: AdminTab, event: MouseEvent): void {
  // Middle mouse button = 1
  if (
    event.button === 1 &&
    effectiveCloseByMiddleClick.value &&
    !tabStore.isTabRetain(tab.id)
  ) {
    event.preventDefault()
    tabStore.removeTab(tab.id)
  }
}

// Phase G — auto-scroll the active tab into view (mirrors soybean's
// `scrollToActiveTab`). Triggers on route change and on initial mount.
const listRef = ref<HTMLElement | null>(null)
const tabRefs = new Map<string, HTMLElement>()
function onTabRef(id: string, el: Element | null | { $el?: Element }): void {
  const dom = el instanceof HTMLElement ? el : null
  if (dom) tabRefs.set(id, dom)
  else tabRefs.delete(id)
}
const rootRef = ref<HTMLElement | null>(null)

watch(
  () => tabStore.activeTabId,
  async (id) => {
    if (!id) return
    await nextTick()
    const el = tabRefs.get(id)
    if (el && typeof el.scrollIntoView === 'function') {
      el.scrollIntoView({ behavior: 'smooth', inline: 'center', block: 'nearest' })
    }
  },
  { immediate: true },
)

// Context menu state
const contextVisible = ref(false)
const contextX = ref(0)
const contextY = ref(0)
const contextTarget = ref<AdminTab | null>(null)

const contextOptions = computed(() => {
  // Consumer-supplied translate wins; otherwise fall back to the bundled
  // locale via translatePageKey so `admin.tabs.*` keys resolve out of the
  // box instead of leaking dotted strings into the right-click menu.
  const t = (k: string) => {
    if (props.translate) {
      const hit = props.translate(k)
      if (hit && hit !== k) return hit
    }
    return translatePageKey('', k) || k
  }
  const target = contextTarget.value
  const isPinned = target ? tabStore.isTabPinned(target.id) : false
  const isHome = target ? target.id === tabStore.homeTab?.id : false
  // Phase G — 6 menu items with iconify icons, mirroring soybean's
  // global-tab/context-menu.vue. The pin/unpin item swaps based on
  // current pin state; close items are disabled for the home tab
  // and reused for pinned tabs (they self-skip via tabStore guards).
  return [
    {
      label: t('admin.tabs.closeCurrent'),
      key: 'close-current',
      disabled: isHome || isPinned,
      icon: () => h(TSvgIcon, { icon: 'mdi:close', size: 16 }),
    },
    {
      label: t('admin.tabs.closeOthers'),
      key: 'close-others',
      icon: () => h(TSvgIcon, { icon: 'mdi:close-box-multiple-outline', size: 16 }),
    },
    {
      label: t('admin.tabs.closeLeft'),
      key: 'close-left',
      icon: () => h(TSvgIcon, { icon: 'mdi:format-horizontal-align-left', size: 16 }),
    },
    {
      label: t('admin.tabs.closeRight'),
      key: 'close-right',
      icon: () => h(TSvgIcon, { icon: 'mdi:format-horizontal-align-right', size: 16 }),
    },
    {
      label: t('admin.tabs.closeAll'),
      key: 'close-all',
      icon: () => h(TSvgIcon, { icon: 'mdi:close-circle-outline', size: 16 }),
    },
    {
      label: isPinned ? t('admin.tabs.unpin') : t('admin.tabs.pin'),
      key: isPinned ? 'unpin' : 'pin',
      disabled: isHome,
      icon: () =>
        h(TSvgIcon, {
          icon: isPinned ? 'mdi:pin-off-outline' : 'mdi:pin-outline',
          size: 16,
        }),
    },
  ]
})

function onContextMenu(tab: AdminTab, event: MouseEvent): void {
  contextTarget.value = tab
  contextX.value = event.clientX
  contextY.value = event.clientY
  contextVisible.value = true
}

// Touch-driven long-press: opens the same context menu as a desktop
// right-click. Threshold (500ms) matches Android's standard long-press;
// any touchmove cancels so the user can still horizontally scroll the
// tab bar without triggering the menu.
const longPressTimers = new WeakMap<AdminTab, number>()
const longPressMoved = new WeakSet<AdminTab>()

function onTouchStart(tab: AdminTab, event: TouchEvent): void {
  if (!bp.isTouch.value) return
  longPressMoved.delete(tab)
  const touch = event.touches[0]
  if (!touch) return
  const x = touch.clientX
  const y = touch.clientY
  const timer = window.setTimeout(() => {
    if (longPressMoved.has(tab)) return
    contextTarget.value = tab
    contextX.value = x
    contextY.value = y
    contextVisible.value = true
  }, 500)
  longPressTimers.set(tab, timer)
}
function onTouchMove(tab: AdminTab): void {
  longPressMoved.add(tab)
  const timer = longPressTimers.get(tab)
  if (timer !== undefined) {
    window.clearTimeout(timer)
    longPressTimers.delete(tab)
  }
}
function onTouchEnd(tab: AdminTab): void {
  const timer = longPressTimers.get(tab)
  if (timer !== undefined) {
    window.clearTimeout(timer)
    longPressTimers.delete(tab)
  }
}
onBeforeUnmount(() => {
  // Ensure no orphaned dropdown remains once the bar unmounts.
  contextVisible.value = false
})

function onContextSelect(key: string): void {
  const target = contextTarget.value
  if (!target) return
  switch (key) {
    case 'close-current':
      tabStore.removeTab(target.id)
      break
    case 'close-left':
      tabStore.removeLeftTabs(target.id)
      break
    case 'close-right':
      tabStore.removeRightTabs(target.id)
      break
    case 'close-others':
      tabStore.removeOtherTabs(target.id)
      break
    case 'close-all':
      tabStore.clearAllTabs()
      break
    case 'pin':
      tabStore.fixTab(target.id)
      break
    case 'unpin':
      tabStore.unfixTab(target.id)
      break
  }
  contextVisible.value = false
}

defineExpose({ contextTarget, contextVisible, onContextSelect })
</script>

<style scoped>
.t-admin-tabs {
  display: flex;
  align-items: center;
  height: var(--tnzi-admin-tab-height, 44px);
  /* Phase H1 D1: flex-shrink:0 so the tab bar always paints at its
     configured height. Without it, the parent column-flex (`.t-admin-
     shell__main`) shrinks the bar to the content's preferred min-
     height (~34px) when the page area is empty (e.g. workbench
     stub) — chrome SVG arcs get cropped and the bar looks like
     button style. soybean's AdminLayout puts flex-shrink:0 on the
     same three rails (header/tab/footer). */
  flex-shrink: 0;
  background-color: var(--tnzi-admin-tab-bg, var(--tnzi-container-bg));
  border-bottom: 1px solid var(--tnzi-border);
  /* Phase H1 D7: soybean's tab bar has a subtle bottom shadow that
     separates it from the content. Earlier audit annotation claimed
     "soybean parity: no box-shadow" — that was wrong. Re-instated. */
  box-shadow: 0 1px 2px 0 rgb(0 21 41 / 0.05);
  padding: 0 8px;
  z-index: var(--tnzi-admin-z-tabs, 70);
}
.t-admin-tabs--fixed {
  position: sticky;
  top: var(--tnzi-admin-header-height, 56px);
}
.t-admin-tabs__list {
  flex: 1 1 auto;
  /* Phase H4 follow-up: list must fill the bar's full height so the
     inner __draggable's `align-items: flex-end` can actually push
     chrome/slider tabs against the bar's bottom edge. Previously the
     parent `align-items: center` left the list at intrinsic height
     (~tab content = 33px), so flex-end had no vertical room to work
     in — tabs floated in the middle of the bar. soybean's `bsWrapper`
     uses `h-full` for the same reason (`global-tab/index.vue:189`). */
  height: 100%;
  align-self: stretch;
  overflow-x: auto;
  overflow-y: hidden;
  scrollbar-width: none;
}
.t-admin-tabs__list::-webkit-scrollbar {
  display: none;
}
.t-admin-tabs__draggable {
  display: flex;
  height: 100%;
  min-width: max-content;
  /* Phase G follow-up #4: align-items differs per tab style. soybean's
     `global-tab/index.vue:193-197` uses `items-end` for chrome+slider
     (tab body sits flush against the bar's bottom edge so the SVG arc
     reads naturally) and `items-center` for button (chip floats mid-
     bar). Without this differentiation, chrome tabs floated centered
     and looked detached from the bar — the user-reported "tab style
     not matching" core issue. */
  align-items: center;
}
.t-admin-tabs[data-style='chrome'] .t-admin-tabs__draggable,
.t-admin-tabs[data-style='slider'] .t-admin-tabs__draggable {
  align-items: flex-end;
}
.t-admin-tabs__tab {
  position: relative;
  display: inline-flex;
  align-items: center;
  height: 33px;
  cursor: pointer;
  user-select: none;
  white-space: nowrap;
  /* Phase G follow-up #3: soybean's chrome/button/slider tab CSS module
     doesn't set a colour on the inactive tab — it inherits the layout-
     tab's base text colour (~rgb(31,31,31) in light mode), which reads
     ~3x stronger than `--tnzi-base-text-muted` (~rgb(75,85,99)) and
     was the user-reported "tab style not matching". Drop muted; use
     base-text so the inactive label has the same near-black weight as
     soybean. Active/hover still flip to primary in the per-style
     rules below. */
  color: var(--tnzi-base-text, #1f1f1f);
  font-size: 14px;
  /* Phase H4 D8: 300ms transition matches soybean's
     `transition-all-300` on PageTab. */
  transition: color 0.3s ease, background-color 0.3s ease;
}

/* ─── Chrome style ──────────────────────────────────────────────────
   Mirrors soybean `chrome-tab.vue` + `chrome-tab-bg.vue` + the
   `index.module.css` colour palette. Tabs overlap by 18px so the SVG
   arcs interlock; active tab z:10 + hover z:9 keep the right tab
   above. The SVG fill is driven by `.t-admin-tabs__chrome-bg`'s own
   `color` property (consumed via `fill="currentColor"` in
   TChromeTabBg) — independent of the tab's text color so we can keep
   title/close text legible. */
.t-admin-tabs[data-style='chrome'] .t-admin-tabs__draggable {
  gap: 0;
  padding-right: 18px;
}
.t-admin-tabs[data-style='chrome'] .t-admin-tabs__tab {
  margin-right: -18px;
  padding: 6px 24px;
  gap: 16px;
  /* Inactive color is inherited from `.t-admin-tabs__tab` (base-text) —
     soybean doesn't override here. Hover/active flip to primary below. */
}
.t-admin-tabs[data-style='chrome'] .t-admin-tabs__tab:hover {
  z-index: 9;
  color: var(--tnzi-primary, #646cff);
}
.t-admin-tabs[data-style='chrome'] .t-admin-tabs__tab--active {
  z-index: 10;
  color: var(--tnzi-primary, #646cff);
}
/* The SVG arc background. Default transparent; hover gets a soft grey
   (matches soybean #dee1e6); active gets primary tinted to white at 10%
   — using an opaque mixed colour (not `rgba primary / 0.10`) so the
   bg colour doesn't shift when the underlying tab-bar colour differs
   between light/dark themes. Dark mode mixes against `#1f1f1f` instead
   of `#ffffff` so the arc still reads as a tinted-but-darker chip. */
.t-admin-tabs__chrome-bg {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  pointer-events: none;
  z-index: -1;
  color: transparent;
  transition: color 0.15s ease;
}
/* Light + dark rules are BOTH wrapped in `:global(...)` so neither picks
   up Vue's `[data-v-xxx]` scope hash. If only the dark variants are
   global, the light rule's scope hash gives it +1 attribute selector
   over the dark rule and wins the cascade tie — leaving the active arc
   stuck on the light-mix colour in dark mode. The `.t-admin-tabs__chrome-bg`
   class is unique to this component, so removing scope isolation here
   doesn't risk collision. */
:global(.t-admin-tabs[data-style='chrome'] .t-admin-tabs__tab:hover .t-admin-tabs__chrome-bg) {
  color: #dee1e6;
}
:global(.t-admin-tabs[data-style='chrome'] .t-admin-tabs__tab--active .t-admin-tabs__chrome-bg),
:global(.t-admin-tabs[data-style='chrome'] .t-admin-tabs__tab--active:hover .t-admin-tabs__chrome-bg) {
  color: color-mix(in srgb, var(--tnzi-primary, #646cff) 10%, #ffffff 90%);
}
/* Dark variants — flip the mix base so the arc background reads as a
   darker tinted chip instead of a bright washed-out tile. soybean's
   `.chrome-tab_dark` uses #18181c for hover and primary@30% for active. */
:global(.dark .t-admin-tabs[data-style='chrome'] .t-admin-tabs__tab:hover .t-admin-tabs__chrome-bg) {
  color: #2b2b30;
}
:global(.dark .t-admin-tabs[data-style='chrome'] .t-admin-tabs__tab--active .t-admin-tabs__chrome-bg),
:global(.dark .t-admin-tabs[data-style='chrome'] .t-admin-tabs__tab--active:hover .t-admin-tabs__chrome-bg) {
  color: color-mix(in srgb, var(--tnzi-primary, #646cff) 22%, #1f1f1f 78%);
}

/* Vertical divider strip on the right of each chrome tab. soybean uses
   a deep `#1f2225` near-black in light mode; our previous border-token
   was too pale. Hidden on hover and on the previous neighbour's
   hover/active so the divider doesn't compete with the SVG arc. */
.t-admin-tabs__chrome-divider {
  position: absolute;
  right: 7px;
  top: 50%;
  width: 1px;
  height: 16px;
  background: #1f2225;
  transform: translateY(-50%);
  pointer-events: none;
  transition: opacity 0.15s ease;
}
/* Phase H2 D6: dark mode flips the divider to soft white (matches
   soybean `.chrome-tab_dark .chrome-tab-divider`). */
:global(.dark .t-admin-tabs__chrome-divider) {
  background: rgba(255, 255, 255, 0.9);
}
.t-admin-tabs[data-style='chrome'] .t-admin-tabs__tab--active .t-admin-tabs__chrome-divider,
.t-admin-tabs[data-style='chrome'] .t-admin-tabs__tab:hover .t-admin-tabs__chrome-divider {
  opacity: 0;
}

/* ─── Button style ──────────────────────────────────────────────────
   Mirrors soybean `button-tab.vue`: 4px-rounded rectangle with 1px
   border; active uses primary@10% bg + primary text + primary@30%
   border (NOT pill-shape with white text). */
.t-admin-tabs[data-style='button'] .t-admin-tabs__draggable {
  gap: 12px;
}
.t-admin-tabs[data-style='button'] .t-admin-tabs__tab {
  padding: 4px 12px;
  gap: 12px;
  border-radius: var(--tnzi-admin-radius-sm, 4px);
  border: 1px solid var(--tnzi-border, #e5e7eb);
  background-color: transparent;
}
.t-admin-tabs[data-style='button'] .t-admin-tabs__tab:hover {
  color: var(--tnzi-primary, #646cff);
  border-color: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.30);
}
.t-admin-tabs[data-style='button'] .t-admin-tabs__tab--active {
  color: var(--tnzi-primary, #646cff);
  background-color: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.10);
  border-color: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.30);
}
.t-admin-tabs[data-style='button'] .t-admin-tabs__close:hover {
  color: #fff;
  background-color: var(--tnzi-primary, #646cff);
}

/* ─── Slider style ──────────────────────────────────────────────────
   Mirrors soybean `slider-tab.vue`: full-bar-height transparent chip;
   active gets primary@10% bg + primary text + 2px primary bottom
   border (which doubles as the active accent). No close button. */
.t-admin-tabs[data-style='slider'] .t-admin-tabs__draggable {
  gap: 6px;
  height: 100%;
}
.t-admin-tabs[data-style='slider'] .t-admin-tabs__tab {
  height: 100%;
  padding: 4px 12px;
  gap: 6px;
  background-color: transparent;
  border-bottom: 2px solid transparent;
}
.t-admin-tabs[data-style='slider'] .t-admin-tabs__tab:hover {
  color: var(--tnzi-primary, #646cff);
}
.t-admin-tabs[data-style='slider'] .t-admin-tabs__tab--active {
  color: var(--tnzi-primary, #646cff);
  background-color: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.10);
  border-bottom-color: var(--tnzi-primary, #646cff);
}

/* Close button — circular 16x16 SVG-rendered close. soybean's
   `.svg-close` doesn't set its own colour either — it inherits from
   the tab outer (`currentColor` in the SVG path).
   On touch devices the hit area grows to 24×24 (still rendering the
   16×16 SVG) so a thumb can reliably hit the target without enlarging
   the visual chrome on desktop. */
.t-admin-tabs__close {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  padding: 0;
  border: none;
  border-radius: 50%;
  background-color: transparent;
  cursor: pointer;
  transition: background-color 0.15s ease, color 0.15s ease;
}
@media (pointer: coarse) {
  .t-admin-tabs__close {
    width: 24px;
    height: 24px;
  }
}
.t-admin-tabs__close:hover {
  color: #fff;
  background-color: #9ca3af;
}
.t-admin-tabs__tab--active .t-admin-tabs__close:hover {
  background-color: var(--tnzi-primary, #646cff);
}

.t-admin-tabs__title {
  /* Title text — colour is set per-style above. Cap at 220px on
     desktop / 140px on phones so an over-long route name (e.g.
     "NotificationTemplate 模板管理详情") doesn't single-handedly fill
     the tab bar. The title still grows up to the cap so short labels
     render naturally. The full string is exposed via the `title`
     attribute for a native tooltip. */
  display: inline-block;
  max-width: 220px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  vertical-align: middle;
}
@media (max-width: 640px) {
  .t-admin-tabs__title {
    max-width: 140px;
  }
}

.t-admin-tabs__actions {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  padding: 0 4px;
}
.t-admin-tabs__reload {
  width: 28px;
  height: 28px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-radius: var(--tnzi-admin-radius-sm, 4px);
  background-color: transparent;
  color: var(--tnzi-base-text-muted);
  cursor: pointer;
  transition: background-color 0.15s ease, color 0.15s ease;
  font-size: 16px;
}
.t-admin-tabs__reload:hover {
  background-color: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.06);
  color: var(--tnzi-primary, #646cff);
}
/* Phase H3 D4: generic action button (fullscreen) — same chrome as
   reload but no spin animation. */
.t-admin-tabs__action {
  width: 28px;
  height: 28px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-radius: var(--tnzi-admin-radius-sm, 4px);
  background-color: transparent;
  color: var(--tnzi-base-text-muted);
  cursor: pointer;
  transition: background-color 0.15s ease, color 0.15s ease;
  font-size: 16px;
  margin-left: 2px;
}
.t-admin-tabs__action:hover {
  background-color: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.06);
  color: var(--tnzi-primary, #646cff);
}
/* Phase H2 D5: spin while reloadFlag is false (soybean's
   animate-spin animate-duration-750). */
.t-admin-tabs__reload--loading {
  animation: t-admin-tabs-reload-spin 0.75s linear infinite;
}
@keyframes t-admin-tabs-reload-spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
</style>
