<script setup lang="ts">
/**
 * DialogProvider — naive-ui style dialog provider.
 *
 * Renders all active dialogs from reactive list.
 * Mount once at app root. Supports multiple concurrent dialogs.
 */
import { Teleport } from 'vue';
import { getDialogList, handleAfterLeave, registerInstance } from '../../composables/dialog-state';
import DialogEnvironment from './DialogEnvironment.vue';

const dialogList = getDialogList();
</script>

<template>
  <slot />
  <Teleport to="body">
    <DialogEnvironment
      v-for="dialog in dialogList"
      :key="dialog.key"
      :dialog="dialog"
      @register="(inst) => registerInstance(dialog.key, inst)"
      @after-leave="handleAfterLeave(dialog.key)"
    />
  </Teleport>
</template>
