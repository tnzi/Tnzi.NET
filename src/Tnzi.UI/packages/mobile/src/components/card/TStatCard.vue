<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';

type StatCardSize = 'small' | 'medium' | 'large';
type StatCardColor = 'blue' | 'green' | 'orange' | 'red' | 'purple';

interface IStatCardProps {
  title: string;
  value: number | string;
  suffix?: string;
  prefix?: string;
  trend?: number;
  /** Value type scale (default: 'medium') */
  size?: StatCardSize;
  /** Accent color applied to the value (default: 'blue') */
  color?: StatCardColor;
  loading?: boolean;
}

const props = withDefaults(defineProps<IStatCardProps>(), {
  suffix: '',
  prefix: '',
  trend: undefined,
  size: 'medium',
  color: 'blue',
  loading: false,
});

const { t } = useI18n();

const VALUE_SIZE_CLASS: Record<StatCardSize, string> = {
  small: 'text-xl',
  medium: 'text-3xl',
  large: 'text-4xl',
};

const VALUE_COLOR_VAR: Record<StatCardColor, string> = {
  blue: 'var(--van-blue)',
  green: 'var(--van-green)',
  orange: 'var(--van-orange)',
  red: 'var(--van-red)',
  purple: 'var(--van-purple, #7232dd)',
};

const valueClass = computed(() => VALUE_SIZE_CLASS[props.size]);
const valueStyle = computed(() => ({ color: VALUE_COLOR_VAR[props.color] }));
</script>

<template>
  <van-card>
    <template #title>
      <div class="text-sm text-van-muted">{{ props.title }}</div>
    </template>
    <template #desc>
      <div v-if="props.loading" class="text-sm text-van-subtle">{{ t('common.loading') }}</div>
      <div v-else class="flex items-end gap-1">
        <span v-if="props.prefix" class="text-sm text-van-muted">{{ props.prefix }}</span>
        <span class="font-bold" :class="valueClass" :style="valueStyle">{{ props.value }}</span>
        <span v-if="props.suffix" class="text-sm text-van-muted">{{ props.suffix }}</span>
      </div>
      <div
        v-if="props.trend != null"
        class="mt-1 text-xs"
        :class="props.trend >= 0 ? 'text-van-success' : 'text-van-danger'"
      >
        {{ props.trend >= 0 ? '+' : '' }}{{ props.trend }}%
      </div>
    </template>
  </van-card>
</template>
