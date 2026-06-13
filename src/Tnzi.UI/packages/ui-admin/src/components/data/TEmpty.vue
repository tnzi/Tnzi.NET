<template>
  <!--
    TEmpty — unified empty-state visual (icon + text, muted). Replaces the
    three divergent empty states that grew across the package (Naive's
    default "No Data", TDataCardList's inline icon+text, TCardRenderer's
    own copy). Consumers pass already-translated `text` (typically
    `t('admin.crud.empty')`); without it the component falls back to the
    English 'No data'.
  -->
  <div class="t-empty" :class="`t-empty--${size}`" role="status">
    <TSvgIcon :icon="icon" :size="iconSize" />
    <span class="t-empty__text">{{ text || 'No data' }}</span>
    <slot />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { TSvgIcon } from '@tnzi/ui'

export type TEmptySize = 'small' | 'medium' | 'large'

export interface TEmptyProps {
  /** Already-translated empty text; falls back to 'No data'. */
  text?: string
  /** mdi:* icon (default mdi:inbox-outline). */
  icon?: string
  /** Visual scale — icon size + vertical padding. */
  size?: TEmptySize
}

const props = withDefaults(defineProps<TEmptyProps>(), {
  text: undefined,
  icon: 'mdi:inbox-outline',
  size: 'medium',
})

defineSlots<{
  /** Optional trailing content (e.g. a "create" call-to-action button). */
  default?: () => unknown
}>()

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
</style>
