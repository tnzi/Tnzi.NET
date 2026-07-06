<script setup lang="ts">
/**
 * ChatMessage — Single message bubble
 *
 * Renders a user or assistant message with support for reasoning,
 * tool calls, attachments, actions, and feedback.
 */

import { computed } from 'vue';
import { TAvatar } from '@tnzi/ui';
import type { ChatMessage } from '@/composables/useChat';
import type { FeedbackValue } from './MessageFeedback.vue';
import MessageResponse from './MessageResponse.vue';
import MessageAttachments from './MessageAttachments.vue';
import MessageActions from './MessageActions.vue';
import MessageFeedback from './MessageFeedback.vue';
import Reasoning from '../reasoning/Reasoning.vue';
import ToolCallDisplay from '../reasoning/ToolCallDisplay.vue';

const props = withDefaults(defineProps<{
  message: ChatMessage;
  showFeedback?: boolean;
  showBranch?: boolean;
  showActions?: boolean;
}>(), {
  showFeedback: false,
  showBranch: false,
  showActions: true,
});

const emit = defineEmits<{
  copy: [messageId: string];
  regenerate: [messageId: string];
  edit: [messageId: string];
  feedback: [messageId: string, type: FeedbackValue, reason?: string];
}>();

const isUser = computed(() => props.message.role === 'user');
const isAssistant = computed(() => props.message.role === 'assistant');
const isStreaming = computed(() => props.message.isStreaming ?? false);

const hasReasoning = computed(() => !!props.message.reasoning);
const hasToolCalls = computed(
  () => !!props.message.toolCalls && props.message.toolCalls.length > 0,
);
const hasAttachments = computed(
  () => !!props.message.attachments && props.message.attachments.length > 0,
);
</script>

<template>
  <div
    class="t-chat-message"
    :class="{ 't-chat-message--user': isUser, 't-chat-message--assistant': isAssistant }"
  >
    <!-- Avatar -->
    <div class="shrink-0">
      <slot name="avatar">
        <TAvatar
          :icon="isUser ? 'lucide:user' : 'lucide:bot'"
          prefer-icon
          :size="32"
          :color="isUser ? 'var(--tnzi-primary)' : 'var(--tnzi-border)'"
          :text-color="isUser ? '#fff' : 'var(--tnzi-base-text-muted)'"
        />
      </slot>
    </div>

    <!-- Content -->
    <div
      class="t-chat-message__content"
      :class="{ 't-chat-message__content--user': isUser }"
    >
      <!-- Agent name -->
      <span
        v-if="isAssistant && message.agentName"
        class="t-chat-message__agent-name"
      >
        {{ message.agentName }}
      </span>

      <!-- Reasoning (before content, assistant only) -->
      <Reasoning
        v-if="isAssistant && hasReasoning"
        :content="message.reasoning!"
        :is-streaming="isStreaming"
        class="w-full"
      />

      <!-- Tool calls (assistant only) -->
      <div
        v-if="isAssistant && hasToolCalls"
        class="flex w-full flex-col gap-1"
      >
        <ToolCallDisplay
          v-for="(tc, i) in message.toolCalls"
          :key="i"
          :tool-call="tc"
        />
      </div>

      <!-- Attachments -->
      <MessageAttachments
        v-if="hasAttachments"
        :attachments="message.attachments!"
      />

      <!-- Message bubble -->
      <div
        v-if="message.content"
        class="t-chat-message__bubble"
        :class="{ 't-chat-message__bubble--user': isUser, 't-chat-message__bubble--assistant': isAssistant }"
      >
        <!-- User: plain text -->
        <p v-if="isUser" class="whitespace-pre-wrap text-sm">
          {{ message.content }}
        </p>

        <!-- Assistant: rendered markdown -->
        <MessageResponse
          v-else
          :content="message.content"
          :streaming="isStreaming"
        />
      </div>

      <!-- Actions (assistant only, not while streaming) -->
      <div
        v-if="isAssistant && !isStreaming"
        class="flex items-center gap-2"
      >
        <slot name="actions">
          <MessageActions
            v-if="showActions"
            :show-copy="true"
            :show-regenerate="true"
            @copy="emit('copy', message.id)"
            @regenerate="emit('regenerate', message.id)"
            @edit="emit('edit', message.id)"
          />
          <MessageFeedback
            v-if="showFeedback"
            :value="null"
            @feedback="(type, reason) => emit('feedback', message.id, type, reason)"
          />
        </slot>
      </div>

      <!-- Footer slot -->
      <slot name="footer" />
    </div>
  </div>
</template>

<style scoped>
.t-chat-message {
  display: flex;
  gap: 12px;
  flex-direction: row;
}
.t-chat-message--user {
  flex-direction: row-reverse;
}
.t-chat-message__content {
  display: flex;
  max-width: 80%;
  flex-direction: column;
  gap: 8px;
  align-items: flex-start;
}
.t-chat-message__content--user {
  align-items: flex-end;
}
.t-chat-message__agent-name {
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-base-text-muted);
}
.t-chat-message__bubble {
  border-radius: 16px;
  padding: 10px 16px;
}
.t-chat-message__bubble--user {
  background-color: var(--tnzi-ai-chat-user-bg);
  color: var(--tnzi-ai-chat-user-text);
}
.t-chat-message__bubble--assistant {
  background-color: var(--tnzi-ai-chat-assistant-bg);
  color: var(--tnzi-ai-chat-assistant-text);
  border: 1px solid var(--tnzi-border);
}
</style>
