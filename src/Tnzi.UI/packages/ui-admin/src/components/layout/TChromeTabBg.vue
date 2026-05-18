<script setup lang="ts">
/**
 * `TChromeTabBg` — SVG arc background for the chrome-style tab.
 * Ported from soybean-admin's `chrome-tab-bg.vue` (the SVG path that
 * gives chrome tabs their characteristic curved shoulders).
 *
 * Usage: stack it absolutely at z-index -1 inside a chrome tab so the
 * filled fill colour comes from the `currentColor` of the parent tab.
 *
 * Reference: D:\Github\soybean-admin-main\packages\materials\src\libs\
 *   page-tab\chrome-tab-bg.vue
 */
defineOptions({ name: 'TChromeTabBg' })
</script>

<template>
  <svg class="t-chrome-tab-bg__svg">
    <defs>
      <symbol id="t-chrome-tab-geometry-left" viewBox="0 0 214 36">
        <path d="M17 0h197v36H0v-2c4.5 0 9-3.5 9-8V8c0-4.5 3.5-8 8-8z" />
      </symbol>
      <symbol id="t-chrome-tab-geometry-right" viewBox="0 0 214 36">
        <use xlink:href="#t-chrome-tab-geometry-left" />
      </symbol>
      <clipPath>
        <rect width="100%" height="100%" x="0" />
      </clipPath>
    </defs>
    <svg width="51%" height="100%">
      <use
        xlink:href="#t-chrome-tab-geometry-left"
        width="214"
        height="36"
        fill="currentColor"
      />
    </svg>
    <g transform="scale(-1, 1)">
      <svg width="51%" height="100%" x="-100%" y="0">
        <use
          xlink:href="#t-chrome-tab-geometry-right"
          width="214"
          height="36"
          fill="currentColor"
        />
      </svg>
    </g>
  </svg>
</template>

<style scoped>
.t-chrome-tab-bg__svg {
  width: 100%;
  height: 100%;
}
</style>
