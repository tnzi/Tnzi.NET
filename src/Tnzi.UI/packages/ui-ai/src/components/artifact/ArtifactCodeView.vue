<script setup lang="ts">
/**
 * ArtifactCodeView — Code view using Shiki
 */

import { Button, Tooltip } from '../../primitives';
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
        <Tooltip>
          <template #trigger>
            <Button variant="ghost" size="icon-sm" @click="copyCode">
              <Icon icon="lucide:copy" class="size-3.5" />
            </Button>
          </template>
          {{ t.chat.copy }}
        </Tooltip>
        <Tooltip>
          <template #trigger>
            <Button variant="ghost" size="icon-sm" @click="$emit('download')">
              <Icon icon="lucide:download" class="size-3.5" />
            </Button>
          </template>
          {{ t.artifact.download }}
        </Tooltip>
      </div>
    </div>
    <div class="flex-1 min-h-0 overflow-auto p-4 text-sm [&_pre]:!bg-transparent [&_code]:!bg-transparent" v-html="highlightedHtml" />
  </div>
</template>
