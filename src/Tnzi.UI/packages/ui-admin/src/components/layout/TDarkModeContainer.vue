<script setup lang="ts">
/**
 * TDarkModeContainer — local theme-invert wrapper.
 *
 * When `inverted` is true, the slot subtree consumes the inverted token
 * surface (dark background + bright foreground) regardless of the global
 * light/dark mode. Useful for designs where the sider stays dark while the
 * rest of the page is light (or vice-versa) — e.g. soybean-admin's hybrid
 * layouts and most dashboards with a deep brand-colored side rail.
 *
 * Inspired by soybean-admin's `DarkModeContainer` (src/components/common/).
 */
interface Props {
  /** Force the subtree into dark-surface mode. Default: false. */
  inverted?: boolean
  /** Use a fully-saturated brand surface (primary 700) instead of neutral dark. */
  brand?: boolean
  /** Root element tag. Defaults to `'div'`. */
  tag?: string
  /**
   * Transition mode between light <-> dark surfaces. (Phase I.6.7)
   *   - `'smooth'` — 300ms color + background-color crossfade (default)
   *   - `'none'`   — no transition (preferred for full-page hard-flips)
   */
  transition?: 'smooth' | 'none'
}

withDefaults(defineProps<Props>(), {
  inverted: false,
  brand: false,
  tag: 'div',
  transition: 'smooth',
})
</script>

<template>
  <component
    :is="tag"
    class="t-dark-mode-container"
    :class="{
      't-dark-mode-container--inverted': inverted,
      't-dark-mode-container--brand': brand,
      't-dark-mode-container--transition': transition === 'smooth',
    }"
  >
    <slot />
  </component>
</template>

<style scoped>
.t-dark-mode-container {
  /* No-op when neither modifier is active — token cascade is unchanged. */
  color: var(--tnzi-base-text);
  background-color: transparent;
}

/* 300ms crossfade between light and dark surfaces. Scoped to this
   component so global dark-mode flips elsewhere stay snap (avoids
   theme-pop artifacts on heavy pages). */
.t-dark-mode-container--transition {
  transition:
    background-color var(--tnzi-admin-motion-duration-slow, 0.3s)
      var(--tnzi-admin-motion-ease-in-out, ease),
    color var(--tnzi-admin-motion-duration-slow, 0.3s)
      var(--tnzi-admin-motion-ease-in-out, ease);
}
.t-dark-mode-container--transition :deep(*) {
  transition:
    background-color var(--tnzi-admin-motion-duration-base, 0.25s)
      var(--tnzi-admin-motion-ease-in-out, ease),
    color var(--tnzi-admin-motion-duration-base, 0.25s)
      var(--tnzi-admin-motion-ease-in-out, ease),
    border-color var(--tnzi-admin-motion-duration-base, 0.25s)
      var(--tnzi-admin-motion-ease-in-out, ease);
}

.t-dark-mode-container--inverted {
  /* Surface */
  background-color: var(--tnzi-admin-sider-inverted-bg, rgb(0 20 40));

  /* Override base text + border tokens for child components so they
     re-render with the inverted palette automatically. */
  --tnzi-base-text: var(--tnzi-admin-inverted-text, rgba(255, 255, 255, 0.92));
  --tnzi-base-text-muted: var(--tnzi-admin-inverted-text-muted, rgba(255, 255, 255, 0.6));
  --tnzi-border: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.12));
  --tnzi-divider: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.08));
  --tnzi-container-bg: var(--tnzi-admin-sider-inverted-bg, rgb(0 20 40));
  --tnzi-admin-menu-item-hover-bg: var(--tnzi-admin-inverted-hover-bg, rgba(255, 255, 255, 0.08));
  --tnzi-admin-menu-item-active-bg: var(--tnzi-admin-inverted-active-bg, rgb(var(--tnzi-primary-rgb) / 0.2));

  color: var(--tnzi-base-text);
}

.t-dark-mode-container--brand {
  background-color: var(--tnzi-primary-700, #3b3fc1);
  --tnzi-base-text: rgba(255, 255, 255, 0.96);
  --tnzi-base-text-muted: rgba(255, 255, 255, 0.72);
  --tnzi-border: rgba(255, 255, 255, 0.16);
  --tnzi-divider: rgba(255, 255, 255, 0.1);
  --tnzi-container-bg: var(--tnzi-primary-700, #3b3fc1);
  --tnzi-admin-menu-item-hover-bg: rgba(255, 255, 255, 0.1);
  --tnzi-admin-menu-item-active-bg: rgba(255, 255, 255, 0.16);

  color: var(--tnzi-base-text);
}
</style>
