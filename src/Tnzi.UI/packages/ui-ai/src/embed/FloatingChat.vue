<script setup lang="ts">
/**
 * FloatingChat — Bottom-right bubble that expands to a chat window
 *
 * Fixed positioned. Uses useEmbedMode('floating') for open/close/minimize state.
 * Slot: #trigger for custom button.
 */

import { NButton } from 'naive-ui';
import { watch } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { useEmbedMode } from '@/composables/useEmbedMode';
import ChatBox from '@/components/chat/ChatBox.vue';
import type { ChatMessage } from '@/composables/useChat';

const t = useAiI18n();
const { isOpen, toggle, minimize, isMinimized, expand } = useEmbedMode('floating');

const props = defineProps<{
  messages: readonly ChatMessage[];
  isStreaming?: boolean;
  inputText?: string;
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
  <div class="t-floating-chat">
    <!-- Expanded chat window -->
    <Transition
      enter-active-class="transition-all duration-300 ease-out"
      enter-from-class="opacity-0 scale-95 translate-y-4"
      enter-to-class="opacity-100 scale-100 translate-y-0"
      leave-active-class="transition-all duration-200 ease-in"
      leave-from-class="opacity-100 scale-100 translate-y-0"
      leave-to-class="opacity-0 scale-95 translate-y-4"
    >
      <div
        v-if="isOpen && !isMinimized"
        class="t-floating-chat__window"
      >
        <!-- Header -->
        <div class="t-floating-chat__header">
          <span class="text-sm font-medium">AI Chat</span>
          <div class="flex items-center gap-1">
            <NButton text size="small" @click="minimize()">
              <template #icon><Icon icon="lucide:minimize-2" class="size-3.5" /></template>
            </NButton>
            <NButton text size="small" @click="toggle()">
              <template #icon><Icon icon="lucide:x" class="size-3.5" /></template>
            </NButton>
          </div>
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

    <!-- Minimized bar -->
    <div
      v-if="isOpen && isMinimized"
      class="t-floating-chat__minimized"
      @click="expand()"
    >
      <Icon icon="lucide:message-circle" class="size-4 t-floating-chat__primary-icon" />
      <span class="text-sm">AI Chat</span>
      <NButton text size="small" class="size-5" @click.stop="toggle()">
        <template #icon><Icon icon="lucide:x" class="size-3" /></template>
      </NButton>
    </div>

    <!-- Trigger button -->
    <slot name="trigger">
      <NButton
        type="primary"
        round
        class="t-floating-chat__fab"
        @click="toggle()"
      >
        <template #icon>
          <Icon
            :icon="isOpen ? 'lucide:x' : 'lucide:message-circle'"
            class="size-5"
          />
        </template>
      </NButton>
    </slot>
  </div>
</template>

<style scoped>
.t-floating-chat {
  position: fixed;
  bottom: 16px;
  right: 16px;
  z-index: 50;
}
.t-floating-chat__window {
  margin-bottom: 12px;
  width: 380px;
  height: 560px;
  border-radius: 16px;
  border: 1px solid var(--tnzi-border);
  background-color: var(--tnzi-container-bg);
  box-shadow: 0 25px 50px rgba(0, 0, 0, 0.25);
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
.t-floating-chat__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--tnzi-border);
  padding: 8px 16px;
}
.t-floating-chat__minimized {
  margin-bottom: 12px;
  display: flex;
  align-items: center;
  gap: 8px;
  border-radius: 999px;
  border: 1px solid var(--tnzi-border);
  background-color: var(--tnzi-container-bg);
  padding: 8px 16px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
  cursor: pointer;
}
.t-floating-chat__primary-icon { color: var(--tnzi-primary); }
.t-floating-chat__fab {
  width: 48px;
  height: 48px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}
</style>
