<template>
  <footer
    class="t-admin-footer"
    :class="{
      't-admin-footer--fixed': fixed,
      't-admin-footer--inverted': surface === 'dark',
      't-admin-footer--surface-light': surface === 'light',
    }"
  >
    <div v-if="copyright" class="t-admin-footer__copyright">{{ copyright }}</div>
    <ul v-if="links && links.length > 0" class="t-admin-footer__links">
      <li v-for="link in links" :key="link.href">
        <a
          :href="link.href"
          :target="link.external ? '_blank' : undefined"
          :rel="link.external ? 'noopener noreferrer' : undefined"
        >{{ link.label }}</a>
      </li>
    </ul>
    <div v-if="$slots.default" class="t-admin-footer__extra">
      <slot />
    </div>
  </footer>
</template>

<script setup lang="ts">
export interface TAdminFooterLink {
  label: string
  href: string
  external?: boolean
}

interface Props {
  copyright?: string
  links?: TAdminFooterLink[]
  /** Pin the footer to the bottom of the viewport (sticky). */
  fixed?: boolean
  /** Surface tone when the footer carries a custom background color.
   *  `'dark'` → light foreground, `'light'` → dark foreground (dark-mode only). */
  surface?: 'dark' | 'light'
}

withDefaults(defineProps<Props>(), { fixed: false, surface: undefined })
</script>

<style scoped>
.t-admin-footer {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  /* Phase H1 D1: flex-shrink:0 so the column-flex parent doesn't
     squash the footer when content is empty. */
  flex-shrink: 0;
  gap: 12px;
  /* Compact: a single copyright/links line - min-height centers it, no
     vertical padding so the footer doesn't eat content height. Horizontal
     padding matches the 12px content gutter. */
  min-height: var(--tnzi-admin-footer-height, 32px);
  padding: 0 12px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  background-color: var(--tnzi-admin-footer-bg, transparent);
  border-top: 1px solid var(--tnzi-border);
}
.t-admin-footer--fixed {
  position: sticky;
  bottom: 0;
  /* Clear the iOS home indicator when pinned to the viewport bottom. */
  padding-bottom: env(safe-area-inset-bottom);
}
/* Adaptive surface - a custom footer background flips its foreground token
   set (copyright + links) so the footer stays legible. */
.t-admin-footer--inverted {
  --tnzi-base-text: var(--tnzi-admin-footer-fg, var(--tnzi-admin-inverted-text, rgba(255, 255, 255, 0.92)));
  --tnzi-base-text-muted: var(--tnzi-admin-footer-fg, var(--tnzi-admin-inverted-text-muted, rgba(255, 255, 255, 0.6)));
  --tnzi-border: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.12));
  color: var(--tnzi-base-text-muted);
  border-top-color: var(--tnzi-admin-inverted-border, rgba(255, 255, 255, 0.12));
}
.t-admin-footer--surface-light {
  --tnzi-base-text: var(--tnzi-admin-footer-fg, var(--tnzi-admin-surface-light-text, rgba(0, 0, 0, 0.88)));
  --tnzi-base-text-muted: var(--tnzi-admin-footer-fg, var(--tnzi-admin-surface-light-text-muted, rgba(0, 0, 0, 0.5)));
  --tnzi-border: var(--tnzi-admin-surface-light-border, rgba(0, 0, 0, 0.1));
  color: var(--tnzi-base-text-muted);
  border-top-color: var(--tnzi-admin-surface-light-border, rgba(0, 0, 0, 0.1));
}
.t-admin-footer__links {
  display: flex;
  gap: 12px;
  list-style: none;
  margin: 0;
  padding: 0;
}
.t-admin-footer__links a {
  color: var(--tnzi-base-text-muted);
  text-decoration: none;
  transition: color var(--tnzi-admin-motion-duration-fast, 0.15s) ease;
}
.t-admin-footer__links a:hover {
  color: var(--tnzi-primary);
}
.t-admin-footer__extra {
  display: flex;
  align-items: center;
  gap: 12px;
}
</style>
