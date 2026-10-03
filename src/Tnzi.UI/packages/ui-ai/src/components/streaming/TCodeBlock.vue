<script setup lang="ts">
/**
 * TCodeBlock - Code syntax highlighting with copy button
 *
 * Uses shiki for async highlighting with dual light/dark theme support.
 * Falls back to plain <pre><code> while shiki loads.
 */

import { NButton } from 'naive-ui';
import { computed, ref, onBeforeUnmount } from 'vue';
import { Icon } from '@iconify/vue';
import { useCodeHighlight } from '../../headless/useCodeHighlight';

const props = withDefaults(defineProps<{
  code: string;
  /** Programming language (e.g., "typescript", "python"). */
  language?: string;
  showLineNumbers?: boolean;
  class?: string;
}>(), {
  language: '',
  showLineNumbers: false,
});

// Re-highlights on a change of the code or the language and drops a pass a
// newer input has superseded, so a late Shiki result never replaces a newer
// one. Debounced because streamed code changes on every token.
const { html: highlightedHtml, error: highlightError } = useCodeHighlight(
  () => props.code,
  () => props.language || 'text',
  { debounceMs: 150 },
);
// Shiki unavailable (or the grammar failed): the plain fallback below.
const isLoaded = computed(() => !highlightError.value);
const isCopied = ref(false);

let copyTimeout: ReturnType<typeof setTimeout> | null = null;

function handleCopy(): void {
  navigator.clipboard.writeText(props.code).catch(() => {
    // Clipboard API unavailable - silent fail
  });

  isCopied.value = true;
  if (copyTimeout) clearTimeout(copyTimeout);
  copyTimeout = setTimeout(() => {
    isCopied.value = false;
  }, 2000);
}

onBeforeUnmount(() => {
  if (copyTimeout) clearTimeout(copyTimeout);
});
</script>

<template>
  <div class="t-code-block" :class="props.class">
    <!-- Header bar -->
    <div class="t-code-block__header">
      <span v-if="props.language" class="t-code-block__lang font-mono">{{ props.language }}</span>
      <span v-else>&nbsp;</span>
      <div class="flex items-center gap-1">
        <slot name="actions" />
        <NButton
          text
          size="small"
          :aria-label="isCopied ? 'Copied' : 'Copy code'"
          @click="handleCopy"
        >
          <template #icon>
            <Icon
              :icon="isCopied ? 'lucide:check' : 'lucide:copy'"
              class="size-3.5"
              :class="{ 't-code-block__copy--done': isCopied }"
            />
          </template>
        </NButton>
      </div>
    </div>

    <!-- Highlighted code -->
    <div
      v-if="isLoaded && highlightedHtml"
      class="t-code-block__highlighted"
      :class="{ 't-code-block__highlighted--line-numbers': showLineNumbers }"
      v-html="highlightedHtml"
    />

    <!-- Fallback (plain text) -->
    <pre
      v-else
      class="t-code-block__fallback"
    ><code :class="language && `language-${language}`">{{ props.code }}</code></pre>
  </div>
</template>

<style scoped>
.t-code-block {
  position: relative;
  border-radius: 8px;
  background-color: var(--tnzi-ai-code-bg);
  overflow: hidden;
}
.t-code-block__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 12px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  border-bottom: 1px solid color-mix(in srgb, var(--tnzi-border) 50%, transparent);
}
.t-code-block__lang { color: var(--tnzi-base-text-muted); }
.t-code-block__copy--done { color: var(--tnzi-ai-node-completed); }
.t-code-block__highlighted {
  overflow-x: auto;
  padding: 12px;
  font-size: 14px;
}
.t-code-block__highlighted :deep(pre) {
  background: transparent !important;
  padding: 0 !important;
  margin: 0 !important;
}
.t-code-block__fallback {
  overflow-x: auto;
  padding: 12px;
  font-size: 14px;
  margin: 0;
}
</style>
