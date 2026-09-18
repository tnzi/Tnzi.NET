<script setup lang="ts">
/**
 * TInlineChat - Simple wrapper that fills parent container with TChatBox
 */

import TChatBox from '../components/chat/TChatBox.vue';
import type { ChatMessage } from '../headless/useChat';

withDefaults(
  defineProps<{
    messages: readonly ChatMessage[];
    isStreaming?: boolean;
    inputText?: string;
    /** Paperclip + drag/drop + paste. Default true; set false when the
     *  transport cannot carry files (BYO `onSend` that ignores them). */
    enableAttachments?: boolean;
  }>(),
  // An absent boolean prop is cast to false, so the default has to be explicit.
  { enableAttachments: true },
);

const emit = defineEmits<{
  send: [content: string, files: File[]];
  stop: [];
  /** The Retry on an errored row / the row's regenerate action; relayed to the host. */
  regenerate: [messageId: string];
  'update:inputText': [value: string];
}>();
</script>

<template>
  <div class="h-full w-full">
    <TChatBox
      :messages="messages"
      :is-streaming="isStreaming"
      :input-text="inputText"
      :enable-attachments="enableAttachments"
      @send="(content: string, files: File[]) => emit('send', content, files)"
      @stop="emit('stop')"
      @regenerate="emit('regenerate', $event)"
      @update:input-text="emit('update:inputText', $event)"
    />
  </div>
</template>
