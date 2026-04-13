<script setup lang="ts">
/**
 * WorkflowNode — Card-based graph node with handles
 */

import {
  Card,
  CardHeader,
  CardTitle,
  CardDescription,
  CardContent,
  CardFooter,
} from '../../primitives';
import { Handle, Position } from '@vue-flow/core';
import { cn } from '@/lib/utils';
defineProps<{
  label: string;
  description?: string;
  status?: 'pending' | 'active' | 'completed' | 'failed';
  targetHandle?: boolean;
  sourceHandle?: boolean;
}>();

const statusClasses: Record<string, string> = {
  pending: 'border-border',
  active: 'border-ai-node-active ring-2 ring-ai-node-active/20',
  completed: 'border-ai-node-completed',
  failed: 'border-ai-node-failed ring-2 ring-ai-node-failed/20',
};
</script>

<template>
  <Card :class="cn('min-w-[180px] max-w-[250px] shadow-md', statusClasses[status ?? 'pending'])">
    <Handle v-if="targetHandle" type="target" :position="Position.Left" class="!bg-primary !border-background !w-3 !h-3" />
    <CardHeader class="p-3 pb-1">
      <CardTitle class="text-xs font-medium">{{ label }}</CardTitle>
      <CardDescription v-if="description" class="text-[11px]">{{ description }}</CardDescription>
    </CardHeader>
    <CardContent v-if="$slots.content || $slots.default" class="p-3 pt-0">
      <slot name="content"><slot /></slot>
    </CardContent>
    <CardFooter v-if="$slots.footer" class="p-3 pt-0">
      <slot name="footer" />
    </CardFooter>
    <Handle v-if="sourceHandle" type="source" :position="Position.Right" class="!bg-primary !border-background !w-3 !h-3" />
  </Card>
</template>
