<script setup lang="ts">
/**
 * TChatMessage - Single message bubble
 *
 * Renders a user or assistant message with support for reasoning,
 * tool calls, attachments, actions, and feedback.
 */

import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import { TAvatar } from '@tnzi/ui';
import { useAiI18n } from '../../i18n/index';
import type { ChatMessage } from '../../headless/useChat';
import type { FeedbackValue } from './TMessageFeedback.vue';
import TMessageResponse from './TMessageResponse.vue';
import TMessageAttachments from './TMessageAttachments.vue';
import TMessageActions, { type MessageAction } from './TMessageActions.vue';
import TMessageFeedback from './TMessageFeedback.vue';
import TReasoning from '../reasoning/TReasoning.vue';
import TToolCallDisplay from '../reasoning/TToolCallDisplay.vue';

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

const t = useAiI18n();

// Assistant action pills rendered through the canonical TMessageActions.
// Copy is TMessageActions' built-in icon button; regenerate is a labeled pill.
const messageActions = computed<MessageAction[]>(() => [
  {
    icon: 'lucide:refresh-cw',
    label: t.value.chat.retry,
    onClick: () => emit('regenerate', props.message.id),
  },
]);

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
      <TReasoning
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
        <TToolCallDisplay
          v-for="(tc, i) in message.toolCalls"
          :key="`${tc.name}:${i}`"
          :tool-call="tc"
        />
      </div>

      <!-- Attachments -->
      <TMessageAttachments
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
        <TMessageResponse
          v-else
          :content="message.content"
          :streaming="isStreaming"
        />
      </div>

      <!-- Failure: the row keeps whatever text arrived, and says what went
           wrong under it. Without this block an errored turn rendered as an
           assistant that had nothing to say. -->
      <div
        v-if="isAssistant && message.status === 'error'"
        class="t-chat-message__error"
        role="alert"
      >
        <Icon icon="lucide:circle-alert" class="t-chat-message__error-icon" />
        <span>{{ message.error || t.chat.errorGeneric }}</span>
        <button
          type="button"
          class="t-chat-message__error-retry"
          @click="emit('regenerate', message.id)"
        >
          <Icon icon="lucide:rotate-ccw" />
          {{ t.common.retry }}
        </button>
      </div>
      <div
        v-else-if="isAssistant && message.status === 'stopped'"
        class="t-chat-message__stopped"
      >
        <span class="t-chat-message__stopped-mark" aria-hidden="true" />
        {{ t.chat.generationStopped }}
      </div>

      <!-- Actions (assistant only, not while streaming) -->
      <div
        v-if="isAssistant && !isStreaming"
        class="flex items-center gap-2"
      >
        <slot name="actions">
          <TMessageActions
            v-if="showActions"
            :content="message.content ?? ''"
            :actions="messageActions"
            @copy="emit('copy', message.id)"
          />
          <TMessageFeedback
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
.t-chat-message__error {
  display: flex;
  align-items: center;
  gap: 8px;
  max-width: 100%;
  padding: 10px 14px;
  border: 1px solid color-mix(in srgb, var(--tnzi-ai-danger) 28%, transparent);
  background: color-mix(in srgb, var(--tnzi-ai-danger) 6%, var(--tnzi-ai-surface));
  border-radius: 10px;
  color: var(--tnzi-ai-danger);
  font-size: 14px;
}
.t-chat-message__error-icon {
  flex-shrink: 0;
  font-size: 16px;
}
.t-chat-message__error-retry {
  margin-left: auto;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  border: none;
  background: none;
  color: var(--tnzi-ai-danger);
  font: inherit;
  font-size: 13px;
  cursor: pointer;
  padding: 2px 6px;
  border-radius: 6px;
}
.t-chat-message__error-retry:hover {
  background: color-mix(in srgb, var(--tnzi-ai-danger) 10%, transparent);
}
.t-chat-message__stopped {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11.5px;
  color: var(--tnzi-ai-text-tertiary);
  user-select: none;
}
.t-chat-message__stopped-mark {
  width: 9px;
  height: 9px;
  border-radius: 2px;
  background: currentColor;
  flex-shrink: 0;
}
</style>
