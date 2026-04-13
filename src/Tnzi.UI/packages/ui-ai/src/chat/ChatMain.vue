<script setup lang="ts">
/**
 * ChatMain — Main chat area: header + message list + prompt input
 */

import type { ChatMessage } from '@/composables/useChat';
import type { FeedbackValue } from '@/components/chat/MessageFeedback.vue';
import type { SuggestionItem } from '@/components/chat/Suggestions.vue';
import ChatBox from '@/components/chat/ChatBox.vue';
import ChatHeader from './ChatHeader.vue';

defineProps<{
  messages: readonly ChatMessage[];
  isStreaming?: boolean;
  inputText?: string;
  suggestions?: SuggestionItem[];
  agentName?: string;
  threadTitle?: string;
}>();

const emit = defineEmits<{
  send: [content: string, files: File[]];
  stop: [];
  copy: [messageId: string];
  regenerate: [messageId: string];
  edit: [messageId: string];
  feedback: [messageId: string, type: FeedbackValue, reason?: string];
  'update:inputText': [value: string];
  export: [];
  settings: [];
  'toggle-sidebar': [];
}>();
</script>

<template>
  <div class="flex h-full flex-col">
    <ChatHeader
      :agent-name="agentName"
      :title="threadTitle"
      @export="emit('export')"
      @settings="emit('settings')"
      @toggle-sidebar="emit('toggle-sidebar')"
    >
      <template v-if="$slots['header-extra']" #right>
        <slot name="header-extra" />
      </template>
    </ChatHeader>
    <ChatBox
      :messages="messages"
      :is-streaming="isStreaming"
      :input-text="inputText"
      :suggestions="suggestions"
      class="flex-1"
      @send="(content: string, files: File[]) => emit('send', content, files)"
      @stop="emit('stop')"
      @copy="emit('copy', $event)"
      @regenerate="emit('regenerate', $event)"
      @edit="emit('edit', $event)"
      @feedback="(id, type, reason) => emit('feedback', id, type, reason)"
      @update:input-text="emit('update:inputText', $event)"
    >
      <template v-if="$slots['input-above']" #header>
        <slot name="input-above" />
      </template>
    </ChatBox>
  </div>
</template>
