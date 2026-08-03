<template>
  <!--
    TEmpty - the framework's empty state: a muted icon over a line of text,
    with room for a call to action.

    Moved down from `@tnzi/ui-admin` on 2026-08-02. It had already earned the
    position - 25 call sites there, having itself replaced three divergent
    in-package variants - while the version that used to live here had ZERO
    consumers and drew a tick-in-a-circle, which reads as success rather than
    absence.
  -->
  <div class="t-empty" :class="`t-empty--${size}`" role="status">
    <slot name="icon">
      <TSvgIcon :icon="icon" :size="iconSize" />
    </slot>
    <span class="t-empty__text">
      <slot name="description">{{ label }}</slot>
    </span>
    <div v-if="$slots.default || $slots.action" class="t-empty__action">
      <slot />
      <slot name="action" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import TSvgIcon from '../display/TSvgIcon.vue'

export type TEmptySize = 'small' | 'medium' | 'large'

export interface TEmptyProps {
  /** Already-translated empty text; falls back to 'No data'. */
  text?: string
  /**
   * @deprecated Use `text`. Kept because the earlier version of this component
   * named the prop this way; both resolve to the same line.
   */
  description?: string
  /** Iconify name (default `mdi:inbox-outline`). */
  icon?: string
  /** Visual scale - icon size + vertical padding. */
  size?: TEmptySize
}

const props = withDefaults(defineProps<TEmptyProps>(), {
  text: undefined,
  description: undefined,
  icon: 'mdi:inbox-outline',
  size: 'medium',
})

defineSlots<{
  /** Trailing content, typically a "create" call to action. */
  default?: () => unknown
  /** Same position as the default slot; the older prop name for it. */
  action?: () => unknown
  /** Replace the icon entirely. */
  icon?: () => unknown
  /** Replace the text line entirely. */
  description?: () => unknown
}>()

const label = computed(() => props.text || props.description || 'No data')

const ICON_SIZES: Record<TEmptySize, number> = { small: 28, medium: 40, large: 56 }
const iconSize = computed(() => ICON_SIZES[props.size])
</script>

<style scoped>
.t-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  color: var(--tnzi-base-text-muted, #9ca3af);
}
.t-empty--small { padding: 24px 0; }
.t-empty--medium { padding: 48px 0; }
.t-empty--large { padding: 64px 0; }
.t-empty__text { font-size: 13px; }
.t-empty__action { margin-top: 8px; }
</style>
