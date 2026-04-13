<script setup lang="ts">
/**
 * SidebarChat — Right-side slide-in panel with overlay
 *
 * Fixed positioned. Uses useEmbedMode('sidebar').
 * Slot: #header for custom header content.
 */

import { Button } from '../primitives';
import { watch } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { useEmbedMode } from '@/composables/useEmbedMode';
import ChatBox from '@/components/chat/ChatBox.vue';
import type { ChatMessage } from '@/composables/useChat';

const t = useAiI18n();
const { isOpen, toggle } = useEmbedMode('sidebar');

const props = defineProps<{
  messages: readonly ChatMessage[];
  isStreaming?: boolean;
  inputText?: string;
  /** Panel width. */
  width?: string;
  /** External open state control (for imperative API). */
  open?: boolean;
}>();

const emit = defineEmits<{
  send: [content: string, files: File[]];
  stop: [];
  'update:inputText': [value: string];
  'update:open': [value: boolean];
}>();

// Sync external prop → internal state
watch(() => props.open, (v) => {
  if (v != null && v !== isOpen.value) {
    isOpen.value = v;
  }
});

// Sync internal state → external prop
watch(isOpen, (v) => {
  emit('update:open', v);
});
</script>

<template>
  <!-- Overlay -->
  <Transition
    enter-active-class="transition-opacity duration-200"
    enter-from-class="opacity-0"
    leave-active-class="transition-opacity duration-200"
    leave-to-class="opacity-0"
  >
    <div
      v-if="isOpen"
      class="fixed inset-0 z-40 bg-black/20"
      @click="toggle()"
    />
  </Transition>

  <!-- Panel -->
  <Transition
    enter-active-class="transition-transform duration-300 ease-out"
    enter-from-class="translate-x-full"
    leave-active-class="transition-transform duration-200 ease-in"
    leave-to-class="translate-x-full"
  >
    <div
      v-if="isOpen"
      class="fixed top-0 right-0 bottom-0 z-50 flex flex-col border-l bg-background shadow-xl"
      :style="{ width: width ?? '380px' }"
    >
      <!-- Header -->
      <div class="flex items-center justify-between border-b px-4 py-2">
        <slot name="header">
          <span class="text-sm font-medium">AI Chat</span>
        </slot>
        <Button variant="ghost" size="icon-sm" @click="toggle()">
          <Icon icon="lucide:x" class="size-4" />
        </Button>
      </div>

      <!-- Chat -->
      <ChatBox
        :messages="messages"
        :is-streaming="isStreaming"
        :input-text="inputText"
        class="flex-1"
        @send="(content: string, files: File[]) => emit('send', content, files)"
        @stop="emit('stop')"
        @update:input-text="emit('update:inputText', $event)"
      />
    </div>
  </Transition>
</template>
