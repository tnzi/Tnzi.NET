<template>
  <n-card
    size="small"
    class="t-stat-card"
    :class="[`t-stat-card--${size}`, `t-stat-card--${resolvedColor}`]"
    :style="{ '--t-stat-border-color': borderColorValue }"
  >
    <template v-if="loading">
      <n-skeleton text :repeat="1" style="width: 60%" />
      <n-skeleton text style="width: 40%; height: 32px; margin-top: 8px" />
    </template>
    <template v-else>
      <n-statistic :label="title">
        <template #prefix v-if="prefix">
          <span class="t-stat-prefix">{{ prefix }}</span>
        </template>
        <n-number-animation
          v-if="typeof value === 'number'"
          :from="0"
          :to="value"
          :active="true"
        />
        <span v-else>{{ value }}</span>
        <template #suffix v-if="suffix || trend != null">
          <span v-if="suffix" class="t-stat-suffix">{{ suffix }}</span>
          <span
            v-if="trend != null"
            class="t-stat-trend"
            :class="{
              't-stat-trend--up': trend > 0,
              't-stat-trend--down': trend < 0,
              't-stat-trend--neutral': trend === 0,
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

const resolvedColor = computed(() => props.color)

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
.t-stat-card {
  border-left: 3px solid var(--t-stat-border-color, #2080f0);
  transition: box-shadow 0.2s ease;
}

.t-stat-card:hover {
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.08);
}

.t-stat-card--small :deep(.n-statistic .n-statistic-value) {
  font-size: 20px;
}

.t-stat-card--medium :deep(.n-statistic .n-statistic-value) {
  font-size: 28px;
}

.t-stat-card--large :deep(.n-statistic .n-statistic-value) {
  font-size: 36px;
}

.t-stat-prefix {
  font-size: 0.8em;
  margin-right: 2px;
}

.t-stat-suffix {
  font-size: 0.6em;
  margin-left: 4px;
  color: var(--n-text-color-3, #999);
}

.t-stat-trend {
  font-size: 13px;
  margin-left: 8px;
  font-weight: 500;
}

.t-stat-trend--up {
  color: #18a058;
}

.t-stat-trend--down {
  color: #d03050;
}

.t-stat-trend--neutral {
  color: #999;
}
</style>
