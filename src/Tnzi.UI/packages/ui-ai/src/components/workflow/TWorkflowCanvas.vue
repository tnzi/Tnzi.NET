<script setup lang="ts">
/**
 * TWorkflowCanvas - @vue-flow canvas wrapper
 */

import { computed, useSlots } from 'vue';
import { VueFlow, type Node, type Edge } from '@vue-flow/core';
import { Background } from '@vue-flow/background';
import '@vue-flow/core/dist/style.css';
import '@vue-flow/core/dist/theme-default.css';

defineProps<{
  nodes: Node[];
  edges: Edge[];
  fitView?: boolean;
}>();

defineEmits<{
  'nodes-change': [changes: unknown[]];
  'edges-change': [changes: unknown[]];
  'node-click': [event: unknown];
  'edge-click': [event: unknown];
  connect: [connection: unknown];
}>();

const slots = useSlots();

/* `default` is forwarded explicitly below, next to <Background>. Including it
   in the dynamic forwarding loop would make Vue's generated createSlots()
   overwrite the whole default slot with the forwarding function, silently
   dropping <Background> from the canvas. */
const forwardedSlotNames = computed(() =>
  Object.keys(slots).filter((name) => name !== 'default'),
);
</script>

<template>
  <div class="h-full w-full">
    <VueFlow
      :nodes="nodes"
      :edges="edges"
      :fit-view-on-init="fitView ?? true"
      :pan-on-drag="false"
      :pan-on-scroll="true"
      :selection-on-drag="true"
      :zoom-on-double-click="false"
      :delete-key-code="['Backspace', 'Delete']"
      @nodes-change="$emit('nodes-change', $event)"
      @edges-change="$emit('edges-change', $event)"
      @node-click="$emit('node-click', $event)"
      @edge-click="$emit('edge-click', $event)"
      @connect="$emit('connect', $event)"
    >
      <Background :style="{ backgroundColor: 'var(--tnzi-layout-bg)' }" />
      <slot />
      <!--
        Forward every named slot to VueFlow so consumers can override built-in
        node/edge types (`#node-default`, `#node-input`, `#edge-default`, …) or
        inject controls/minimap from outside the wrapper without having to
        fork the component. `default` is excluded on purpose (see script).
      -->
      <template v-for="name in forwardedSlotNames" #[name]="scope">
        <slot :name="name" v-bind="scope" />
      </template>
    </VueFlow>
  </div>
</template>
