<template>
  <div class="t-conv-pane">
    <!-- Header (always present so the window has a consistent top bar + close) -->
    <div class="t-conv-pane__header">
      <button v-if="isSm && conversation" class="t-conv-pane__icon-btn t-conv-pane__back" :title="t('back')" @click="emit('back')">
        <Icon icon="mdi:arrow-left" :width="20" />
      </button>

      <div v-if="conversation" class="t-conv-pane__titles">
        <span class="t-conv-pane__title">{{ conversation.title }}</span>
        <span v-if="conversation.type === ConversationType.Group" class="t-conv-pane__subtitle">
          {{ conversation.memberCount }} {{ t('window.members') }}
        </span>
      </div>
      <div v-else class="t-conv-pane__titles" />

      <div class="t-conv-pane__actions">
        <button
          v-if="conversation"
          class="t-conv-pane__icon-btn"
          :class="{ 't-conv-pane__icon-btn--active': infoShow }"
          :title="t('window.info')"
          @click="emit('toggle-info')"
        >
          <Icon icon="mdi:dots-horizontal" :width="18" />
        </button>
        <button
          v-if="showMaximize"
          class="t-conv-pane__icon-btn"
          :title="maximized ? t('window.restore') : t('window.maximize')"
          @click="emit('toggle-maximize')"
        >
          <Icon :icon="maximized ? 'mdi:window-restore' : 'mdi:window-maximize'" :width="16" />
        </button>
        <button class="t-conv-pane__icon-btn" :title="t('close')" @click="emit('close')">
          <Icon icon="mdi:close" :width="18" />
        </button>
      </div>
    </div>

    <!-- Body row: message column + slide-in info panel (panel never overlays
         the messages — the message column shrinks/keeps its own scroll while
         the panel takes a fixed 250px on the right). -->
    <div class="t-conv-pane__body">
      <div class="t-conv-pane__col">
        <div class="t-conv-pane__main">
          <div v-if="!conversation" class="t-conv-pane__empty">
            <Icon icon="mdi:message-text-outline" :width="56" class="t-conv-pane__empty-icon" />
            <span class="t-conv-pane__empty-text">{{ t('window.emptyPane') }}</span>
          </div>
          <TMessageList
            v-else
            :messages="messages"
            :my-id="myId"
            :my-name="myName"
            :my-avatar-file-id="myAvatarFileId"
            :is-group="conversation.type === ConversationType.Group"
          />
        </div>

        <!-- Composer (hidden for System conversations and empty state) -->
        <TMessageComposer
          v-if="conversation && conversation.type !== ConversationType.System"
          :uploading="uploading"
          :upload-progress="uploadProgress"
          :upload-kind="uploadKind"
          :upload-name="uploadName"
          @send="emit('send', $event)"
          @pick-file="emit('pick-file', $event)"
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
import { Icon } from '@iconify/vue'
import { ConversationType } from '@tnzi/core/services/chat'
import type { ConversationListItemDto, ChatMessageDto } from '@tnzi/core/services/chat'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { translatePageKey } from '../../pages/_shared/translate'
import TMessageList from './TMessageList.vue'
import TMessageComposer from './TMessageComposer.vue'
import TConversationInfoPanel from './TConversationInfoPanel.vue'

const props = defineProps<{
  conversation: ConversationListItemDto | null
  messages: ChatMessageDto[]
  myId?: string
  myName?: string
  myAvatarFileId?: string | null
  uploading?: boolean
  uploadProgress?: number
  uploadKind?: 'image' | 'file'
  uploadName?: string
  infoShow?: boolean
  maximized?: boolean
  showMaximize?: boolean
}>()

const emit = defineEmits<{
  send: [text: string]
  'pick-file': [type: 'image' | 'file']
  'toggle-info': []
  'update:info-show': [v: boolean]
  'toggle-maximize': []
  'panel-changed': []
  'open-conversation': [id: string]
  back: []
  close: []
}>()

const t = (k: string) => translatePageKey('chat', k)
const { isSm } = useBreakpoint()
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
  height: 56px;
  padding: 0 12px 0 18px;
  border-bottom: 1px solid var(--chat-border, #e6e6e6);
  flex-shrink: 0;
}

.t-conv-pane__titles {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 1px;
}

.t-conv-pane__title {
  font-size: 16px;
  font-weight: 500;
  color: var(--chat-text, #1f1f1f);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-conv-pane__subtitle {
  font-size: 12px;
  color: var(--chat-text-3, #a8a8a8);
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

/* ── Body row: message column + slide-in panel ──────────────────────────── */
.t-conv-pane__body {
  flex: 1;
  min-height: 0;
  display: flex;
  overflow: hidden;
}

/* Message column (list + composer) — always visible, shrinks as the panel
   opens but keeps its own scroll instead of being covered. */
.t-conv-pane__col {
  flex: 1;
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

/* Slide-in info panel: collapses to width 0 (hidden) ↔ 250px (open) with a
   transition. Never an overlay — messages stay visible alongside it. */
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

/* Phone: the window is one column, so an open panel would crush the message
   column. Let it cover the body (absolute, full-width) instead — desktop keeps
   the side-by-side slide-in. */
@media (max-width: 768px) {
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
