<script setup lang="ts">
/**
 * NotificationContainer — naive-ui style
 *
 * Fixed positioned container at top-right by default.
 * Mount once at app root (like NNotificationProvider).
 * Renders all active notifications from notification-store.
 */
import { Teleport } from 'vue';
import { notificationState } from './notification-store';
import NotificationItem from './NotificationItem.vue';

const placementStyles: Record<string, Record<string, string>> = {
  'top-right': { top: '12px', right: '12px', display: 'flex', flexDirection: 'column', alignItems: 'flex-end' },
  'top-left': { top: '12px', left: '12px', display: 'flex', flexDirection: 'column', alignItems: 'flex-start' },
  'bottom-right': { bottom: '12px', right: '12px', display: 'flex', flexDirection: 'column-reverse', alignItems: 'flex-end' },
  'bottom-left': { bottom: '12px', left: '12px', display: 'flex', flexDirection: 'column-reverse', alignItems: 'flex-start' },
};
</script>

<template>
  <Teleport to="body">
    <div
      class="notif-container"
      :style="placementStyles[notificationState.placement] || placementStyles['top-right']"
    >
      <NotificationItem
        v-for="notif in notificationState.notifications"
        :key="notif.key"
        :notification="notif"
        :placement="notificationState.placement"
      />
    </div>
  </Teleport>
</template>

<style scoped>
.notif-container {
  position: fixed;
  z-index: 6000;
  pointer-events: none;
}
.notif-container > * {
  pointer-events: all;
}
</style>
