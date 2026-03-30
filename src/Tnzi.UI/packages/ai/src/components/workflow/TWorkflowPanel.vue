<script setup lang="ts">
/**
 * TWorkflowPanel — Canvas floating panel
 */

import { cn } from '@/lib/utils';

defineProps<{
  position?: 'top-left' | 'top-right' | 'bottom-left' | 'bottom-right';
}>();

const positionClasses: Record<string, string> = {
  'top-left': 'top-3 left-3',
  'top-right': 'top-3 right-3',
  'bottom-left': 'bottom-3 left-3',
  'bottom-right': 'bottom-3 right-3',
};
</script>

<template>
  <div :class="cn('absolute z-10', positionClasses[position ?? 'top-right'])">
    <slot />
  </div>
</template>
