<script setup lang="ts">
/**
 * @experimental
 * TToolOutput - text a tool printed, inside a tool step: monospace on an inset well by
 * default (command output, a file's lines), or as prose (`mono: false`, a note the tool
 * wrote for a reader). Past `foldLines` lines it shows the first ones and a toggle for
 * the rest, so one long result does not push the answer off the screen.
 */
import { computed, ref } from 'vue'
import { useAiI18n, formatAiMessage } from '../../i18n/index'

const props = withDefaults(
  defineProps<{
    text: string
    /** Monospace on an inset well (default), or prose. */
    mono?: boolean
    /** Lines shown before the rest folds away; 0 shows everything. */
    foldLines?: number
    /** The text is an error message: drawn in the danger colour. */
    error?: boolean
  }>(),
  { mono: true, foldLines: 12, error: false },
)

const t = useAiI18n()
const expanded = ref(false)

const lines = computed(() => props.text.replace(/\s+$/, '').split('\n'))
const foldable = computed(() => props.foldLines > 0 && lines.value.length > props.foldLines)
const shown = computed(() =>
  foldable.value && !expanded.value ? lines.value.slice(0, props.foldLines).join('\n') : lines.value.join('\n'),
)
const toggleLabel = computed(() =>
  expanded.value ? t.value.tools.showLess : formatAiMessage(t.value.tools.showAll, { count: lines.value.length }),
)
</script>

<template>
  <div class="t-tool-output" :class="{ 'is-mono': mono, 'is-error': error }">
    <pre v-if="mono" class="t-tool-output__text t-ai-scroll">{{ shown }}</pre>
    <p v-else class="t-tool-output__text">{{ shown }}</p>
    <button
      v-if="foldable"
      type="button"
      class="t-tool-output__toggle"
      :aria-expanded="expanded"
      @click="expanded = !expanded"
    >
      {{ toggleLabel }}
    </button>
  </div>
</template>

<style scoped>
.t-tool-output {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  min-width: 0;
}
.t-tool-output__text {
  align-self: stretch;
  margin: 0;
  min-width: 0;
  font-size: 13px;
  line-height: 1.55;
  color: var(--tnzi-ai-text);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.t-tool-output.is-mono .t-tool-output__text {
  max-height: 480px;
  overflow: auto;
  position: relative;
  padding: 8px 10px;
  border-radius: 8px;
  background: var(--tnzi-ai-code-bg);
  font-family: var(--tnzi-ai-font-mono);
  font-size: 12px;
  line-height: var(--tnzi-ai-code-line-height, 1.5);
}
.t-tool-output.is-error .t-tool-output__text { color: var(--tnzi-ai-danger); }
.t-tool-output__toggle {
  padding: 2px 0;
  border: none;
  border-radius: 4px;
  background: transparent;
  color: var(--tnzi-ai-accent-text);
  font: inherit;
  font-size: 12px;
  cursor: pointer;
}
.t-tool-output__toggle:hover { text-decoration: underline; }
.t-tool-output__toggle:focus-visible {
  outline: 2px solid var(--tnzi-ai-accent);
  outline-offset: 2px;
}
</style>
