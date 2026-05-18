<script setup lang="ts">
/**
 * `TLayoutModeCard` — soybean-parity preview card for one of the six
 * admin layout modes. Renders a 96x64 canvas with primary-tinted boxes
 * arranged to mimic each mode's chrome (sider/header/main combinations).
 *
 * Phase G rewrite: dropped the previous `geometry switch + content-line`
 * abstraction in favour of six explicit `v-if` blocks that mirror
 * soybean's `layout-mode.vue:18-60` template-by-template structure. The
 * old abstraction couldn't express which sider was the "primary tint"
 * vs the "drawer tint" vs the "menu rail tint", so all hybrid cards
 * looked identical.
 *
 * Reference: D:\Github\soybean-admin-main\src\layouts\modules\theme-drawer\
 *   modules\layout\modules\layout-mode.vue
 */
import type { AdminLayoutMode } from '../../stores/useAdminThemeStore'

interface Props {
  mode: AdminLayoutMode
  active?: boolean
  label?: string
}

withDefaults(defineProps<Props>(), {
  active: false,
  label: undefined,
})

defineEmits<{ select: [mode: AdminLayoutMode] }>()
</script>

<template>
  <button
    type="button"
    class="t-layout-card"
    :class="{ 't-layout-card--active': active }"
    :aria-pressed="active"
    :aria-label="label ?? mode"
    @click="$emit('select', mode)"
  >
    <!-- Soybean parity: card canvas is fixed 96x64; outer wrapper hosts
         the primary-tinted ring on hover/active via box-shadow rings. -->
    <span class="t-layout-card__canvas" aria-hidden="true">
      <!-- vertical: full-height sider on the left + (header bar + main) stacked on the right -->
      <span v-if="mode === 'vertical'" class="t-layout-card__row">
        <span class="t-layout-card__sider t-layout-card__sider--primary t-layout-card__sider--w18" />
        <span class="t-layout-card__col">
          <span class="t-layout-card__header t-layout-card__header--secondary" />
          <span class="t-layout-card__main" />
        </span>
      </span>

      <!-- vertical-mix: 8px primary rail + 16px tertiary drawer + (header bar + main) -->
      <span v-else-if="mode === 'vertical-mix'" class="t-layout-card__row">
        <span class="t-layout-card__sider t-layout-card__sider--primary t-layout-card__sider--w8" />
        <span class="t-layout-card__sider t-layout-card__sider--tertiary t-layout-card__sider--w16" />
        <span class="t-layout-card__col">
          <span class="t-layout-card__header t-layout-card__header--secondary" />
          <span class="t-layout-card__main" />
        </span>
      </span>

      <!-- vertical-hybrid-header-first: 8px rail + 16px drawer + (primary header + main).
           Distinguished from vertical-mix by the header being primary-tinted. -->
      <span v-else-if="mode === 'vertical-hybrid-header-first'" class="t-layout-card__row">
        <span class="t-layout-card__sider t-layout-card__sider--primary t-layout-card__sider--w8" />
        <span class="t-layout-card__sider t-layout-card__sider--tertiary t-layout-card__sider--w16" />
        <span class="t-layout-card__col">
          <span class="t-layout-card__header t-layout-card__header--primary" />
          <span class="t-layout-card__main" />
        </span>
      </span>

      <!-- horizontal: full-width primary header + main below (no sider) -->
      <span v-else-if="mode === 'horizontal'" class="t-layout-card__col">
        <span class="t-layout-card__header t-layout-card__header--primary" />
        <span class="t-layout-card__row t-layout-card__row--grow">
          <span class="t-layout-card__main" />
        </span>
      </span>

      <!-- top-hybrid-sidebar-first: tertiary header strip + (primary sider + main) -->
      <span v-else-if="mode === 'top-hybrid-sidebar-first'" class="t-layout-card__col">
        <span class="t-layout-card__header t-layout-card__header--tertiary" />
        <span class="t-layout-card__row t-layout-card__row--grow">
          <span class="t-layout-card__sider t-layout-card__sider--primary t-layout-card__sider--w18" />
          <span class="t-layout-card__main" />
        </span>
      </span>

      <!-- top-hybrid-header-first: primary header + (default-tinted sider + main).
           Distinguished from top-hybrid-sidebar-first by sider being plain. -->
      <span v-else-if="mode === 'top-hybrid-header-first'" class="t-layout-card__col">
        <span class="t-layout-card__header t-layout-card__header--primary" />
        <span class="t-layout-card__row t-layout-card__row--grow">
          <span class="t-layout-card__sider t-layout-card__sider--w18" />
          <span class="t-layout-card__main" />
        </span>
      </span>
    </span>
    <span v-if="label" class="t-layout-card__label">{{ label }}</span>
  </button>
</template>

<style scoped>
/* Card outer button — flex column for canvas + label, no border on the
   button itself (soybean uses a transparent ring + box-shadow on the
   inner canvas instead). Width is pinned to the canvas width so that
   even if the parent grid uses an auto-sizing column, the button itself
   doesn't stretch beyond 96px. */
.t-layout-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  width: 96px;
  /* When dropped into an auto-sizing grid cell (`1fr`), centre the
     fixed-96px card horizontally inside the cell so column gaps look
     even rather than left-bunched. */
  justify-self: center;
  padding: 0;
  background: transparent;
  border: none;
  cursor: pointer;
}

/* Canvas is the 96x64 chrome preview. Soybean uses a ring (box-shadow)
   for active/hover state — this avoids reflow on selection change. */
.t-layout-card__canvas {
  display: flex;
  width: 96px;
  height: 64px;
  padding: 6px;
  gap: 6px;
  border-radius: 4px;
  background: var(--tnzi-container-bg, #ffffff);
  box-shadow:
    0 1px 2px 0 rgb(0 0 0 / 0.06),
    0 0 0 2px transparent;
  transition: box-shadow 0.18s ease;
}
.t-layout-card:hover .t-layout-card__canvas {
  box-shadow:
    0 1px 2px 0 rgb(0 0 0 / 0.06),
    0 0 0 2px var(--tnzi-primary, #646cff);
}
.t-layout-card--active .t-layout-card__canvas {
  box-shadow:
    0 1px 2px 0 rgb(0 0 0 / 0.06),
    0 0 0 2px var(--tnzi-primary, #646cff);
}

/* Layout primitives — match soybean's atomic UnoCSS classes:
   `.layout-header { h-16px rd-4px }`, `.layout-sider { bg-primary-300 rd-4px }`,
   `.layout-main { flex-1 bg-primary-200 rd-4px }`. */
.t-layout-card__col {
  display: flex;
  flex-direction: column;
  gap: 6px;
  flex: 1;
  min-width: 0;
}
.t-layout-card__row {
  display: flex;
  gap: 6px;
  width: 100%;
  height: 100%;
}
.t-layout-card__row--grow {
  flex: 1;
  min-height: 0;
}
.t-layout-card__header {
  height: 16px;
  border-radius: 4px;
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.30); /* primary-300 */
}
.t-layout-card__header--primary {
  background: var(--tnzi-primary, #646cff);
}
.t-layout-card__header--secondary {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.20); /* primary-200 */
}
.t-layout-card__header--tertiary {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.30); /* primary-300 */
}
.t-layout-card__sider {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.30); /* primary-300 default */
  border-radius: 4px;
}
.t-layout-card__sider--primary {
  background: var(--tnzi-primary, #646cff);
}
.t-layout-card__sider--tertiary {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.30);
}
.t-layout-card__sider--w8 {
  width: 8px;
}
.t-layout-card__sider--w16 {
  width: 16px;
}
.t-layout-card__sider--w18 {
  width: 18px;
}
.t-layout-card__main {
  flex: 1;
  border-radius: 4px;
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.20); /* primary-200 */
}

.t-layout-card__label {
  width: 100%;
  font-size: 12px;
  text-align: center;
  color: var(--tnzi-base-text, #4b5563);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
