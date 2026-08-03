<script setup lang="ts">
/**
 * TReasoning - Collapsible thinking/reasoning process
 *
 * Displays AI reasoning content (e.g., DeepSeek-R1 thinking)
 * with auto-open/close behavior tied to streaming state.
 * Tracks thinking duration and shows elapsed time when done.
 */

import { ref, watch, onBeforeUnmount } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
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
  <div class="t-reasoning">
    <!-- Trigger -->
    <button
      type="button"
      class="t-reasoning__trigger"
      @click="handleOpenChange(!isOpen)"
    >
      <Icon icon="lucide:brain" class="size-4 shrink-0" />

      <template v-if="isStreaming">
        <TShimmer class="t-reasoning__title">{{ t.reasoning.thinking }}</TShimmer>
      </template>
      <template v-else>
        <span class="t-reasoning__title">
          {{ elapsedSeconds > 0 ? t.reasoning.thoughtFor.replace('{seconds}', String(elapsedSeconds)) : t.reasoning.thinking }}
        </span>
      </template>

      <Icon
        icon="lucide:chevron-down"
        class="ml-auto size-4 shrink-0 t-reasoning__chevron"
        :class="{ 't-reasoning__chevron--open': isOpen }"
      />
    </button>

    <!-- Content -->
    <div v-show="isOpen" class="t-reasoning__content">
      <div class="px-3 py-2">
        <TStreamMarkdown
          :content="content"
          :streaming="isStreaming"
          class="text-sm t-reasoning__body"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-reasoning {
  border-radius: 8px;
  border: 1px solid color-mix(in srgb, var(--tnzi-border) 50%, transparent);
  background-color: var(--tnzi-ai-reasoning-bg);
  overflow: hidden;
}
.t-reasoning__trigger {
  display: flex;
  width: 100%;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  font-size: 14px;
  color: var(--tnzi-base-text-muted);
  background: none;
  border: none;
  cursor: pointer;
  transition: background-color 0.15s;
}
.t-reasoning__trigger:hover { background-color: var(--tnzi-hover-bg, rgba(0,0,0,0.04)); }
.t-reasoning__title { font-weight: 500; color: var(--tnzi-base-text); }
.t-reasoning__chevron { transition: transform 0.2s; }
.t-reasoning__chevron--open { transform: rotate(180deg); }
.t-reasoning__content {
  border-top: 1px solid color-mix(in srgb, var(--tnzi-border) 30%, transparent);
}
.t-reasoning__body { color: var(--tnzi-base-text-muted); }
</style>
