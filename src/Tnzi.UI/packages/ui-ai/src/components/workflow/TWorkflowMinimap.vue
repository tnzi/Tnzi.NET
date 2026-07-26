<script setup lang="ts">
/**
 * TWorkflowMinimap - @vue-flow minimap wrapper
 */

import { MiniMap } from '@vue-flow/minimap';
import '@vue-flow/minimap/dist/style.css';

defineProps<{
  visible?: boolean;
}>();
</script>

<template>
  <MiniMap v-if="visible ?? true" class="!bottom-3 !right-3" />
</template>
