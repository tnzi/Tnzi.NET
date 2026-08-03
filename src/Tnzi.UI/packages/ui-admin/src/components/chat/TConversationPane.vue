<template>
  <div class="t-conv-pane">
    <!-- Header (always present): a single 52px bar matching the conversation
         list's avatar/search row. It doubles as the window drag strip
         (drag-start handled by TChatWindow) and hosts the window controls via
         the #winctl slot on desktop, so the two columns share one top line. -->
    <div class="t-conv-pane__header" @mousedown="emit('drag-start', $event)">
      <button v-if="isSm && conversation" class="t-conv-pane__icon-btn t-conv-pane__back" :title="t('back')" @mousedown.stop @click="emit('back')">
        <Icon icon="mdi:arrow-left" :width="20" />
      </button>

      <div class="t-conv-pane__titles">
        <template v-if="conversation">
          <span class="t-conv-pane__title">{{ headerTitle }}</span>
          <!-- Info panel toggle: a quiet thin-stroke gear right after the
               title (a content action, clearly apart from the window controls
               at the far right). Lucide's stroke style keeps it unobtrusive
               next to the title text. System conversations have no settings
               (no members/remark/mute of interest), so no toggle at all. -->
          <button
            v-if="showInfoToggle"
            class="t-conv-pane__icon-btn t-conv-pane__info-toggle"
            :class="{ 't-conv-pane__icon-btn--active': infoShow }"
            :title="t('window.info')"
            @mousedown.stop
            @click="emit('toggle-info')"
          >
            <Icon icon="lucide:settings" :width="15" />
          </button>
        </template>
      </div>

      <!-- Window controls (maximize/close) injected by TChatWindow on desktop. -->
      <div class="t-conv-pane__actions" @mousedown.stop>
        <slot name="winctl" />
      </div>
    </div>

    <!-- Body row: message column + slide-in info panel (panel never overlays
         the messages - the message column shrinks/keeps its own scroll while
         the panel takes a fixed 250px on the right). -->
    <div class="t-conv-pane__body">
      <div class="t-conv-pane__col">
        <div class="t-conv-pane__main">
          <div v-if="!conversation" class="t-conv-pane__empty">
            <!-- Same glyph as the chat launcher so the empty state reads as
                 part of the same feature. -->
            <Icon icon="nimbus:chat-dots" :width="56" class="t-conv-pane__empty-icon" />
            <span class="t-conv-pane__empty-text">{{ t('window.emptyPane') }}</span>
          </div>
          <TMessageList
            v-else
            :messages="messages"
            :my-id="myId"
            :my-name="myName"
            :my-avatar-file-id="myAvatarFileId"
            :is-group="conversation.type === ConversationType.Group"
            :is-system="conversation.type === ConversationType.System"
            @retry="emit('retry', $event)"
          />
        </div>

        <!-- Composer (hidden for System conversations and empty state) -->
        <TMessageComposer
          v-if="conversation && conversation.type !== ConversationType.System"
          :uploading="uploading"
          :upload-progress="uploadProgress"
          :upload-kind="uploadKind"
          :upload-name="uploadName"
          :attachments="attachments"
          @send="emit('send', $event)"
          @pick-file="emit('pick-file')"
          @drop-file="emit('drop-file', $event)"
        />
      </div>

      <!-- Slide-in conversation info panel -->
      <div class="t-conv-pane__info" :class="{ 't-conv-pane__info--open': infoShow }">
        <TConversationInfoPanel
          :show="!!infoShow"
          :conversation-id="conversation?.id ?? null"
          :my-id="myId"
          @update:show="emit('update:info-show', $event)"
          @changed="emit('panel-changed')"
          @open-conversation="emit('open-conversation', $event)"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import { ConversationType } from '@tnzi/core/services/chat'
import type { ConversationListItemDto } from '@tnzi/core/services/chat'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { translatePageKey } from '../../i18n/translate'
import type { ChatMessageView } from '../../stores/useChatStore'
import TMessageList from './TMessageList.vue'
import TMessageComposer from './TMessageComposer.vue'
import TConversationInfoPanel from './TConversationInfoPanel.vue'

const props = defineProps<{
  conversation: ConversationListItemDto | null
  messages: ChatMessageView[]
  myId?: string
  myName?: string
  myAvatarFileId?: string | null
  uploading?: boolean
  uploadProgress?: number
  uploadKind?: 'image' | 'file'
  uploadName?: string
  infoShow?: boolean
  /** Deployment file-message toggle - false hides the attachment entry. */
  attachments?: boolean
}>()

const emit = defineEmits<{
  send: [text: string]
  'pick-file': []
  'drop-file': [file: File]
  'toggle-info': []
  'update:info-show': [v: boolean]
  'panel-changed': []
  'open-conversation': [id: string]
  'drag-start': [e: MouseEvent]
  retry: [message: ChatMessageView]
  back: []
}>()

const t = (k: string) => translatePageKey('chat', k)
const { isSm } = useBreakpoint()

// Group title carries the member count in parentheses - `Team (5)` - matching
// the direct-chat title style (no separate "N Members" subtitle).
const headerTitle = computed(() => {
  const c = props.conversation
  if (!c) return ''
  return c.type === ConversationType.Group ? `${c.title} (${c.memberCount})` : c.title
})

// System conversations are read-only announcements - no info panel to open.
const showInfoToggle = computed(
  () => !!props.conversation && props.conversation.type !== ConversationType.System,
)
</script>

<style scoped>
.t-conv-pane {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-width: 0;
  background: var(--chat-bg, #f5f5f5);
}

/* ── Header ─────────────────────────────────────────────────────────────── */
.t-conv-pane__header {
  display: flex;
  align-items: center;
  gap: 8px;
  /* Same height as the list's avatar/search row (.t-conv-list__top:
     12px + 30px content + 10px), so both columns share one top line. */
  height: 52px;
  padding: 0 12px 0 18px;
  border-bottom: 1px solid var(--chat-border, #e6e6e6);
  flex-shrink: 0;
  /* The whole bar is the window drag handle. */
  cursor: move;
}

.t-conv-pane__titles {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 4px;
}

.t-conv-pane__title {
  min-width: 0;
  font-size: 16px;
  font-weight: 500;
  color: var(--chat-text, #1f1f1f);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-conv-pane__actions {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
}

.t-conv-pane__icon-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border: none;
  border-radius: 6px;
  background: transparent;
  cursor: pointer;
  color: var(--chat-text-2, #5a5a5a);
  transition: background 0.12s, color 0.12s;
}

.t-conv-pane__icon-btn:hover {
  background: var(--chat-hover, #e8e8e8);
  color: var(--chat-text, #1f1f1f);
}

.t-conv-pane__icon-btn--active {
  background: var(--chat-active, #e0e0e0);
  color: var(--chat-text, #1f1f1f);
}

/* Info toggle next to the title: quieter than a regular icon button (faint
   until hovered) and slightly smaller so it reads as part of the title. */
.t-conv-pane__info-toggle {
  width: 22px;
  height: 22px;
  color: var(--chat-text-3, #9b9b9b);
}

/* ── Body row: message column + slide-in panel ──────────────────────────── */
.t-conv-pane__body {
  flex: 1;
  min-height: 0;
  display: flex;
  overflow: hidden;
}

/* Message column (list + composer) - always visible, shrinks as the panel
   opens but keeps its own scroll instead of being covered. */
.t-conv-pane__col {
  flex: 1;
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

/* Slide-in info panel: collapses to width 0 (hidden) ↔ 250px (open) with a
   transition. Never an overlay - messages stay visible alongside it. */
.t-conv-pane__info {
  flex-shrink: 0;
  width: 0;
  overflow: hidden;
  transition: width 0.22s ease;
}

.t-conv-pane__info--open {
  width: 250px;
}

/* ── Body ───────────────────────────────────────────────────────────────── */
.t-conv-pane__main {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.t-conv-pane__empty {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
}

.t-conv-pane__empty-icon {
  color: var(--chat-text-3, #cfcfcf);
}

.t-conv-pane__empty-text {
  font-size: 13.5px;
  color: var(--chat-text-3, #aeaeae);
}

/* Touch: enlarge the header icon buttons (back / info gear) to a ≥40px tap
   target - the visible glyphs stay their small size, the hit area grows around
   them. Placed after the base rules so it overrides the 30px / 22px defaults.
   Coarse-pointer only, so desktop keeps the compact 30px / 22px chrome. */
@media (pointer: coarse) {
  .t-conv-pane__icon-btn,
  .t-conv-pane__info-toggle {
    width: 40px;
    height: 40px;
  }
}

/* Phone: the window is one column, so an open panel would crush the message
   column. Let it cover the body (absolute, full-width) instead - desktop keeps
   the side-by-side slide-in. */
@media (max-width: 768px) {
  /* Match the conversation list's 12px edge padding so the back arrow sits on
     the same left line as the list avatars (the desktop 18px is tuned for a
     leading title, not a leading icon button). */
  .t-conv-pane__header {
    padding: 0 12px;
  }

  .t-conv-pane__body {
    position: relative;
  }

  .t-conv-pane__info {
    position: absolute;
    inset: 0 0 0 auto;
    width: 100%;
    transition: transform 0.22s ease;
    transform: translateX(100%);
  }

  .t-conv-pane__info--open {
    width: 100%;
    transform: translateX(0);
  }
}
</style>
