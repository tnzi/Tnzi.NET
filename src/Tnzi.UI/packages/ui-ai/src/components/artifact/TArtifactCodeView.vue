<script setup lang="ts">
/**
 * TArtifactCodeView - Code view using Shiki
 */

import { NButton, NTooltip } from 'naive-ui';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import { useCodeHighlight } from '../../headless/useCodeHighlight';
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

async function copyCode(): Promise<void> {
  await navigator.clipboard.writeText(props.code);
}
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center justify-between border-b px-3 py-1.5">
      <span v-if="filename" class="text-xs font-mono text-tnzi-muted">{{ filename }}</span>
      <span v-else class="text-xs text-tnzi-muted">{{ language ?? 'text' }}</span>
      <div class="flex items-center gap-1">
        <NTooltip>
          <template #trigger>
            <NButton quaternary size="tiny" @click="copyCode">
              <template #icon><Icon icon="lucide:copy" /></template>
            </NButton>
          </template>
          {{ t.chat.copy }}
        </NTooltip>
        <NTooltip>
          <template #trigger>
            <NButton quaternary size="tiny" @click="$emit('download')">
              <template #icon><Icon icon="lucide:download" /></template>
            </NButton>
          </template>
          {{ t.artifact.download }}
        </NTooltip>
      </div>
    </div>
    <div class="flex-1 min-h-0 overflow-auto p-4 text-sm [&_pre]:!bg-transparent [&_code]:!bg-transparent" v-html="highlightedHtml" />
  </div>
</template>
