<script setup lang="ts">
/**
 * `TSystemLogo` - brand mark + (optional) caption. Used in admin sidebar,
 * mobile drawer, login page, and footer.
 *
 * Three rendering modes:
 *   - `full` (default): icon + text side by side
 *   - `icon-only`: just the mark (used when sidebar is collapsed)
 *   - `stacked`: icon above text (used in login page brand-head)
 */
import { computed } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import TBrandMark from './TBrandMark.vue'

export type TSystemLogoLayout = 'full' | 'icon-only' | 'stacked'

interface Props {
  /**
   * Iconify name. When `undefined` (default), TSystemLogo renders the
   * built-in {@link TBrandMark} inline SVG - a 3-cube stacked motif
   * designed for ui-admin. Pass a string to override (e.g. consumers
   * configuring a custom brand via `defineAdminApp({ login: { brandIcon } })`).
   */
  icon?: string
  /** Pixel size of the icon. */
  iconSize?: number
  /** Brand title (e.g. "Tnzi Admin"). */
  title?: string
  /** Subtitle / tagline displayed under the title. */
  subtitle?: string
  /** Layout mode. */
  layout?: TSystemLogoLayout
}

const props = withDefaults(defineProps<Props>(), {
  icon: undefined,
  iconSize: 32,
  title: 'Tnzi Admin',
  subtitle: '',
  layout: 'full',
})

const containerClass = computed(() => [
  't-system-logo',
  `t-system-logo--${props.layout}`,
])

// Inline display style so the layout survives the cross-package CSS
// ordering that sometimes resets `.t-system-logo`'s `display: flex`
// scoped rule (observed under unocss + library bundling).
const layoutStyle = computed(() => ({
  display: 'inline-flex',
  alignItems: 'center',
  gap: '10px',
  flexDirection: props.layout === 'stacked' ? ('column' as const) : ('row' as const),
}))
</script>

<template>
  <div
    :class="containerClass"
    :style="layoutStyle"
  >
    <slot name="icon">
      <TBrandMark v-if="!icon" :size="iconSize" :aria-label="title" class="t-system-logo__icon" />
      <TSvgIcon v-else :icon="icon" :size="iconSize" class="t-system-logo__icon" />
    </slot>
    <div v-if="layout !== 'icon-only'" class="t-system-logo__text">
      <span class="t-system-logo__title">{{ title }}</span>
      <span v-if="subtitle" class="t-system-logo__subtitle">{{ subtitle }}</span>
    </div>
  </div>
</template>

<style scoped>
.t-system-logo {
  display: flex;
  align-items: center;
  gap: 10px;
  color: var(--tnzi-primary);
}
.t-system-logo--stacked {
  flex-direction: column;
  align-items: center;
  gap: 12px;
  text-align: center;
}
.t-system-logo--icon-only .t-system-logo__text {
  display: none;
}
.t-system-logo__text {
  display: flex;
  flex-direction: column;
  line-height: 1.2;
}
.t-system-logo__title {
  font-size: 16px;
  font-weight: 700;
  letter-spacing: -0.01em;
  color: var(--tnzi-primary);
  white-space: nowrap;
}
/* Login page stacked layout uses 24px primary (matches soybean login). */
.t-system-logo--stacked .t-system-logo__title {
  font-size: 24px;
}
.t-system-logo__subtitle {
  font-size: 12px;
  font-weight: 400;
  color: var(--tnzi-base-text-muted);
}
</style>
