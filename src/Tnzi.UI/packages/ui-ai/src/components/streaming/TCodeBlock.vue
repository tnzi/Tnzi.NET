<script setup lang="ts">
/**
 * TCodeBlock — Code syntax highlighting with copy button
 *
 * Uses shiki for async highlighting with dual light/dark theme support.
 * Falls back to plain <pre><code> while shiki loads.
 */

import { NButton } from 'naive-ui';
import { ref, watch, onMounted, onBeforeUnmount } from 'vue';
import { Icon } from '@iconify/vue';

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

const highlightedHtml = ref('');
const isLoaded = ref(false);
const isCopied = ref(false);

let copyTimeout: ReturnType<typeof setTimeout> | null = null;
let highlightDebounce: ReturnType<typeof setTimeout> | null = null;

async function highlight(): Promise<void> {
  if (!props.code) {
    highlightedHtml.value = '';
    return;
  }

  try {
    const { codeToHtml } = await import('shiki');
    const html = await codeToHtml(props.code, {
      lang: props.language || 'text',
      themes: {
        light: 'github-light',
        dark: 'github-dark',
      },
    });
    highlightedHtml.value = html;
    isLoaded.value = true;
  } catch {
    // shiki load failed — keep fallback
    isLoaded.value = false;
  }
}

function handleCopy(): void {
  navigator.clipboard.writeText(props.code).catch(() => {
    // Clipboard API unavailable — silent fail
  });

  isCopied.value = true;
  if (copyTimeout) clearTimeout(copyTimeout);
  copyTimeout = setTimeout(() => {
    isCopied.value = false;
  }, 2000);
}

onMounted(highlight);

watch(() => [props.code, props.language], () => {
  if (highlightDebounce) clearTimeout(highlightDebounce);
  highlightDebounce = setTimeout(highlight, 150);
});

onBeforeUnmount(() => {
  if (copyTimeout) clearTimeout(copyTimeout);
  if (highlightDebounce) clearTimeout(highlightDebounce);
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
