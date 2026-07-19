<script setup lang="ts">
/**
 * TSidebarChat - Right-side slide-in panel with overlay
 *
 * Fixed positioned. Uses useEmbedMode('sidebar').
 * Slot: #header for custom header content.
 */

import { NButton } from 'naive-ui';
import { watch } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { useEmbedMode } from '@/composables/useEmbedMode';
import TChatBox from '@/components/chat/TChatBox.vue';
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
      class="t-sidebar-chat__overlay"
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
      class="t-sidebar-chat__panel"
      :style="{ width: width ?? '380px' }"
    >
      <!-- Header -->
      <div class="t-sidebar-chat__header">
        <slot name="header">
          <span class="text-sm font-medium">AI Chat</span>
        </slot>
        <NButton text size="small" @click="toggle()">
          <template #icon><Icon icon="lucide:x" class="size-4" /></template>
        </NButton>
      </div>

      <!-- Chat -->
      <TChatBox
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

<style scoped>
.t-sidebar-chat__overlay {
  position: fixed;
  inset: 0;
  z-index: 40;
  background-color: rgba(0, 0, 0, 0.2);
}
.t-sidebar-chat__panel {
  position: fixed;
  top: 0;
  right: 0;
  bottom: 0;
  z-index: 50;
  display: flex;
  flex-direction: column;
  border-left: 1px solid var(--tnzi-border);
  background-color: var(--tnzi-container-bg);
  box-shadow: -4px 0 20px rgba(0, 0, 0, 0.1);
}
.t-sidebar-chat__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--tnzi-border);
  padding: 8px 16px;
}
</style>
