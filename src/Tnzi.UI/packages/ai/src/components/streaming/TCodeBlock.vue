<script setup lang="ts">
/**
 * TCodeBlock — Code syntax highlighting with copy button
 *
 * Uses shiki for async highlighting with dual light/dark theme support.
 * Falls back to plain <pre><code> while shiki loads.
 */

import { ref, watch, onMounted, onBeforeUnmount } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { Button } from '@/primitives/ui/button';

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
  <div :class="cn('group relative rounded-lg bg-ai-code-bg overflow-hidden', props.class)">
    <!-- Header bar -->
    <div class="flex items-center justify-between px-3 py-1.5 text-xs text-muted-foreground border-b border-border/50">
      <span v-if="props.language" class="font-mono">{{ props.language }}</span>
      <span v-else>&nbsp;</span>
      <div class="flex items-center gap-1">
        <slot name="actions" />
        <Button
          variant="ghost"
          size="sm"
          class="h-auto px-1.5 py-0.5"
          :aria-label="isCopied ? 'Copied' : 'Copy code'"
          @click="handleCopy"
        >
          <Icon
            :icon="isCopied ? 'lucide:check' : 'lucide:copy'"
            :class="cn('size-3.5', isCopied && 'text-ai-node-completed')"
          />
        </Button>
      </div>
    </div>

    <!-- Highlighted code -->
    <div
      v-if="isLoaded && highlightedHtml"
      class="overflow-x-auto p-3 text-sm [&>pre]:!bg-transparent [&>pre]:!p-0 [&>pre]:!m-0"
      :class="showLineNumbers && '[&_.line]:before:content-[counter(line)] [&_.line]:before:counter-increment-[line] [&_.line]:before:mr-4 [&_.line]:before:text-muted-foreground/50 [&_.line]:before:text-right [&_.line]:before:inline-block [&_.line]:before:w-4 [&>pre]:counter-reset-[line]'"
      v-html="highlightedHtml"
    />

    <!-- Fallback (plain text) -->
    <pre
      v-else
      class="overflow-x-auto p-3 text-sm"
    ><code :class="language && `language-${language}`">{{ props.code }}</code></pre>
  </div>
</template>
