<script setup lang="ts">
/**
 * TMathBlock — Math formula rendering
 *
 * Uses KaTeX to render LaTeX math expressions.
 * Falls back to raw expression on parse error.
 */

import { ref, watch, onMounted } from 'vue';
import { cn } from '@/lib/utils';

const props = withDefaults(defineProps<{
  expression: string;
  /** Block (centered, large) or inline mode. */
  display?: boolean;
  class?: string;
}>(), {
  display: false,
});

const renderedHtml = ref('');
const hasError = ref(false);

async function render(): Promise<void> {
  if (!props.expression) {
    renderedHtml.value = '';
    hasError.value = false;
    return;
  }

  try {
    const katex = await import('katex');
    renderedHtml.value = katex.default.renderToString(props.expression, {
      displayMode: props.display,
      throwOnError: false,
      output: 'htmlAndMathml',
    });
    hasError.value = false;
  } catch {
    renderedHtml.value = '';
    hasError.value = true;
  }
}

onMounted(render);
watch(() => [props.expression, props.display], render);
</script>

<template>
  <span
    v-if="!hasError && renderedHtml"
    :class="cn(display ? 'block text-center my-4' : 'inline', props.class)"
    v-html="renderedHtml"
  />
  <code
    v-else
    :class="cn('text-sm text-muted-foreground font-mono', props.class)"
  >{{ props.expression }}</code>
</template>
