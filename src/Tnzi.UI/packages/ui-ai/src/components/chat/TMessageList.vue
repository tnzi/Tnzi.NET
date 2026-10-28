<script setup lang="ts">
/**
 * TMessageList - Grouped message list
 *
 * Renders messages grouped by role using groupMessages().
 * Shows loading skeleton and streaming indicator; each message (tool calls
 * included) is drawn by TChatMessage.
 */

import { computed } from 'vue';
import type { ChatMessage } from '../../headless/useChat';
import { groupMessages, type MessageGroup } from '../../headless/use-message-group';
import type { FeedbackValue } from './TMessageFeedback.vue';
import TChatMessage from './TChatMessage.vue';
import TStreamLoader from '../streaming/TStreamLoader.vue';
import { useAiI18n } from '../../i18n/index';

const props = withDefaults(defineProps<{
  messages: readonly ChatMessage[];
  /** Show loading skeleton when true and no messages. */
  loading?: boolean;
}>(), {
  loading: false,
});

const emit = defineEmits<{
  copy: [messageId: string];
  regenerate: [messageId: string];
  edit: [messageId: string];
  feedback: [messageId: string, type: FeedbackValue, reason?: string];
}>();

const t = useAiI18n();

const groups = computed<MessageGroup[]>(() => groupMessages(props.messages));

const isLastMessageStreaming = computed(() => {
  const msgs = props.messages;
  if (msgs.length === 0) return false;
  return msgs[msgs.length - 1]?.isStreaming === true;
});
</script>

<template>
  <div role="log" class="flex flex-col gap-6 p-4">
    <!-- Loading skeleton -->
    <template v-if="loading && messages.length === 0">
      <div class="space-y-4 animate-pulse">
        <div class="h-4 w-3/4 rounded bg-tnzi-layout" />
        <div class="h-4 w-1/2 rounded bg-tnzi-layout" />
        <div class="h-4 w-2/3 rounded bg-tnzi-layout" />
      </div>
    </template>

    <!-- Grouped messages -->
    <template v-else>
      <div v-for="group in groups" :key="group.id" class="flex flex-col gap-2">
        <!-- Tool calls are TChatMessage's to render: it draws them inside the
             bubble column, keyed by position. Drawing them here as well showed
             every card twice. -->
        <template v-for="message in group.messages" :key="message.id">
          <TChatMessage
            :message="message"
            :show-actions="message.role === 'assistant'"
            @copy="emit('copy', $event)"
            @regenerate="emit('regenerate', $event)"
            @edit="emit('edit', $event)"
            @feedback="(id, type, reason) => emit('feedback', id, type, reason)"
          />
        </template>
      </div>

      <!-- Streaming indicator -->
      <div v-if="isLastMessageStreaming" class="flex items-center gap-2 text-sm text-tnzi-muted">
        <TStreamLoader :size="14" />
        <span>{{ t.chat.generating }}</span>
      </div>
    </template>

    <!-- Bottom padding spacer for scroll room -->
    <div class="h-40" aria-hidden="true" />
  </div>
</template>
