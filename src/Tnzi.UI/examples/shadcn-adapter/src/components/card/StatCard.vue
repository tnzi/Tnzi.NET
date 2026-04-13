<script setup lang="ts">
import { computed } from 'vue';
interface IStatCardProps {
  title: string;
  value: number | string;
  suffix?: string;
  prefix?: string;
  trend?: number;
  chartData?: Array<{ x: string | number; y: number }>;
  size?: 'small' | 'medium' | 'large';
  color?: 'blue' | 'green' | 'orange' | 'red' | 'purple';
  loading?: boolean;
  class?: string | string[];
  style?: string | Record<string, string | number>;
}

const props = withDefaults(defineProps<IStatCardProps>(), {
  suffix: '',
  prefix: '',
  trend: undefined,
  chartData: () => [],
  size: 'medium',
  color: 'blue',
  loading: false,
});

const cardColorClass = computed(() => {
  const map = {
    blue: 'border-l-blue-500',
    green: 'border-l-emerald-500',
    orange: 'border-l-amber-500',
    red: 'border-l-rose-500',
    purple: 'border-l-violet-500',
  } as const;

  return map[props.color];
});

const sizeClass = computed(() => {
  const map = {
    small: 'p-4 text-sm',
    medium: 'p-5 text-base',
    large: 'p-6 text-lg',
  } as const;

  return map[props.size];
});

const trendClass = computed(() => {
  if (props.trend == null) return '';
  return props.trend >= 0 ? 'text-emerald-600' : 'text-rose-600';
});

const trendText = computed(() => {
  if (props.trend == null) return '';
  const sign = props.trend >= 0 ? '+' : '';
  return `${sign}${props.trend}%`;
});
</script>

<template>
  <article
    class="rounded-xl border border-l-4 bg-card text-card-foreground shadow-sm"
    :class="[cardColorClass, sizeClass]"
  >
    <p class="text-sm font-medium text-muted-foreground">{{ props.title }}</p>

    <div v-if="props.loading" class="mt-3 h-8 w-24 animate-pulse rounded bg-muted" />

    <div v-else class="mt-3 flex items-end gap-2">
      <span v-if="props.prefix" class="text-muted-foreground">{{ props.prefix }}</span>
      <span class="text-3xl font-bold leading-none tracking-tight">{{ props.value }}</span>
      <span v-if="props.suffix" class="text-sm text-muted-foreground">{{ props.suffix }}</span>
      <span v-if="props.trend != null" class="text-sm font-medium" :class="trendClass">
        {{ trendText }}
      </span>
    </div>
  </article>
</template>
