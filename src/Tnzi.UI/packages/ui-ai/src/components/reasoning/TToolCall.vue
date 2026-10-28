<script setup lang="ts">
/**
 * @experimental
 * TToolCall - renders one tool call with the renderer the product registered for its tool
 * name, else with the view this package ships for it (`BUILT_IN_TOOL_RENDERERS`: the
 * Claude Code CLI's `WebSearch` and `WebFetch`), else with the generic card
 * (`TToolCallCard`).
 *
 * Renderers come from the render context (`TChatApp`'s `toolRenderers`, or
 * `defineChatApp({ toolRenderers })` for the whole app) and receive `ToolRendererProps`
 * (`toolCall`, `streaming`). See `resolveToolRenderer` for how names match. Several
 * calls in a row are shown together by `TToolRun`, which draws each of them with this.
 */
import { computed } from 'vue'
import type { ToolCallInfo } from '../../headless/useChat'
import { useChatRenderContext } from '../chat/chat-render-context'
import TToolCallCard from './TToolCallCard.vue'
import { resolveToolEntry } from './built-in-tool-renderers'

const props = withDefaults(
  defineProps<{
    toolCall: ToolCallInfo
    streaming?: boolean
  }>(),
  { streaming: false },
)

const context = useChatRenderContext()
const renderer = computed(
  () => resolveToolEntry(props.toolCall.name, context.value.toolRenderers).component ?? TToolCallCard,
)
</script>

<template>
  <component :is="renderer" :tool-call="toolCall" :streaming="streaming" />
</template>
