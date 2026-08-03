<template>
  <n-card
    size="small"
    class="t-stat-card transition-shadow hover:shadow-md"
    :class="`t-stat-card--${size}`"
    :style="{ '--t-stat-border-color': borderColorValue }"
  >
    <template v-if="loading">
      <n-skeleton text :repeat="1" class="w-[60%]" />
      <n-skeleton text class="w-[40%] h-8 mt-2" />
    </template>
    <template v-else>
      <n-statistic :label="title">
        <template #prefix v-if="prefix">
          <span class="text-[0.8em] mr-0.5">{{ prefix }}</span>
        </template>
        <n-number-animation
          v-if="typeof value === 'number'"
          :from="0"
          :to="value"
          :active="true"
        />
        <span v-else>{{ value }}</span>
        <template #suffix v-if="suffix || trend != null">
          <span v-if="suffix" class="text-[0.6em] ml-1 text-naive-secondary">{{ suffix }}</span>
          <span
            v-if="trend != null"
            class="text-[13px] ml-2 font-500"
            :class="{
              'text-success': trend > 0,
              'text-error': trend < 0,
              'text-disabled': trend === 0,
            }"
          >
            <span v-if="trend > 0">&#x25B2;</span>
            <span v-else-if="trend < 0">&#x25BC;</span>
            <span v-else>&#x25C6;</span>
            {{ Math.abs(trend) }}%
          </span>
        </template>
      </n-statistic>
    </template>
  </n-card>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NCard, NStatistic, NSkeleton, NNumberAnimation } from 'naive-ui'
import type { ColorRole } from '../../theme/types'

/**
 * Legacy colour names accepted by the `color` prop before it moved to semantic
 * roles. Kept so `color="blue"` keeps compiling and rendering; it now resolves
 * to a themed token instead of a frozen hex.
 *
 * `purple` had no semantic counterpart and maps to `primary` - the accent the
 * app actually chose.
 */
const LEGACY_COLOR_ROLES = {
  blue: 'info',
  green: 'success',
  orange: 'warning',
  red: 'error',
  purple: 'primary',
} as const satisfies Record<string, ColorRole>

type LegacyColor = keyof typeof LEGACY_COLOR_ROLES

interface Props {
  title: string
  value: number | string
  suffix?: string
  prefix?: string
  trend?: number
  size?: 'small' | 'medium' | 'large'
  /**
   * Accent colour for the left border, as a semantic theme role.
   *
   * This used to be a fixed palette (`blue` / `green` / …) hard-coded to the
   * Naive UI default hexes - four of the five were byte-identical to the theme
   * system's own defaults, so a consumer who changed their palette got a card
   * that silently stayed on the old colours. Roles follow the live theme.
   * The old names still work, mapped onto the nearest role.
   */
  color?: ColorRole | LegacyColor
  loading?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  suffix: undefined,
  prefix: undefined,
  trend: undefined,
  size: 'medium',
  color: 'info',
  loading: false,
})

const resolvedRole = computed<ColorRole>(
  () => LEGACY_COLOR_ROLES[props.color as LegacyColor] ?? (props.color as ColorRole),
)

/**
 * Reads the palette off CSS custom properties rather than `usePalette()` so the
 * card still renders standalone (no theme context injected) - it just inherits
 * whatever `:root` provides, with the token's own fallback.
 */
const borderColorValue = computed(() => `var(--tnzi-${resolvedRole.value}-500)`)
</script>

<style scoped>
/* CSS variable border - cannot be expressed as utility.
   The fallback is the info token, not a literal hex: if the theme vars are not
   injected yet the border should still track the theme once they land. */
.t-stat-card {
  border-left: 3px solid var(--t-stat-border-color, var(--tnzi-info-500));
}

/* :deep() for Naive UI internal statistic value sizing */
.t-stat-card--small :deep(.n-statistic .n-statistic-value) {
  font-size: 20px;
}

.t-stat-card--medium :deep(.n-statistic .n-statistic-value) {
  font-size: 28px;
}

.t-stat-card--large :deep(.n-statistic .n-statistic-value) {
  font-size: 36px;
}
</style>
