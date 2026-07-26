<script setup lang="ts">
/**
 * `TWidgetChatStats` - chat IM summary widget.
 *
 * The old announcement-style session API was removed in the Chat IM refactor
 * (feat/chat-im-refactor); the standalone broadcast page was later folded
 * into the Conversations page (its toolbar opens the Broadcast dialog).
 * This widget shows a static quick-link there. Navigate by route NAME so
 * the link follows any `basePath` / history-base deployment prefix.
 */
import { TSvgIcon } from '@tnzi/ui'
import { useRouter } from 'vue-router'
import { resolveBackendLabel } from '../../pages/_shared/translate'

const router = useRouter()

// `resolveBackendLabel` returns the dictionary hit when present and the English
// fallback on a miss (it detects the miss by comparing against the humanised
// last segment, unlike a bare `translatePageKey(...) || fallback` where the
// humanised string is always truthy and the fallback is dead).
function t(key: string, fallback: string): string {
  return resolveBackendLabel(key, fallback)
}

function go(): void {
  router.push({ name: 'chat.conversations' }).catch(() => undefined)
}
</script>

<template>
  <div class="t-widget-chat">
    <div class="t-widget-chat__cell" style="cursor: pointer" @click="go">
      <span class="t-widget-chat__icon" data-tone="primary">
        <TSvgIcon icon="mdi:bullhorn-outline" :size="20" />
      </span>
      <div class="t-widget-chat__text">
        <span class="t-widget-chat__label">{{ t('admin.widgets.chat.broadcast', 'Broadcast') }}</span>
        <span class="t-widget-chat__desc">{{ t('admin.widgets.chat.broadcastDesc', 'Send a message to users') }}</span>
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
