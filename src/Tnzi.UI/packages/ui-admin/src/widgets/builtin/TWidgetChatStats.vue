<script setup lang="ts">
/**
 * `TWidgetChatStats` — chat IM summary widget.
 *
 * The old announcement-style session API was removed in the Chat IM refactor
 * (feat/chat-im-refactor). The admin chat surface is now the broadcast form
 * at /admin/chat/broadcast. This widget shows a static quick-link.
 */
import { TSvgIcon } from '@tnzi/ui'
import { useRouter } from 'vue-router'

const router = useRouter()

function go(): void {
  router.push('/admin/chat/broadcast')
}
</script>

<template>
  <div class="t-widget-chat">
    <div class="t-widget-chat__cell" style="cursor: pointer" @click="go">
      <span class="t-widget-chat__icon" data-tone="primary">
        <TSvgIcon icon="mdi:bullhorn-outline" :size="20" />
      </span>
      <div class="t-widget-chat__text">
        <span class="t-widget-chat__label">Broadcast</span>
        <span class="t-widget-chat__desc">Send a message to users</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-widget-chat {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.t-widget-chat__cell {
  display: flex;
  align-items: center;
  gap: 12px;
}
.t-widget-chat__icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  flex-shrink: 0;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.12);
  color: var(--tnzi-primary);
}
.t-widget-chat__text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-widget-chat__label {
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-widget-chat__desc {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
</style>
