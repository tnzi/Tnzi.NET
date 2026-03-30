<script setup lang="ts">
/**
 * FloatingChat — Bottom-right bubble that expands to a chat window
 *
 * Fixed positioned. Uses useEmbedMode('floating') for open/close/minimize state.
 * Slot: #trigger for custom button.
 */

import { watch } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { useEmbedMode } from '@/composables/useEmbedMode';
import { Button } from '@/primitives/ui/button';
import TChatBox from '@/components/chat/TChatBox.vue';
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
  <div class="fixed bottom-4 right-4 z-50">
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
        class="mb-3 w-[380px] h-[560px] rounded-2xl border bg-background shadow-2xl overflow-hidden flex flex-col"
      >
        <!-- Header -->
        <div class="flex items-center justify-between border-b px-4 py-2">
          <span class="text-sm font-medium">AI Chat</span>
          <div class="flex items-center gap-1">
            <Button variant="ghost" size="icon-sm" @click="minimize()">
              <Icon icon="lucide:minimize-2" class="size-3.5" />
            </Button>
            <Button variant="ghost" size="icon-sm" @click="toggle()">
              <Icon icon="lucide:x" class="size-3.5" />
            </Button>
          </div>
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

    <!-- Minimized bar -->
    <div
      v-if="isOpen && isMinimized"
      class="mb-3 flex items-center gap-2 rounded-full border bg-background px-4 py-2 shadow-lg cursor-pointer"
      @click="expand()"
    >
      <Icon icon="lucide:message-circle" class="size-4 text-primary" />
      <span class="text-sm">AI Chat</span>
      <Button variant="ghost" size="icon-sm" class="size-5" @click.stop="toggle()">
        <Icon icon="lucide:x" class="size-3" />
      </Button>
    </div>

    <!-- Trigger button -->
    <slot name="trigger">
      <Button
        size="icon"
        class="size-12 rounded-full shadow-lg"
        @click="toggle()"
      >
        <Icon
          :icon="isOpen ? 'lucide:x' : 'lucide:message-circle'"
          class="size-5"
        />
      </Button>
    </slot>
  </div>
</template>
