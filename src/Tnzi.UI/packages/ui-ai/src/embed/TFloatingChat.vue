<script setup lang="ts">
/**
 * TFloatingChat - Bottom-right bubble that expands to a chat window
 *
 * Fixed positioned. Uses useEmbedMode('floating') for open/close/minimize state.
 * Slot: #trigger for custom button.
 */

import { watch } from 'vue';
import { Icon } from '@iconify/vue';
import TIconButton from '../components/controls/TIconButton.vue';
import { vTooltip } from '../headless/tooltip';
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
            <TIconButton icon="lucide:minimize-2" size="xs" :label="t.embed.minimize" @click="minimize()" />
            <TIconButton icon="lucide:x" size="xs" :label="t.embed.close" @click="toggle()" />
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

    <!-- Minimized bar: the title is the button that expands the window, the
         cross beside it closes; two sibling buttons, so both are reachable
         from the keyboard. -->
    <div v-if="isOpen && isMinimized" class="t-floating-chat__minimized">
      <button
        v-tooltip="t.embed.expand"
        type="button"
        class="t-floating-chat__expand"
        :aria-label="`${t.embed.expand}: ${t.embed.title}`"
        @click="expand()"
      >
        <Icon icon="lucide:message-circle" class="t-floating-chat__primary-icon" aria-hidden="true" />
        <span>{{ t.embed.title }}</span>
      </button>
      <TIconButton icon="lucide:x" size="xs" :label="t.embed.close" @click="toggle()" />
    </div>

    <!-- Trigger button -->
    <slot name="trigger">
      <button
        v-tooltip="{ text: isOpen ? t.embed.close : t.embed.open, side: 'left' }"
        type="button"
        class="t-floating-chat__fab"
        :aria-label="isOpen ? t.embed.close : t.embed.open"
        :aria-expanded="isOpen"
        @click="toggle()"
      >
        <Icon :icon="isOpen ? 'lucide:x' : 'lucide:message-circle'" aria-hidden="true" />
      </button>
    </slot>
  </div>
</template>

<style scoped>
.t-floating-chat {
  position: fixed;
  bottom: 16px;
  right: 16px;
  z-index: var(--tnzi-ai-z-drawer, 50);
}
.t-floating-chat__window {
  margin-bottom: 12px;
  /* A phone keeps 16px of page on either side. */
  width: min(380px, calc(100vw - 32px));
  height: 560px;
  /* On a short viewport the window shrinks rather than rising into the page's
     top inset (`--tnzi-overlay-inset-top`) or off the top: 96px is the
     launcher below it (16 + 48 + 12) plus a 20px gap at the top. */
  max-height: calc(100vh - var(--tnzi-overlay-inset-top, 0px) - 96px);
  border-radius: 16px;
  border: 1px solid var(--tnzi-ai-border);
  background-color: var(--tnzi-ai-surface);
  box-shadow: 0 25px 50px rgba(0, 0, 0, 0.25);
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
.t-floating-chat__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--tnzi-ai-border);
  padding: 6px 8px 6px 16px;
  color: var(--tnzi-ai-text);
}
.t-floating-chat__minimized {
  margin-bottom: 12px;
  display: flex;
  align-items: center;
  gap: 4px;
  border-radius: 999px;
  border: 1px solid var(--tnzi-ai-border);
  background-color: var(--tnzi-ai-surface);
  padding: 3px 4px 3px 3px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}
.t-floating-chat__expand {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  height: 28px;
  padding: 0 8px 0 10px;
  border: none;
  border-radius: 999px;
  background: transparent;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 14px;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-floating-chat__expand:hover { background: var(--tnzi-ai-hover); }
.t-floating-chat__primary-icon {
  font-size: 16px;
  color: var(--tnzi-ai-accent-text);
}
.t-floating-chat__fab {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  margin-left: auto;
  border: none;
  border-radius: 999px;
  background: var(--tnzi-ai-accent);
  color: var(--tnzi-ai-on-accent);
  font-size: 20px;
  cursor: pointer;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  transition: transform var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-floating-chat__fab:hover { transform: translateY(-1px); }
.t-floating-chat__expand:focus-visible,
.t-floating-chat__fab:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
</style>
