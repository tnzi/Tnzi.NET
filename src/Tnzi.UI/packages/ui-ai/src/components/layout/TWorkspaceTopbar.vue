<script setup lang="ts">
/**
 * @experimental
 * TWorkspaceTopbar - Manus-style transparent chat top bar.
 *
 * 3-segment layout: workspace switcher on the left, a flex spacer in
 * the middle, and a right-side action cluster. All three regions are
 * slot-driven so consumers can freely customize.
 *
 * Provides the standard `workspace-switcher` button shape via the
 * `title` + `onSwitch` shortcut for the common case ("Workspace name ▾").
 * Pass `#left` to fully replace it. Use `#right` for the action cluster
 * (Share / Publish / etc).
 *
 * The component is purely presentational - keeps its own bg transparent
 * so it inherits the canvas color and reads as part of the main column.
 */
import { Icon } from '@iconify/vue'

withDefaults(
  defineProps<{
    /** Workspace title shown in the default left button. */
    title?: string
    /** Show a chevron after the title (suggests the button opens a menu). */
    showChevron?: boolean
  }>(),
  {
    title: '',
    showChevron: true,
  },
)

defineEmits<{
  switch: []
}>()
</script>

<template>
  <header class="t-workspace-topbar">
    <slot name="left">
      <button
        v-if="title"
        type="button"
        class="t-workspace-topbar__switcher"
        @click="$emit('switch')"
      >
        <span>{{ title }}</span>
        <Icon v-if="showChevron" icon="lucide:chevron-down" />
      </button>
    </slot>

    <div class="t-workspace-topbar__spacer" />

    <div class="t-workspace-topbar__right">
      <slot name="right" />
    </div>
  </header>
</template>

<style scoped>
.t-workspace-topbar {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 48px;
  padding: 8px 16px;
  background: transparent;
  flex-shrink: 0;
}
.t-workspace-topbar__switcher {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  height: 32px;
  padding: 0 10px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text, #1a1a1a);
  border-radius: 8px;
  font-family: inherit;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
}
.t-workspace-topbar__switcher:hover {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
}
.t-workspace-topbar__switcher .iconify {
  font-size: 14px;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
}
.t-workspace-topbar__spacer {
  flex: 1;
}
.t-workspace-topbar__right {
  display: flex;
  align-items: center;
  gap: 4px;
}
</style>
