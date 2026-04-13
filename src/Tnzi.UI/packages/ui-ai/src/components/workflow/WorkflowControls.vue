<script setup lang="ts">
/**
 * WorkflowControls — Zoom/pan control buttons
 */

import { Button } from '../../primitives';
import { useVueFlow } from '@vue-flow/core';
import { Icon } from '@iconify/vue';
const { zoomIn, zoomOut, fitView } = useVueFlow();
</script>

<template>
  <div class="flex items-center gap-1 rounded-lg border bg-background p-1 shadow-sm">
    <Button variant="ghost" size="icon-sm" @click="zoomIn()">
      <Icon icon="lucide:plus" class="size-4" />
    </Button>
    <Button variant="ghost" size="icon-sm" @click="zoomOut()">
      <Icon icon="lucide:minus" class="size-4" />
    </Button>
    <Button variant="ghost" size="icon-sm" @click="fitView()">
      <Icon icon="lucide:maximize-2" class="size-4" />
    </Button>
  </div>
</template>
