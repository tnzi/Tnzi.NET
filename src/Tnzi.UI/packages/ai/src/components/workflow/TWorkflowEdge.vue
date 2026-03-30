<script setup lang="ts">
/**
 * TWorkflowEdge — Animated bezier edge (dashed or flowing dot)
 */

import { computed } from 'vue';
import { BaseEdge, type EdgeProps } from '@vue-flow/core';
import { calcBezierPath } from './calcBezierPath';

const props = defineProps<EdgeProps & {
  variant?: 'default' | 'animated';
}>();

const path = computed(() =>
  calcBezierPath(props.sourceX, props.sourceY, props.sourcePosition, props.targetX, props.targetY, props.targetPosition),
);
</script>

<template>
  <template v-if="(variant ?? 'default') === 'default'">
    <BaseEdge :id="id" :path="path" :style="{ stroke: 'hsl(var(--ring))', strokeDasharray: '5, 5', strokeWidth: 1.5 }" />
  </template>
  <template v-else>
    <BaseEdge :id="id" :path="path" :style="{ stroke: 'hsl(var(--primary))', strokeWidth: 1.5 }" />
    <circle r="4" :fill="'hsl(var(--primary))'">
      <animateMotion :dur="'2s'" repeatCount="indefinite" :path="path" />
    </circle>
  </template>
</template>
