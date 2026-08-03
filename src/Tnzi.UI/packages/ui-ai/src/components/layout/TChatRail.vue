<script setup lang="ts">
/**
 * @experimental
 * TChatRail - the collapsed (icon-only) state of a chat sidebar.
 *
 * The 56px rail is its own layout problem, not a narrow copy of the expanded
 * sidebar: labels are gone, so group headings become rules, the brand mark
 * doubles as the expand affordance, and the account block collapses to a bare
 * avatar. `TCollapsibleSidebar` owns the width animation and mode state; this
 * component owns what goes inside at that width.
 *
 * Mirrors how `@tnzi/ui-admin` treats the same problem - its rail is a
 * dedicated `TAdminMixNavRail`, not an inline branch of the shell.
 *
 * Every nav group's items are rendered, not just the primary ones: collapsing
 * the sidebar must not put a destination out of reach. Groups are separated by
 * a rule since their headings cannot fit.
 */
import { Icon } from '@iconify/vue'
import type { NavItem, NavGroup } from './TSidebarNav.vue'

withDefaults(
  defineProps<{
    groups?: ReadonlyArray<NavGroup>
    /** `'monogram'` renders the built-in T-mark; any other string is an
     *  iconify name. Replace wholesale via the `brand` slot. */
    brandLogo?: 'monogram' | string
    expandLabel?: string
  }>(),
  {
    groups: () => [],
    brandLogo: 'monogram',
    expandLabel: 'Expand sidebar',
  },
)

const emit = defineEmits<{
  expand: []
  select: [item: NavItem, group: NavGroup]
}>()
</script>

<template>
  <div class="t-chat-rail">
    <button
      type="button"
      class="t-chat-rail__brand"
      :aria-label="expandLabel"
      @click="emit('expand')"
    >
      <slot name="brand">
        <span class="t-chat-rail__brand-logo" aria-hidden="true">
          <svg
            v-if="brandLogo === 'monogram'"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2.5"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <path d="M5 6 L19 6" />
            <path d="M12 6 L12 19" />
            <circle cx="12" cy="19" r="1.4" fill="currentColor" stroke="none" />
          </svg>
          <Icon v-else :icon="brandLogo" />
        </span>
      </slot>
      <Icon icon="lucide:panel-left-open" class="t-chat-rail__brand-expand" />
    </button>

    <div class="t-chat-rail__group t-chat-rail__group--top">
      <slot name="top" />
      <template v-for="(group, groupIndex) in groups" :key="group.id">
        <div v-if="groupIndex > 0" class="t-chat-rail__sep" />
        <button
          v-for="item in group.items"
          :key="item.id"
          type="button"
          class="t-chat-rail__btn"
          :class="{ 'is-active': item.active }"
          :aria-label="item.label"
          @click="emit('select', item, group)"
        >
          <Icon v-if="item.icon" :icon="item.icon" />
        </button>
      </template>
    </div>

    <div class="t-chat-rail__group t-chat-rail__group--bottom">
      <slot name="bottom" />
    </div>
  </div>
</template>

<style scoped>
.t-chat-rail {
  display: flex;
  flex-direction: column;
  align-items: center;
  height: 100%;
  padding: 8px 0;
  gap: 4px;
}
.t-chat-rail__brand {
  position: relative;
  width: 40px;
  height: 40px;
  border: none;
  background: transparent;
  border-radius: 10px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  margin-bottom: 4px;
}
.t-chat-rail__brand-logo {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border-radius: 10px;
  background: var(--tnzi-ai-brand-mark-bg);
  color: var(--tnzi-ai-brand-mark-fg);
  font-size: 18px;
}
.t-chat-rail__brand-logo svg {
  width: 18px;
  height: 18px;
}
/* The expand chevron replaces the mark on hover: at 40px there is no room to
   show both, and the mark is the more useful resting state. */
.t-chat-rail__brand-expand {
  position: absolute;
  inset: 0;
  margin: auto;
  width: 18px;
  height: 18px;
  font-size: 18px;
  color: var(--tnzi-ai-text-secondary);
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-rail__brand:hover .t-chat-rail__brand-logo { opacity: 0; }
.t-chat-rail__brand:hover .t-chat-rail__brand-expand { opacity: 1; }

.t-chat-rail__group {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  width: 100%;
}
.t-chat-rail__group--top { margin-top: 4px; }
.t-chat-rail__group--bottom {
  margin-top: auto;
  padding-top: 8px;
}
.t-chat-rail__btn {
  width: 40px;
  height: 40px;
  border: none;
  background: transparent;
  border-radius: 10px;
  color: var(--tnzi-ai-text-secondary);
  font-size: 19px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  flex-shrink: 0;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-rail__btn:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-chat-rail__btn.is-active {
  background: var(--tnzi-ai-accent-soft);
  color: var(--tnzi-ai-accent);
}
/* Stands in for the group headings that cannot fit at 56px. */
.t-chat-rail__sep {
  width: 24px;
  height: 1px;
  margin: 6px auto;
  background: var(--tnzi-ai-divider);
}
</style>
