<script setup lang="ts">
/**
 * @experimental
 * TPopoverMenu - Floating dropdown menu container.
 *
 * Pure layout primitive for any "click a button to open a small list of
 * actions" pattern. Provides:
 *
 *   - `open` v-model + outside-click auto-close
 *   - Optional header label slot
 *   - Default slot for menu items (use `.t-popover-menu__item` class)
 *   - Anchored positioning relative to the closest positioned ancestor
 *
 * Items are NOT a separate component - consumers compose them with raw
 * buttons styled by the `.t-popover-menu__item` / `__item-toggle` /
 * `__item-check` classes. This keeps the API minimal while letting
 * consumers add icons, badges, dividers, and toggles freely.
 *
 * The component does NOT manage trigger button state - wrap your trigger
 * + this menu in a positioned parent and toggle `open` from the trigger's
 * click handler. See playground `AppShell.vue` for examples.
 */
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'

const props = withDefaults(
  defineProps<{
    /** Whether the menu is currently visible. v-model. */
    modelValue: boolean
    /** Optional header label shown at the top of the menu. */
    label?: string
    /** Min-width of the menu. Default 200px. */
    minWidth?: number
    /** Max-width - wider for menus with longer item text. Default 320px. */
    maxWidth?: number
    /**
     * Anchor side. `'right'` aligns the menu's right edge with the
     * parent's right edge; `'left'` aligns the left edges. Default
     * `'right'` since most action menus open from a right-side trigger.
     */
    align?: 'left' | 'right'
    /**
     * Whether clicking outside closes the menu. Default `true`. Set
     * `false` if the consumer wants to manage close behavior manually
     * (e.g. nested menus that need a click-through pattern).
     */
    closeOnOutsideClick?: boolean
  }>(),
  {
    label: '',
    minWidth: 200,
    maxWidth: 320,
    align: 'right',
    closeOnOutsideClick: true,
  },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
}>()

const root = ref<HTMLDivElement | null>(null)

function onDocumentClick(e: MouseEvent): void {
  if (!props.modelValue) return
  if (!props.closeOnOutsideClick) return
  const target = e.target as Node | null
  if (root.value && target && !root.value.contains(target)) {
    emit('update:modelValue', false)
  }
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      // Defer one tick so the click that OPENED the menu doesn't
      // immediately bubble up and close it on the same event loop.
      setTimeout(() => document.addEventListener('click', onDocumentClick), 0)
    } else {
      document.removeEventListener('click', onDocumentClick)
    }
  },
)

onMounted(() => {
  if (props.modelValue) {
    setTimeout(() => document.addEventListener('click', onDocumentClick), 0)
  }
})

onBeforeUnmount(() => {
  document.removeEventListener('click', onDocumentClick)
})
</script>

<template>
  <div
    v-if="modelValue"
    ref="root"
    class="t-popover-menu"
    :class="`t-popover-menu--${align}`"
    :style="{ minWidth: `${minWidth}px`, maxWidth: `${maxWidth}px` }"
    role="menu"
    @click.stop
  >
    <div v-if="label" class="t-popover-menu__head">{{ label }}</div>
    <slot />
  </div>
</template>

<style scoped>
.t-popover-menu {
  position: absolute;
  top: 100%;
  margin-top: 6px;
  z-index: 20;
  background: var(--tnzi-ai-surface, #ffffff);
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  border-radius: 12px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.08);
  padding: 6px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.t-popover-menu--right {
  right: 0;
}
.t-popover-menu--left {
  left: 0;
}
.t-popover-menu__head {
  padding: 6px 10px 4px;
  font-size: 11px;
  font-weight: 500;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  letter-spacing: 0.02em;
}

/* Item primitives - exposed via :deep so consumers can use them on raw
   button / label elements inside the default slot without needing to
   wrap them in another component. */
.t-popover-menu :deep(.t-popover-menu__item) {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 34px;
  padding: 0 10px;
  border: none;
  background: transparent;
  border-radius: 8px;
  color: var(--tnzi-ai-text, #1a1a1a);
  font-family: inherit;
  font-size: 13px;
  text-align: left;
  cursor: pointer;
  width: 100%;
}
.t-popover-menu :deep(.t-popover-menu__item:hover) {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
}
.t-popover-menu :deep(.t-popover-menu__item .iconify) {
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  font-size: 15px;
  flex-shrink: 0;
}
.t-popover-menu :deep(.t-popover-menu__item-check) {
  color: var(--tnzi-ai-accent, #3b82f6);
  margin-left: auto;
  flex-shrink: 0;
}
.t-popover-menu :deep(.t-popover-menu__sep) {
  height: 1px;
  background: var(--tnzi-ai-divider, #ececec);
  margin: 4px 4px;
}
</style>
