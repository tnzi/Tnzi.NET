<script setup lang="ts">
/**
 * `TAdminMixRail` — direct port of soybean-admin-example's
 * `first-level-menu.vue` (vertical-mix sidebar rail). Unlike the rest
 * of our sidebar which wraps NMenu, this is a custom div-based list
 * because the rail's "icon on top + label below" layout doesn't fit
 * NMenu's row-based item structure cleanly — soybean wrote it as a
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
 *     custom thin-scrollbar styling — same end result).
 *   - Brand logo slot at the top + MenuToggler at the bottom.
 *
 * What we adapt:
 *   - UnoCSS atomic classes are inlined into the scoped <style> block
 *     because ui-admin doesn't enable UnoCSS itself.
 *   - Icon is rendered via TSvgIcon (our TSvgIcon takes an `icon` string).
 */
import { computed, h } from 'vue'
import type { AdminMenuItem } from '../../stores/useAdminRouteStore'
import TSvgIcon from '../display/TSvgIcon.vue'
import TMenuToggler from '../utility/TMenuToggler.vue'
import { translatePageKey } from '../../pages/_shared/translate'

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
   * Mini mode — when the rail collapses, hide the label and only show
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
    <!-- Top slot — soybean places GlobalLogo (icon-only in mix mode) here. -->
    <div v-if="$slots.header" class="t-admin-mix-rail__header">
      <slot name="header" />
    </div>

    <div class="t-admin-mix-rail__list">
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

    <!-- Bottom slot — soybean places MenuToggler here. Phase H2 C6:
         provide a built-in TMenuToggler so the rail can switch to
         mini mode out of the box; consumer can override via slot. -->
    <div class="t-admin-mix-rail__footer">
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
  background: var(--tnzi-admin-sider-inverted-bg, rgb(0, 20, 40));
  border-right-color: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.12));
}

.t-admin-mix-rail__header {
  flex-shrink: 0;
  height: var(--tnzi-admin-header-height, 56px);
  display: flex;
  align-items: center;
  justify-content: center;
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
  margin: 0 4px 6px;
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

/* Inverted variant: white-on-dark text + primary-fill on active. */
.t-admin-mix-rail--inverted .t-admin-mix-rail__item {
  color: rgba(255, 255, 255, 0.65);
}
.t-admin-mix-rail--inverted .t-admin-mix-rail__item:hover {
  color: #ffffff;
  background-color: rgb(255 255 255 / 0.08);
}
.t-admin-mix-rail--inverted .t-admin-mix-rail__item--active {
  color: #ffffff !important;
  background-color: var(--tnzi-primary, #646cff) !important;
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
  /* Collapse to zero width too — `visibility: hidden + height: 0` still
     reserves the label's intrinsic text width and can push the rail
     past its inline width unless we also zero the horizontal axis. */
  width: 0;
  padding-top: 0;
  visibility: hidden;
}

.t-admin-mix-rail__footer {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  height: 40px;
  border-top: 1px solid var(--tnzi-border, #e5e7eb);
}
.t-admin-mix-rail--inverted .t-admin-mix-rail__footer {
  border-top-color: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.12));
}
</style>
