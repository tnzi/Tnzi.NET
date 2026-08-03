<script setup lang="ts">
/**
 * `TAdminMixNavRail` - direct port of soybean-admin-example's
 * `first-level-menu.vue` (vertical-mix sidebar rail). Unlike the rest
 * of our sidebar which wraps NMenu, this is a custom div-based list
 * because the rail's "icon on top + label below" layout doesn't fit
 * NMenu's row-based item structure cleanly - soybean wrote it as a
 * lightweight reusable template too.
 *
 * Reference: D:\Github\soybean-admin-example\src\layouts\modules\
 *   global-menu\components\first-level-menu.vue
 *
 * What we faithfully port:
 *   - The MixMenuItem div geometry: `mx-4px mb-6px flex-col-center
 *     rounded-8px px-4px py-8px` with `text-icon-large` icon stacked
 *     above a 12px label that uses `h-20px pt-4px ellipsis`.
 *   - Hover/active/inverted colour logic from `selectedBgColor` +
 *     atomic class bindings.
 *   - SimpleScrollbar wrapping the list (we use native overflow with
 *     custom thin-scrollbar styling - same end result).
 *   - Brand logo slot at the top + MenuToggler at the bottom.
 *
 * What we adapt:
 *   - UnoCSS atomic classes are inlined into the scoped <style> block
 *     because ui-admin doesn't enable UnoCSS itself.
 *   - Icon is rendered via TSvgIcon (our TSvgIcon takes an `icon` string).
 */
import { computed, h, ref, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import type { AdminMenuItem } from '../../stores/useAdminRouteStore'
import { TSvgIcon } from '@tnzi/ui'
import { TMenuToggler } from '@tnzi/ui'
import { translatePageKey } from '../../i18n/translate'

function resolveLabel(label: string): string {
  if (!label) return ''
  if (label.startsWith('admin.') || label.startsWith('tnzi.')) {
    return translatePageKey('', label)
  }
  return label
}

interface Props {
  /** Top-level menu items (the rail only renders 1st level). */
  menus: AdminMenuItem[]
  /** Key of the currently-active 1st level item. */
  activeMenuKey?: string
  /** Inverted (dark) palette toggle. Mirrors soybean's `inverted` prop. */
  inverted?: boolean
  /**
   * Mini mode - when the rail collapses, hide the label and only show
   * icons (height: 0). Soybean wires this to `appStore.siderCollapse`.
   */
  isMini?: boolean
  /** Iconify primary theme colour, used to derive the active bg tint. */
  themeColor?: string
  /** Optional collapse handler. When provided, the rail renders a
      MenuToggler in the footer and calls this on toggle. */
  onToggleCollapse?: (collapsed: boolean) => void
}

const props = withDefaults(defineProps<Props>(), {
  activeMenuKey: '',
  inverted: false,
  isMini: false,
  themeColor: '#646cff',
  onToggleCollapse: undefined,
})

const emit = defineEmits<{
  select: [menuKey: string]
}>()

/** Derived active-tile background colour. soybean uses
 *  `transformColorWithOpacity(themeColor, 0.1, '#ffffff')` for light mode
 *  and `0.3 on '#000000'` for dark. We use the same intent via CSS
 *  `color-mix` (opaque mix) so the colour doesn't shift with bg. */
const selectedBgColor = computed(() => {
  const base = props.themeColor || '#646cff'
  return props.inverted
    ? `color-mix(in srgb, ${base} 30%, #000000 70%)`
    : `color-mix(in srgb, ${base} 10%, #ffffff 90%)`
})

function handleClick(key: string): void {
  emit('select', key)
}

/** Render the icon. Iconify-string -> TSvgIcon; vnode -> as is.
 *  Soybean parity (uno.config.ts):
 *    expanded → text-icon-large = 24px
 *    collapsed → text-icon-small = 16px */
function renderIcon(icon: string | undefined): ReturnType<typeof h> | null {
  if (!icon) return null
  return h(TSvgIcon, { icon, size: props.isMini ? 16 : 24 })
}

// ── Scroll-aware edge shadows (mirrors TAdminSidebar) - header/footer only
// cast their elevation shadow while the rail list is actually scrolled. ──
const listRef = ref<HTMLElement | null>(null)
const canScrollUp = ref(false)
const canScrollDown = ref(false)
function updateScrollShadows(): void {
  const el = listRef.value
  if (!el) return
  canScrollUp.value = el.scrollTop > 1
  canScrollDown.value = el.scrollTop + el.clientHeight < el.scrollHeight - 1
}
let resizeObserver: ResizeObserver | null = null
onMounted(() => {
  void nextTick(updateScrollShadows)
  const el = listRef.value
  if (typeof ResizeObserver !== 'undefined' && el) {
    resizeObserver = new ResizeObserver(() => updateScrollShadows())
    resizeObserver.observe(el)
  }
})
onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  resizeObserver = null
})
watch(() => props.isMini, () => void nextTick(updateScrollShadows))
</script>

<template>
  <div
    class="t-admin-mix-rail"
    :class="{
      't-admin-mix-rail--inverted': inverted,
      't-admin-mix-rail--mini': isMini,
    }"
    :style="{ '--t-mix-active-bg': selectedBgColor } as Record<string, string>"
  >
    <!-- Top slot - soybean places GlobalLogo (icon-only in mix mode) here. -->
    <div
      v-if="$slots.header"
      class="t-admin-mix-rail__header"
      :class="{ 't-admin-mix-rail__header--elevated': canScrollUp }"
    >
      <slot name="header" />
    </div>

    <div ref="listRef" class="t-admin-mix-rail__list" @scroll.passive="updateScrollShadows">
      <div
        v-for="menu in menus"
        :key="menu.key"
        class="t-admin-mix-rail__item"
        :class="{ 't-admin-mix-rail__item--active': menu.key === activeMenuKey }"
        :title="resolveLabel(menu.label)"
        @click="handleClick(menu.key)"
      >
        <component :is="renderIcon(menu.icon)" v-if="menu.icon" class="t-admin-mix-rail__icon" />
        <p class="t-admin-mix-rail__label">{{ resolveLabel(menu.label) }}</p>
      </div>
    </div>

    <!-- Bottom slot - soybean places MenuToggler here. Phase H2 C6:
         provide a built-in TMenuToggler so the rail can switch to
         mini mode out of the box; consumer can override via slot. -->
    <div
      class="t-admin-mix-rail__footer"
      :class="{ 't-admin-mix-rail__footer--elevated': canScrollDown }"
    >
      <slot name="footer">
        <TMenuToggler
          v-if="onToggleCollapse"
          :collapsed="isMini"
          @toggle="(v) => onToggleCollapse?.(v)"
        />
      </slot>
    </div>
  </div>
</template>

<style scoped>
.t-admin-mix-rail {
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  background: var(--tnzi-admin-sider-bg, var(--tnzi-container-bg, #ffffff));
  border-right: 1px solid var(--tnzi-border, #e5e7eb);
  overflow: hidden;
}
.t-admin-mix-rail--inverted {
  /* Prefer the user's custom sider color when set; else the built-in dark. */
  background: var(--tnzi-admin-sider-bg, var(--tnzi-admin-sider-inverted-bg, rgb(0, 20, 40)));
  border-right-color: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.12));
  --tnzi-admin-sider-edge-shadow: rgba(0, 0, 0, 0.3);
  /* Flip base text to the light inverted tint so descendants reading it -
     notably the footer (TSidebarSettingsFooter, a child that inherits this
     custom property across the component boundary) - stay legible instead of
     rendering the default dark text (grey) on the dark rail. Mirrors the fix
     on `.t-admin-sidebar--inverted`. */
  --tnzi-base-text: var(--tnzi-admin-sider-fg, var(--tnzi-admin-inverted-text, rgba(255, 255, 255, 0.92)));
}

.t-admin-mix-rail__header {
  flex-shrink: 0;
  height: var(--tnzi-admin-header-height, 56px);
  display: flex;
  align-items: center;
  justify-content: center;
  /* Scroll-aware feathered shadow (matches the main sider) - only while the
     rail list is scrolled beneath the logo bar; no hard border. */
  position: relative;
  z-index: 2;
  transition: box-shadow 0.2s ease;
}
.t-admin-mix-rail__header--elevated {
  box-shadow: 0 6px 8px -6px var(--tnzi-admin-sider-edge-shadow, rgba(0, 0, 0, 0.06));
}

.t-admin-mix-rail__list {
  flex: 1 1 auto;
  overflow-y: auto;
  overflow-x: hidden;
  /* Scrollbar styling delegated to styles/polish.css macOS-style overlay rules. */
}

/* Item: icon stacked on top of a small label. This is soybean's
   `mx-4px mb-6px flex-col-center cursor-pointer rounded-8px
   bg-transparent px-4px py-8px transition-300` inlined. */
.t-admin-mix-rail__item {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  margin: 0 6px 6px;
  padding: 8px 4px;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: transparent;
  color: var(--tnzi-base-text, #1f1f1f);
  cursor: pointer;
  transition:
    background-color 0.18s ease,
    color 0.18s ease;
}
.t-admin-mix-rail__item:hover {
  background-color: rgb(0 0 0 / 0.08);
}
.t-admin-mix-rail__item--active {
  color: var(--tnzi-primary, #646cff);
  background-color: var(--t-mix-active-bg, color-mix(in srgb, var(--tnzi-primary, #646cff) 10%, #ffffff 90%));
}

/* Inverted variant: white-on-dark text + a primary-TINTED pill on active.
   A solid primary fill under white text breaks with light accents (mint /
   frost / amber dark-mode primaries measured 1.6-2.6:1) - the 18%-alpha tint
   keeps the accent visible while the near-white label stays readable on any
   dark rail, mirroring the sidebar's inverted active treatment. */
.t-admin-mix-rail--inverted .t-admin-mix-rail__item {
  color: var(--tnzi-admin-sider-fg, rgba(255, 255, 255, 0.65));
}
.t-admin-mix-rail--inverted .t-admin-mix-rail__item:hover {
  color: #ffffff;
  background-color: rgb(255 255 255 / 0.08);
}
.t-admin-mix-rail--inverted .t-admin-mix-rail__item--active {
  color: rgba(255, 255, 255, 0.95) !important;
  background-color: var(--tnzi-admin-inverted-active-bg, rgb(var(--tnzi-primary-rgb) / 0.18)) !important;
}

.t-admin-mix-rail__icon {
  display: inline-flex;
  /* TSvgIcon already takes a size prop so explicit width/height
     aren't strictly needed, but lock these for vertical alignment. */
  flex-shrink: 0;
}

.t-admin-mix-rail__label {
  width: 100%;
  margin: 0;
  padding-top: 4px;
  height: 20px;
  font-size: 12px;
  text-align: center;
  line-height: 16px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  /* Mini mode hides the label (collapsed rail). */
  transition: height 0.3s ease, padding 0.3s ease;
}
.t-admin-mix-rail--mini .t-admin-mix-rail__label {
  height: 0;
  /* Collapse to zero width too - `visibility: hidden + height: 0` still
     reserves the label's intrinsic text width and can push the rail
     past its inline width unless we also zero the horizontal axis. */
  width: 0;
  padding-top: 0;
  visibility: hidden;
}

.t-admin-mix-rail__footer {
  flex-shrink: 0;
  /* Column so the footer can stack the shared Settings actions above the
     collapse toggler (was a single 40px centered row when it only held the
     toggler). Height is intrinsic to its content. */
  display: flex;
  flex-direction: column;
  align-items: stretch;
  /* No hard top border - scroll-aware upward shadow (matches the main sider
     footer) only while there is more rail content below. */
  position: relative;
  z-index: 2;
  transition: box-shadow 0.2s ease;
}
.t-admin-mix-rail__footer--elevated {
  box-shadow: 0 -6px 8px -6px var(--tnzi-admin-sider-edge-shadow, rgba(0, 0, 0, 0.06));
}
</style>
