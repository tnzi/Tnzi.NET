<script setup lang="ts">
/**
 * MessageContainer — naive-ui style
 *
 * Fixed positioned container at top center.
 * Mount once at app root (like NMessageProvider).
 * Renders all active messages from message-store.
 */
import { Teleport } from 'vue';
import { messageState } from './message-store';
import MessageItem from './MessageItem.vue';

const placementStyles: Record<string, Record<string, string>> = {
  'top': { top: '12px', left: '0', right: '0', display: 'flex', flexDirection: 'column', alignItems: 'center' },
  'top-left': { top: '12px', left: '12px', display: 'flex', flexDirection: 'column', alignItems: 'flex-start' },
  'top-right': { top: '12px', right: '12px', display: 'flex', flexDirection: 'column', alignItems: 'flex-end' },
  'bottom': { bottom: '12px', left: '0', right: '0', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end' },
  'bottom-left': { bottom: '12px', left: '12px', display: 'flex', flexDirection: 'column', alignItems: 'flex-start', justifyContent: 'flex-end' },
  'bottom-right': { bottom: '12px', right: '12px', display: 'flex', flexDirection: 'column', alignItems: 'flex-end', justifyContent: 'flex-end' },
};
</script>

<template>
  <Teleport to="body">
    <div
      class="message-container"
      :style="placementStyles[messageState.placement] || placementStyles.top"
    >
      <MessageItem
        v-for="msg in messageState.messages"
        :key="msg.key"
        :message="msg"
      />
    </div>
  </Teleport>
</template>

<style scoped>
.message-container {
  position: fixed;
  z-index: 6000;
  pointer-events: none;
}
.message-container > * {
  pointer-events: all;
}
</style>
