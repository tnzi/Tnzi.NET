<script setup lang="ts">
/**
 * ArtifactCodeView — Code view using Shiki
 */

import { NButton, NTooltip } from 'naive-ui';
import { ref, watch, onBeforeUnmount } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
const t = useAiI18n();

const props = defineProps<{
  code: string;
  language?: string;
  filename?: string;
}>();

defineEmits<{
  download: [];
}>();

const highlightedHtml = ref('');

async function highlight(): Promise<void> {
  try {
    const { codeToHtml } = await import('shiki');
    highlightedHtml.value = await codeToHtml(props.code, {
      lang: props.language ?? 'text',
      themes: { light: 'github-light', dark: 'github-dark' },
    });
  } catch {
    highlightedHtml.value = `<pre><code>${props.code.replace(/</g, '&lt;')}</code></pre>`;
  }
}

let debounceTimer: ReturnType<typeof setTimeout> | null = null;

watch(() => props.code, () => {
  if (debounceTimer) clearTimeout(debounceTimer);
  debounceTimer = setTimeout(highlight, 150);
}, { immediate: true });

onBeforeUnmount(() => {
  if (debounceTimer) clearTimeout(debounceTimer);
});

async function copyCode(): Promise<void> {
  await navigator.clipboard.writeText(props.code);
}
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center justify-between border-b px-3 py-1.5">
      <span v-if="filename" class="text-xs font-mono text-muted-foreground">{{ filename }}</span>
      <span v-else class="text-xs text-muted-foreground">{{ language ?? 'text' }}</span>
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
