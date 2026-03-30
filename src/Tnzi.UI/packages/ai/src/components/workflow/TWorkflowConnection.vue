<script setup lang="ts">
/**
 * TWorkflowConnection — Connection preview line during drag
 */

import { computed } from 'vue';
import type { ConnectionLineProps } from '@vue-flow/core';
import { calcBezierPath } from './calcBezierPath';

const props = defineProps<ConnectionLineProps>();

const path = computed(() =>
  calcBezierPath(props.sourceX, props.sourceY, props.sourcePosition, props.targetX, props.targetY, props.targetPosition),
);
</script>

<template>
  <path :d="path" fill="none" stroke="hsl(var(--ring))" stroke-dasharray="5, 5" stroke-width="1.5" :style="{ opacity: 0.6 }" />
</template>
