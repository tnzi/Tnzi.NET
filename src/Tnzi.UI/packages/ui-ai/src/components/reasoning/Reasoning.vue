<script setup lang="ts">
/**
 * Reasoning — Collapsible thinking/reasoning process
 *
 * Displays AI reasoning content (e.g., DeepSeek-R1 thinking)
 * with auto-open/close behavior tied to streaming state.
 * Tracks thinking duration and shows elapsed time when done.
 */

import { ref, watch, onBeforeUnmount } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { useAiI18n } from '@/locale/index';
import Shimmer from '../streaming/Shimmer.vue';
import StreamMarkdown from '../streaming/StreamMarkdown.vue';

const t = useAiI18n();

const props = withDefaults(defineProps<{
  /** Reasoning/thinking content (markdown). */
  content: string;
  isStreaming?: boolean;
  defaultOpen?: boolean;
}>(), {
  isStreaming: false,
  defaultOpen: false,
});

const emit = defineEmits<{
  toggle: [open: boolean];
}>();

const isOpen = ref(props.defaultOpen);

/** Duration tracking */
let startTime: number | null = null;
const elapsedSeconds = ref(0);
let timerInterval: ReturnType<typeof setInterval> | null = null;
let autoCloseTimeout: ReturnType<typeof setTimeout> | null = null;

function startTimer(): void {
  if (timerInterval) return;
  startTime = Date.now();
  timerInterval = setInterval(() => {
    if (startTime) {
      elapsedSeconds.value = Math.round((Date.now() - startTime) / 1000);
    }
  }, 1000);
}

function stopTimer(): void {
  if (timerInterval) {
    clearInterval(timerInterval);
    timerInterval = null;
  }
}

function handleOpenChange(open: boolean): void {
  isOpen.value = open;
  emit('toggle', open);
}

// Auto-open when streaming starts, auto-close 1s after streaming ends
watch(
  () => props.isStreaming,
  (streaming) => {
    if (streaming) {
      isOpen.value = true;
      startTimer();
    } else {
      stopTimer();
      if (autoCloseTimeout) clearTimeout(autoCloseTimeout);
      autoCloseTimeout = setTimeout(() => {
        isOpen.value = false;
        autoCloseTimeout = null;
      }, 1000);
    }
  },
  { immediate: true },
);

onBeforeUnmount(() => {
  stopTimer();
  if (autoCloseTimeout) clearTimeout(autoCloseTimeout);
});
</script>

<template>
  <div class="rounded-lg border border-border/50 bg-ai-reasoning-bg overflow-hidden">
    <!-- Trigger -->
    <button
      type="button"
      class="flex w-full items-center gap-2 px-3 py-2 text-sm text-muted-foreground hover:bg-accent/50 transition-colors"
      @click="handleOpenChange(!isOpen)"
    >
      <Icon icon="lucide:brain" class="size-4 shrink-0" />

      <template v-if="isStreaming">
        <Shimmer class="text-foreground font-medium">{{ t.reasoning.thinking }}</Shimmer>
      </template>
      <template v-else>
        <span class="font-medium">
          {{ elapsedSeconds > 0 ? t.reasoning.thoughtFor.replace('{seconds}', String(elapsedSeconds)) : t.reasoning.thinking }}
        </span>
      </template>

      <Icon
        icon="lucide:chevron-down"
        :class="cn('ml-auto size-4 shrink-0 transition-transform', isOpen && 'rotate-180')"
      />
    </button>

    <!-- Content -->
    <div v-show="isOpen" class="border-t border-border/30">
      <div class="px-3 py-2">
        <StreamMarkdown
          :content="content"
          :streaming="isStreaming"
          class="text-sm text-muted-foreground"
        />
      </div>
    </div>
  </div>
</template>
