<script setup lang="ts">
/**
 * TFloatingChat - Bottom-right bubble that expands to a chat window
 *
 * Fixed positioned. Uses useEmbedMode('floating') for open/close/minimize state.
 * Slot: #trigger for custom button.
 */

import { NButton } from 'naive-ui';
import { watch } from 'vue';
import { Icon } from '@iconify/vue';
import { useEmbedMode } from '../headless/useEmbedMode';
import TChatBox from '../components/chat/TChatBox.vue';
import type { ChatMessage } from '../headless/useChat';
import { useAiI18n } from '../i18n/index';

const t = useAiI18n();

const { isOpen, toggle, minimize, isMinimized, expand } = useEmbedMode('floating');

const props = withDefaults(
  defineProps<{
    messages: readonly ChatMessage[];
    isStreaming?: boolean;
    inputText?: string;
    /** Paperclip + drag/drop + paste. Default true; set false when the
     *  transport cannot carry files (BYO `onSend` that ignores them). */
    enableAttachments?: boolean;
    /** External open state control (for imperative API). */
    open?: boolean;
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
          <span class="text-sm font-medium">{{ t.embed.title }}</span>
          <div class="flex items-center gap-1">
            <NButton text size="small" :aria-label="t.embed.minimize" @click="minimize()">
              <template #icon><Icon icon="lucide:minimize-2" class="size-3.5" /></template>
            </NButton>
            <NButton text size="small" :aria-label="t.embed.close" @click="toggle()">
              <template #icon><Icon icon="lucide:x" class="size-3.5" /></template>
            </NButton>
          </div>
        </div>

        <!-- Chat -->
        <TChatBox
          :messages="messages"
          :is-streaming="isStreaming"
          :input-text="inputText"
          :enable-attachments="enableAttachments"
          class="flex-1"
          @send="(content: string, files: File[]) => emit('send', content, files)"
          @stop="emit('stop')"
        @regenerate="emit('regenerate', $event)"
          @update:input-text="emit('update:inputText', $event)"
        />
      </div>
    </Transition>

    <!-- Minimized bar -->
    <div
      v-if="isOpen && isMinimized"
      class="t-floating-chat__minimized"
      :title="t.embed.expand"
      @click="expand()"
    >
      <Icon icon="lucide:message-circle" class="size-4 t-floating-chat__primary-icon" />
      <span class="text-sm">{{ t.embed.title }}</span>
      <NButton text size="small" class="size-5" :aria-label="t.embed.close" @click.stop="toggle()">
        <template #icon><Icon icon="lucide:x" class="size-3" /></template>
      </NButton>
    </div>

    <!-- Trigger button -->
    <slot name="trigger">
      <NButton
        type="primary"
        round
        class="t-floating-chat__fab"
        :aria-label="isOpen ? t.embed.close : t.embed.open"
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
  /* On a short viewport the window shrinks rather than rising into the page's
     top inset (`--tnzi-overlay-inset-top`) or off the top: 96px is the
     launcher below it (16 + 48 + 12) plus a 20px gap at the top. */
  max-height: calc(100vh - var(--tnzi-overlay-inset-top, 0px) - 96px);
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
.t-floating-chat__primary-icon { color: var(--tnzi-ai-accent-text); }
.t-floating-chat__fab {
  width: 48px;
  height: 48px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}
</style>
