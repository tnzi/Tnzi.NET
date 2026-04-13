<script setup lang="ts">
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
</script>

<template>
  <van-card>
    <template #title>
      <div class="text-sm text-slate-500">{{ props.title }}</div>
    </template>
    <template #desc>
      <div v-if="props.loading" class="text-sm text-slate-400">Loading...</div>
      <div v-else class="flex items-end gap-1">
        <span v-if="props.prefix" class="text-sm text-slate-500">{{ props.prefix }}</span>
        <span class="text-3xl font-bold">{{ props.value }}</span>
        <span v-if="props.suffix" class="text-sm text-slate-500">{{ props.suffix }}</span>
      </div>
      <div v-if="props.trend != null" class="mt-1 text-xs" :class="props.trend >= 0 ? 'text-emerald-600' : 'text-rose-600'">
        {{ props.trend >= 0 ? '+' : '' }}{{ props.trend }}%
      </div>
    </template>
  </van-card>
</template>
