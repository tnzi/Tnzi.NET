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
        :disabled="item.type === ConversationType.Direct && item.peerDisabled === true"
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

    <!-- Touch/phone: a persistent "more" button opens the same quick-action menu
         (pin / mute / mark-read / hide / delete) that right-click opens on
         desktop - touch devices have no @contextmenu. It reuses the exact same
         `context-menu` channel: TConversationList reads the click coordinates
         (clientX/clientY of this tap) to anchor its shared NDropdown. Stops
         propagation so tapping it doesn't also select the conversation. -->
    <button
      v-if="isTouch || isSm"
      class="t-conv-item__more"
      :title="t('window.moreActions')"
      @click.stop="emit('context-menu', $event)"
    >
      <Icon icon="mdi:dots-horizontal" :width="20" />
    </button>
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
import { useBreakpoint } from '../../headless/useBreakpoint'
import TChatAvatar from './TChatAvatar.vue'
import TGroupAvatar from './TGroupAvatar.vue'

const props = defineProps<{
  item: ConversationListItemDto
  active: boolean
  /** Deployment presence toggle - false hides the peer status dot. */
  presence?: boolean
}>()

const emit = defineEmits<{
  select: []
  /** Right-click (desktop) or the "more" tap (touch) - the list opens the
   *  quick-action context menu at the event's cursor coordinates. */
  'context-menu': [e: MouseEvent]
}>()

const t = (k: string) => translatePageKey('chat', k)
// Touch / phone gets a persistent "more" button since @contextmenu never fires.
const { isSm, isTouch } = useBreakpoint()

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

/* Touch/phone "more" button: a small dots glyph with a ≥40px tap target (the
   hit area is padded out around the icon so it's comfortable to tap without
   enlarging the visible glyph). Only rendered on touch/phone via v-if. */
.t-conv-item__more {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  min-height: 40px;
  align-self: stretch;
  border: none;
  background: transparent;
  cursor: pointer;
  color: var(--chat-text-3, #b0b0b0);
  border-radius: 6px;
  transition: background 0.12s, color 0.12s;
}

.t-conv-item__more:active {
  background: var(--chat-hover, rgb(51 54 57 / 0.08));
  color: var(--chat-text-2, #6f6f6f);
}
</style>
