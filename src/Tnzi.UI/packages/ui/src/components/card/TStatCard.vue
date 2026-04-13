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

interface Props {
  title: string
  value: number | string
  suffix?: string
  prefix?: string
  trend?: number
  size?: 'small' | 'medium' | 'large'
  color?: 'blue' | 'green' | 'orange' | 'red' | 'purple'
  loading?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  suffix: undefined,
  prefix: undefined,
  trend: undefined,
  size: 'medium',
  color: 'blue',
  loading: false,
})

const colorMap: Record<string, string> = {
  blue: '#2080f0',
  green: '#18a058',
  orange: '#f0a020',
  red: '#d03050',
  purple: '#7b61ff',
}

const borderColorValue = computed(() => colorMap[props.color] ?? colorMap.blue)
</script>

<style scoped>
/* CSS variable border — cannot be expressed as utility */
.t-stat-card {
  border-left: 3px solid var(--t-stat-border-color, #2080f0);
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
