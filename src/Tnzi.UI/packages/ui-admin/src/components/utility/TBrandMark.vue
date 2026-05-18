<script setup lang="ts">
/**
 * `TBrandMark` — the default ui-admin brand mark, an inline SVG of three
 * stacked rounded cubes evoking modularity / composition (a framework
 * built from interlocking modules).
 *
 * `currentColor` is used everywhere so the mark inherits the surrounding
 * text colour — in the sidebar header that's `--tnzi-primary`, in the
 * login card it's whatever the slot caller sets, in dark mode it follows
 * the inverted text colour without any explicit prop.
 *
 * Consumers wanting a custom logo pass `brandIcon` (an iconify name) to
 * `defineAdminApp` / `TAdminShell` — TSystemLogo then routes to TSvgIcon
 * instead of this default mark.
 */
interface Props {
  size?: number | string
  ariaLabel?: string
}

withDefaults(defineProps<Props>(), {
  size: 32,
  ariaLabel: 'Tnzi',
})
</script>

<template>
  <svg
    :width="size"
    :height="size"
    viewBox="0 0 24 24"
    fill="none"
    xmlns="http://www.w3.org/2000/svg"
    role="img"
    :aria-label="ariaLabel"
    class="t-brand-mark"
  >
    <!-- Back-right cube — lightest, slight depth illusion. -->
    <rect x="11.5" y="2.5" width="9.5" height="9.5" rx="2.4" fill="currentColor" opacity="0.32" />
    <!-- Front-left cube — medium opacity. -->
    <rect x="3" y="6.5" width="9.5" height="9.5" rx="2.4" fill="currentColor" opacity="0.6" />
    <!-- Foreground cube — solid primary, centre-aligned visual weight. -->
    <rect x="7.25" y="11" width="10.5" height="10.5" rx="2.6" fill="currentColor" />
    <!-- Tiny highlight on the foreground cube for a subtle 3D feel. -->
    <rect x="9.5" y="13.25" width="2.25" height="0.9" rx="0.45" fill="#ffffff" opacity="0.35" />
  </svg>
</template>

<style scoped>
.t-brand-mark {
  display: inline-block;
  flex-shrink: 0;
  vertical-align: middle;
}
</style>
