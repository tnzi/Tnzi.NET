<script setup lang="ts">
/**
 * TReasoning — Collapsible thinking/reasoning process
 *
 * Displays AI reasoning content (e.g., DeepSeek-R1 thinking)
 * with auto-open/close behavior tied to streaming state.
 * Tracks thinking duration and shows elapsed time when done.
 */

import { ref, watch, onBeforeUnmount } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { useAiI18n } from '@/locale/index';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/primitives/ui/collapsible';
import TShimmer from '../streaming/TShimmer.vue';
import TStreamMarkdown from '../streaming/TStreamMarkdown.vue';

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
  <Collapsible
    v-model:open="isOpen"
    class="rounded-lg border border-border/50 bg-ai-reasoning-bg overflow-hidden"
    @update:open="handleOpenChange"
  >
    <!-- Trigger -->
    <CollapsibleTrigger as-child>
      <button
        type="button"
        class="flex w-full items-center gap-2 px-3 py-2 text-sm text-muted-foreground hover:bg-accent/50 transition-colors"
      >
        <Icon icon="lucide:brain" class="size-4 shrink-0" />

        <template v-if="isStreaming">
          <TShimmer class="text-foreground font-medium">{{ t.reasoning.thinking }}</TShimmer>
        </template>
        <template v-else>
          <span class="font-medium">
            {{ t.reasoning.thoughtFor.replace('{seconds}', String(elapsedSeconds)) }}
          </span>
        </template>

        <Icon
          icon="lucide:chevron-down"
          :class="cn('ml-auto size-4 shrink-0 transition-transform', isOpen && 'rotate-180')"
        />
      </button>
    </CollapsibleTrigger>

    <!-- Content -->
    <CollapsibleContent class="border-t border-border/30 data-[state=closed]:animate-out data-[state=open]:animate-in data-[state=open]:slide-in-from-top-2 data-[state=closed]:slide-out-to-top-2 data-[state=closed]:fade-out-0">
      <div class="px-3 py-2">
        <TStreamMarkdown
          :content="content"
          :streaming="isStreaming"
          class="text-sm text-muted-foreground"
        />
      </div>
    </CollapsibleContent>
  </Collapsible>
</template>
