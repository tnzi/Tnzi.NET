<template>
  <footer class="t-admin-footer" :class="{ 't-admin-footer--fixed': fixed }">
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
}

withDefaults(defineProps<Props>(), { fixed: false })
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
  min-height: var(--tnzi-admin-footer-height, 48px);
  padding: 12px 16px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  background-color: var(--tnzi-admin-footer-bg, transparent);
  border-top: 1px solid var(--tnzi-border);
}
.t-admin-footer--fixed {
  position: sticky;
  bottom: 0;
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
