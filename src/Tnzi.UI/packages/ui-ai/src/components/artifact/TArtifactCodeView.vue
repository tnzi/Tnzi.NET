<script setup lang="ts">
/**
 * TArtifactCodeView - Code view using Shiki
 *
 * Copy writes the code to the clipboard and shows a check for a moment; a
 * clipboard that refuses is left to the app's error handler.
 */

import { onBeforeUnmount, ref } from 'vue';
import { useAiI18n } from '../../i18n/index';
import { useCodeHighlight } from '../../headless/useCodeHighlight';
import TIconButton from '../controls/TIconButton.vue';
const t = useAiI18n();

const props = defineProps<{
  code: string;
  language?: string;
  filename?: string;
}>();

defineEmits<{
  download: [];
}>();

// Re-highlights on a change of the code OR the language, and drops a pass a
// newer input has superseded (switching artifacts mid-highlight otherwise
// paints the previous one's code). Debounced for code that streams in.
const { html: highlightedHtml } = useCodeHighlight(
  () => props.code,
  () => props.language ?? 'text',
  { debounceMs: 150 },
);

const COPIED_FEEDBACK_MS = 1500;
const copied = ref(false);
let copiedTimer: ReturnType<typeof setTimeout> | null = null;

async function copyCode(): Promise<void> {
  await navigator.clipboard.writeText(props.code);
  copied.value = true;
  if (copiedTimer != null) clearTimeout(copiedTimer);
  copiedTimer = setTimeout(() => {
    copiedTimer = null;
    copied.value = false;
  }, COPIED_FEEDBACK_MS);
}

onBeforeUnmount(() => {
  if (copiedTimer != null) clearTimeout(copiedTimer);
});
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="t-artifact-code__bar">
      <span v-if="filename" class="t-artifact-code__name t-mono">{{ filename }}</span>
      <span v-else class="t-artifact-code__name">{{ language ?? 'text' }}</span>
      <div class="flex items-center gap-0.5">
        <TIconButton
          :icon="copied ? 'lucide:check' : 'lucide:copy'"
          :label="copied ? t.chat.copied : t.chat.copy"
          size="xs"
          @click="copyCode"
        />
        <TIconButton icon="lucide:download" :label="t.artifact.download" size="xs" @click="$emit('download')" />
      </div>
    </div>
    <div class="t-ai-scroll relative flex-1 min-h-0 overflow-auto p-4 text-sm leading-[var(--tnzi-ai-code-line-height,1.5)] [&_pre]:!bg-transparent [&_code]:!bg-transparent" v-html="highlightedHtml" />
  </div>
</template>

<style scoped>
.t-artifact-code__bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 2px 6px 2px 12px;
  border-bottom: 1px solid var(--tnzi-ai-border);
}
.t-artifact-code__name {
  min-width: 0;
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
