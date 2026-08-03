<script setup lang="ts">
/**
 * TInlineChat - Simple wrapper that fills parent container with TChatBox
 */

import TChatBox from '../components/chat/TChatBox.vue';
import type { ChatMessage } from '../headless/useChat';

defineProps<{
  messages: readonly ChatMessage[];
  isStreaming?: boolean;
  inputText?: string;
}>();

const emit = defineEmits<{
  send: [content: string, files: File[]];
  stop: [];
  'update:inputText': [value: string];
}>();
</script>

<template>
  <div class="h-full w-full">
    <TChatBox
      :messages="messages"
      :is-streaming="isStreaming"
      :input-text="inputText"
      @send="(content: string, files: File[]) => emit('send', content, files)"
      @stop="emit('stop')"
      @update:input-text="emit('update:inputText', $event)"
    />
  </div>
</template>
