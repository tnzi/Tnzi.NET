<script setup lang="ts">
/**
 * @experimental
 * TWebFetchCall - the Claude Code CLI's `WebFetch`, as a tool step: "Read <site>",
 * opening on the page's address (a link to it, in a new tab) and what the CLI made of
 * the page, folded.
 *
 * Registered by default for the tool name `WebFetch` (see `BUILT_IN_TOOL_RENDERERS`);
 * a product's own renderer of that name replaces it.
 */
import { computed } from 'vue'
import { useAiI18n } from '../../i18n/index'
import type { ToolRendererProps } from '../chat/chat-render-context'
import TToolStep from './TToolStep.vue'
import TToolLinkList from './TToolLinkList.vue'
import TToolOutput from './TToolOutput.vue'
import { toolCallState } from './tool-call-status'
import { toolResultText } from './tool-views'
import { inputString, webFetchTarget } from './web-tools'

const props = defineProps<ToolRendererProps>()

const t = useAiI18n()

const state = computed(() => toolCallState(props.toolCall, props.streaming))
const page = computed(() => webFetchTarget(props.toolCall.input))
/* An address that is not a web URL is still what was asked for: said as it is. */
const subject = computed(() => page.value?.domain ?? inputString(props.toolCall.input, 'url'))
const label = computed(() => {
  if (state.value === 'failed') return t.value.tools.webFetchFailed
  return state.value === 'completed' ? t.value.tools.webFetchDone : t.value.tools.webFetchRunning
})
const error = computed(() => (state.value === 'failed' ? props.toolCall.error ?? null : null))
const text = computed(() => (state.value === 'failed' ? '' : toolResultText(props.toolCall).trim()))
</script>

<template>
  <TToolStep
    class="t-web-fetch"
    :state="state"
    :label="label"
    :subject="subject"
    icon="lucide:file-text"
    :expandable="!!page || !!text || !!error"
  >
    <TToolLinkList v-if="page" :links="[page]" :show-domain="false" />
    <TToolOutput v-if="text" :text="text" :mono="false" :fold-lines="6" />
    <TToolOutput v-if="error" :text="error" :mono="false" error />
  </TToolStep>
</template>
