<script setup lang="ts">
/**
 * @experimental
 * TToolLinkList - web pages inside a tool step (what a search found, the page a fetch
 * read): one compact row per page on an inset well, its title on the left and its
 * site on the right, opening in a new tab.
 *
 * No favicons: an icon fetched from each listed site would load on its own the moment
 * the step opens, and nothing remote in an answer loads without the user's click (the
 * same rule `useStreamMarkdown` applies to images).
 */
import { Icon } from '@iconify/vue'
import type { ToolLink } from './tool-views'

withDefaults(
  defineProps<{
    links: readonly ToolLink[]
    /** A heading above the list ("Results"). */
    label?: string
    /** Show each page's site on the right (off when the title already names it). */
    showDomain?: boolean
  }>(),
  { label: undefined, showDomain: true },
)
</script>

<template>
  <section class="t-tool-links">
    <div v-if="label" class="t-tool-links__label">{{ label }}</div>
    <ul class="t-tool-links__list t-ai-scroll">
      <li v-for="link in links" :key="link.url">
        <a :href="link.url" target="_blank" rel="noopener noreferrer" class="t-tool-links__row" :title="link.url">
          <Icon icon="lucide:globe" class="t-tool-links__icon" aria-hidden="true" />
          <span class="t-tool-links__title">{{ link.title }}</span>
          <span v-if="showDomain" class="t-tool-links__domain">{{ link.domain }}</span>
          <Icon v-else icon="lucide:arrow-up-right" class="t-tool-links__icon" aria-hidden="true" />
        </a>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.t-tool-links {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}
.t-tool-links__label {
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-ai-text-tertiary);
}
/* Ten rows, then the list scrolls: a search that went several rounds lists dozens. */
.t-tool-links__list {
  display: flex;
  flex-direction: column;
  max-height: 288px;
  overflow-y: auto;
  position: relative;
  margin: 0;
  padding: 4px;
  border-radius: 8px;
  background: var(--tnzi-ai-code-bg);
  list-style: none;
}
.t-tool-links__row {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  height: 28px;
  padding: 0 8px;
  border-radius: 6px;
  color: var(--tnzi-ai-text);
  font-size: 13px;
  text-decoration: none;
  transition: background var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-tool-links__row:hover { background: var(--tnzi-ai-press); }
.t-tool-links__row:focus-visible {
  outline: 2px solid var(--tnzi-ai-accent);
  outline-offset: -2px;
}
.t-tool-links__icon {
  flex-shrink: 0;
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-tool-links__title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-tool-links__domain {
  flex-shrink: 0;
  max-width: 40%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
}
</style>
