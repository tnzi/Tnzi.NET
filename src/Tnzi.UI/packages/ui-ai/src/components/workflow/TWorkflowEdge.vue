<script setup lang="ts">
/**
 * TWorkflowEdge - Animated bezier edge (dashed or flowing dot)
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
    <BaseEdge :id="id" :path="path" :style="{ stroke: 'var(--tnzi-border)', strokeDasharray: '5, 5', strokeWidth: 1.5 }" />
  </template>
  <template v-else>
    <BaseEdge :id="id" :path="path" :style="{ stroke: 'var(--tnzi-primary)', strokeWidth: 1.5 }" />
    <circle r="4" :fill="'var(--tnzi-primary)'">
      <animateMotion :dur="'2s'" repeatCount="indefinite" :path="path" />
    </circle>
  </template>
</template>
