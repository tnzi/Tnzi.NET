<script setup lang="ts">
/**
 * TChatBox - Main chat container
 *
 * Assembles message list, auto-scroll, empty state, suggestions,
 * and prompt input into a complete chat interface.
 */

import { computed } from 'vue';
import type { ChatMessage } from '@/composables/useChat';
import { useAutoScroll } from '@/composables/useAutoScroll';
import type { FeedbackValue } from './TMessageFeedback.vue';
import type { SuggestionItem } from './TSuggestions.vue';
import TMessageList from './TMessageList.vue';
import TConversationEmpty from './TConversationEmpty.vue';
import TSuggestions from './TSuggestions.vue';
import TPromptInput from './TPromptInput.vue';
import TScrollButton from './TScrollButton.vue';

const props = withDefaults(defineProps<{
  messages: readonly ChatMessage[];
  isStreaming?: boolean;
  inputText?: string;
  suggestions?: SuggestionItem[];
  placeholder?: string;
}>(), {
  isStreaming: false,
  inputText: '',
  suggestions: () => [],
});

const emit = defineEmits<{
  send: [content: string, files: File[]];
  stop: [];
  copy: [messageId: string];
  regenerate: [messageId: string];
  edit: [messageId: string];
  feedback: [messageId: string, type: FeedbackValue, reason?: string];
  'update:inputText': [value: string];
}>();

const isEmpty = computed(() => props.messages.length === 0);

const { containerRef, isAtBottom, scrollToBottom } = useAutoScroll();

function handleSuggestionSelect(text: string): void {
  emit('update:inputText', text);
}

function handleSend(content: string, files: File[]): void {
  emit('send', content, files);
}
</script>

<template>
  <div class="flex h-full flex-col">
    <!-- Header slot -->
    <slot name="header" />

    <!-- Scrollable message area -->
    <div
      ref="containerRef"
      role="log"
      class="relative flex-1 overflow-y-auto"
    >
      <!-- Empty state -->
      <template v-if="isEmpty">
        <slot name="empty">
          <TConversationEmpty>
            <template #suggestions>
              <TSuggestions
                v-if="suggestions.length > 0"
                :suggestions="suggestions"
                @select="handleSuggestionSelect"
              />
            </template>
          </TConversationEmpty>
        </slot>
      </template>

      <!-- Message list -->
      <TMessageList
        v-else
        :messages="messages"
        @copy="emit('copy', $event)"
        @regenerate="emit('regenerate', $event)"
        @edit="emit('edit', $event)"
        @feedback="(id, type, reason) => emit('feedback', id, type, reason)"
      />

      <!-- Scroll to bottom button -->
      <TScrollButton
        :visible="!isAtBottom"
        @click="scrollToBottom()"
      />
    </div>

    <!-- Footer -->
    <div class="border-t border-border p-4">
      <slot name="footer">
        <!-- Suggestions (when there are messages) -->
        <TSuggestions
          v-if="!isEmpty && suggestions.length > 0"
          :suggestions="suggestions"
          class="mb-3"
          @select="handleSuggestionSelect"
        />

        <!-- Input -->
        <TPromptInput
          :model-value="inputText"
          :placeholder="placeholder"
          :loading="isStreaming"
          @update:model-value="emit('update:inputText', $event)"
          @submit="handleSend"
          @stop="emit('stop')"
        />
      </slot>
    </div>
  </div>
</template>
