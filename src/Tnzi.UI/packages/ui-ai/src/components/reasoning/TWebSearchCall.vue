<script setup lang="ts">
/**
 * @experimental
 * TWebSearchCall - the Claude Code CLI's `WebSearch`, as a tool step: "Searched the web
 * <query>", opening on the pages it found (title and site, each opening in a new tab).
 *
 * Registered by default for the tool name `WebSearch` (see `BUILT_IN_TOOL_RENDERERS`);
 * a product's own renderer of that name replaces it. The result is the CLI's text,
 * read by `parseWebSearchResult`; when it lists no pages the step shows what the text
 * says instead.
 */
import { computed } from 'vue'
import { useAiI18n } from '../../i18n/index'
import type { ToolRendererProps } from '../chat/chat-render-context'
import TToolStep from './TToolStep.vue'
import TToolLinkList from './TToolLinkList.vue'
import TToolOutput from './TToolOutput.vue'
import { toolCallState } from './tool-call-status'
import { toolResultText } from './tool-views'
import { inputString, parseWebSearchResult } from './web-tools'

const props = defineProps<ToolRendererProps>()

const t = useAiI18n()

const state = computed(() => toolCallState(props.toolCall, props.streaming))
const result = computed(() => parseWebSearchResult(toolResultText(props.toolCall)))
const query = computed(() => inputString(props.toolCall.input, 'query') ?? result.value.query)
const label = computed(() => {
  if (state.value === 'failed') return t.value.tools.webSearchFailed
  return state.value === 'completed' ? t.value.tools.webSearchDone : t.value.tools.webSearchRunning
})
const error = computed(() => (state.value === 'failed' ? props.toolCall.error ?? null : null))
/* The search model's notes are for the agent; they are shown only when there is no
   list of pages to show instead (an error the CLI reported as text, say). */
const note = computed(() => (result.value.links.length ? null : result.value.text))
const nothingFound = computed(
  () => state.value === 'completed' && !result.value.links.length && !note.value,
)
</script>

<template>
  <TToolStep
    class="t-web-search"
    :state="state"
    :label="label"
    :subject="query"
    icon="lucide:globe"
    :expandable="result.links.length > 0 || !!note || !!error || nothingFound"
  >
    <TToolLinkList v-if="result.links.length" :links="result.links" :label="t.tools.results" />
    <TToolOutput v-else-if="note" :text="note" :mono="false" :fold-lines="6" />
    <p v-else-if="nothingFound" class="t-web-search__empty">{{ t.tools.noResults }}</p>
    <TToolOutput v-if="error" :text="error" :mono="false" error />
  </TToolStep>
</template>

<style scoped>
.t-web-search__empty {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-ai-text-tertiary);
}
</style>
