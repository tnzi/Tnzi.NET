<script setup lang="ts">
/**
 * TMessage — Single message bubble
 *
 * Renders a user or assistant message with support for reasoning,
 * tool calls, attachments, actions, and feedback.
 */

import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import type { ChatMessage } from '@/composables/useChat';
import type { FeedbackValue } from './TMessageFeedback.vue';
import TMessageResponse from './TMessageResponse.vue';
import TMessageAttachments from './TMessageAttachments.vue';
import TMessageActions from './TMessageActions.vue';
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
    :class="cn(
      'group flex gap-3',
      isUser ? 'flex-row-reverse' : 'flex-row',
    )"
  >
    <!-- Avatar -->
    <div class="shrink-0">
      <slot name="avatar">
        <div
          :class="cn(
            'flex size-8 items-center justify-center rounded-full',
            isUser ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground',
          )"
        >
          <Icon
            :icon="isUser ? 'lucide:user' : 'lucide:bot'"
            class="size-4"
          />
        </div>
      </slot>
    </div>

    <!-- Content -->
    <div
      :class="cn(
        'flex max-w-[80%] flex-col gap-2',
        isUser ? 'items-end' : 'items-start',
      )"
    >
      <!-- Agent name -->
      <span
        v-if="isAssistant && message.agentName"
        class="text-xs font-medium text-muted-foreground"
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
          :key="i"
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
        :class="cn(
          'rounded-2xl px-4 py-2.5',
          isUser
            ? 'bg-ai-user-bubble text-foreground'
            : 'bg-ai-assistant-bubble border border-border/50 text-foreground',
        )"
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

      <!-- Actions (assistant only, not while streaming) -->
      <div
        v-if="isAssistant && !isStreaming"
        class="flex items-center gap-2"
      >
        <slot name="actions">
          <TMessageActions
            v-if="showActions"
            :show-copy="true"
            :show-regenerate="true"
            @copy="emit('copy', message.id)"
            @regenerate="emit('regenerate', message.id)"
            @edit="emit('edit', message.id)"
          />
        </slot>
      </div>

      <!-- Footer slot -->
      <slot name="footer" />
    </div>
  </div>
</template>
