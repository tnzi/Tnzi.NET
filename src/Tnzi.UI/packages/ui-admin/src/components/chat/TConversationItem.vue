<template>
  <div
    class="t-conv-item"
    :class="{ 't-conv-item--active': active, 't-conv-item--sticky': item.isSticky }"
    role="button"
    tabindex="0"
    @click="emit('select')"
    @keydown.enter="emit('select')"
    @keydown.space.prevent="emit('select')"
    @contextmenu.prevent="emit('context-menu', $event)"
  >
    <!-- Avatar with unread badge. Groups render a WeChat-style composite of the
         earliest-joined member avatars when the backend supplies them; an
         explicit group avatar (avatarFileId) still wins. -->
    <NBadge :value="item.unreadCount" :show="item.unreadCount > 0" :max="99" class="t-conv-item__badge">
      <TGroupAvatar
        v-if="item.type === ConversationType.Group && !item.avatarFileId && item.memberAvatars?.length"
        :members="item.memberAvatars"
        :size="38"
      />
      <TChatAvatar
        v-else
        :name="item.title"
        :file-id="item.avatarFileId"
        :seed="item.id"
        :size="38"
        :system="item.type === ConversationType.System"
        :status="presence !== false && item.type === ConversationType.Direct ? item.peerStatus : null"
      />
    </NBadge>

    <!-- Content -->
    <div class="t-conv-item__body">
      <div class="t-conv-item__line">
        <Icon v-if="item.isSticky" icon="mdi:pin" class="t-conv-item__pin" :width="12" />
        <span class="t-conv-item__title">{{ item.remark || item.title }}</span>
        <span class="t-conv-item__time">{{ timeLabel }}</span>
      </div>
      <div class="t-conv-item__line">
        <span class="t-conv-item__preview">{{ item.lastMessagePreview ?? '' }}</span>
        <Icon v-if="item.isMuted" icon="mdi:bell-off-outline" class="t-conv-item__mute" :width="13" />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NBadge } from 'naive-ui'
import { Icon } from '@iconify/vue'
import type { ConversationListItemDto } from '@tnzi/core/services/chat'
import { ConversationType } from '@tnzi/core/services/chat'
import { formatChatTime } from './time'
import { translatePageKey } from '../../pages/_shared/translate'
import TChatAvatar from './TChatAvatar.vue'
import TGroupAvatar from './TGroupAvatar.vue'

const props = defineProps<{
  item: ConversationListItemDto
  active: boolean
  /** Deployment presence toggle — false hides the peer status dot. */
  presence?: boolean
}>()

const emit = defineEmits<{
  select: []
  /** Right-click — the list opens the quick-action context menu at the cursor. */
  'context-menu': [e: MouseEvent]
}>()

const timeLabel = computed(() => formatChatTime(props.item.lastMessageAt, translatePageKey('chat', 'window.yesterday')))
</script>

<style scoped>
.t-conv-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 14px;
  cursor: pointer;
  user-select: none;
  transition: background 0.12s ease;
}

.t-conv-item:hover {
  background: var(--chat-hover, #ececec);
}

.t-conv-item--active,
.t-conv-item--active:hover {
  background: var(--chat-active, #d9d9d9);
}

.t-conv-item__badge {
  flex-shrink: 0;
}

/* Tight two-line block: title + preview ≈ the avatar's height so the row reads
   compact and the text aligns to the avatar instead of sprawling. */
.t-conv-item__body {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.t-conv-item__line {
  display: flex;
  align-items: center;
  gap: 6px;
}

.t-conv-item__title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 14px;
  font-weight: 500;
  color: var(--chat-text, #1f1f1f);
}

.t-conv-item__time {
  flex-shrink: 0;
  font-size: 11px;
  color: var(--chat-text-3, #b0b0b0);
}

.t-conv-item__preview {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 12.5px;
  color: var(--chat-text-2, #8a8a8a);
}

.t-conv-item__mute {
  flex-shrink: 0;
  color: var(--chat-text-3, #b0b0b0);
}

.t-conv-item__pin {
  flex-shrink: 0;
  color: var(--chat-text-3, #b0b0b0);
}

.t-conv-item--sticky {
  background: color-mix(in srgb, var(--chat-hover, #ececec) 60%, transparent);
}
</style>
