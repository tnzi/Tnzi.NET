<script setup lang="ts">
import { Icon } from '@iconify/vue';
import { computed } from 'vue';
import type { IconSize } from '@tnzi/core';

const props = withDefaults(defineProps<{
  /** Iconify icon name, format: collection:name (e.g. lucide:home) */
  icon: string;
  /** Icon size */
  size?: IconSize | number | string;
  /** Icon color */
  color?: string;
  /** Spin animation (for loading) */
  spin?: boolean;
}>(), {
  size: 'md',
});

const sizeMap: Record<IconSize, number> = {
  xs: 12,
  sm: 14,
  md: 16,
  lg: 20,
  xl: 24,
  '2xl': 28,
  '3xl': 32,
};

const resolvedSize = computed(() => {
  if (typeof props.size === 'number') return props.size;
  if (typeof props.size === 'string' && props.size in sizeMap) {
    return sizeMap[props.size as IconSize];
  }
  return props.size;
});
</script>

<template>
  <Icon
    :icon="icon"
    :width="resolvedSize"
    :height="resolvedSize"
    :color="color"
    :class="{ 'animate-spin': spin }"
    :inline="true"
  />
</template>
