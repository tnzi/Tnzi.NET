<script setup lang="ts">
/**
 * @experimental
 * TToolCallCard - the generic view of a tool call: what the tool was asked to do, and
 * what it returned, as a tool step (`TToolStep`).
 *
 * The fallback for every tool without a renderer of its own (see `TToolCall`). The
 * line says the tool's name and what it acted on (the first short string of its
 * input), or, when the product described the tool (`ToolRendererEntry.describe`), its
 * phrase for the step's state and its subject; opened, it shows the input as labelled lines and the result read for a
 * person (`toolResultView`): pages as links, JSON as labelled lines, text as text,
 * pictures as pictures. Files the tool handed over show under the line whether it is
 * open or not: they are the part a user came for.
 */
import { computed } from 'vue'
import { useAiI18n } from '../../i18n/index'
import type { ToolCallInfo } from '../../headless/useChat'
import { useChatRenderContext } from '../chat/chat-render-context'
import TAttachmentImage from '../chat/TAttachmentImage.vue'
import TFileCard from '../chat/TFileCard.vue'
import TToolStep from './TToolStep.vue'
import TToolFields from './TToolFields.vue'
import TToolLinkList from './TToolLinkList.vue'
import TToolOutput from './TToolOutput.vue'
import { readableToolName, toolCallState, toolInputSummary } from './tool-call-status'
import { toolFields, toolResultText, toolResultView } from './tool-views'
import { resolveToolEntry } from './built-in-tool-renderers'
import { describeToolStep, stepPhrase } from './tool-steps'

const props = withDefaults(
  defineProps<{
    toolCall: ToolCallInfo
    /** The message is still streaming. */
    streaming?: boolean
    /** Open the details from the start. */
    defaultOpen?: boolean
  }>(),
  { streaming: false, defaultOpen: false },
)

const t = useAiI18n()
const context = useChatRenderContext()

const state = computed(() => toolCallState(props.toolCall, props.streaming))
const name = computed(() => readableToolName(props.toolCall.name))
/* The product's words for the tool, when it gave some. */
const described = computed(() => {
  const describe = resolveToolEntry(props.toolCall.name, context.value.toolRenderers).describe
  return describe ? describeToolStep(props.toolCall, describe, t.value) : null
})
const label = computed(() =>
  described.value ? stepPhrase({ description: described.value, state: state.value }) : name.value,
)
const summary = computed(() =>
  described.value?.subject !== undefined ? described.value.subject : toolInputSummary(props.toolCall.input),
)
const inputFields = computed(() => toolFields(props.toolCall.input))

const blocks = computed(() => props.toolCall.result ?? [])
const files = computed(() => blocks.value.filter((b) => b.type === 'file'))
const images = computed(() => blocks.value.filter((b) => b.type === 'image'))
const result = computed(() => toolResultView(toolResultText(props.toolCall)))
const hasResult = computed(
  () => result.value.links.length > 0 || result.value.fields.length > 0 || !!result.value.text || images.value.length > 0,
)
const error = computed(() => (state.value === 'failed' ? props.toolCall.error ?? null : null))

const hasDetails = computed(() => inputFields.value.length > 0 || hasResult.value || !!error.value)
</script>

<template>
  <TToolStep
    class="t-tool-card"
    :state="state"
    :label="label"
    :subject="summary"
    :expandable="hasDetails"
    :default-open="defaultOpen"
  >
    <TToolFields v-if="inputFields.length" :fields="inputFields" :label="t.chat.toolInput" />
    <section v-if="hasResult" class="t-tool-card__result">
      <div class="t-tool-card__label">{{ t.chat.toolResult }}</div>
      <TToolLinkList v-if="result.links.length" :links="result.links" />
      <TToolFields v-if="result.fields.length" :fields="result.fields" />
      <TToolOutput v-if="result.text" :text="result.text" />
      <div v-if="images.length" class="t-tool-card__images">
        <TAttachmentImage
          v-for="(image, i) in images"
          :key="`image:${image.attachmentId ?? i}`"
          :attachment-id="image.attachmentId"
          :alt="name"
          large
        />
      </div>
    </section>
    <TToolOutput v-if="error" :text="error" :mono="false" error />

    <template v-if="files.length" #after>
      <div class="t-tool-card__files">
        <TFileCard
          v-for="(file, i) in files"
          :key="`${file.uri}:${i}`"
          :file="{ name: file.name, uri: file.uri, mediaType: file.mediaType }"
        />
      </div>
    </template>
  </TToolStep>
</template>

<style scoped>
.t-tool-card__result {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}
.t-tool-card__label {
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-ai-text-tertiary);
}
.t-tool-card__images { display: flex; flex-wrap: wrap; gap: 8px; }
.t-tool-card__files { display: flex; flex-wrap: wrap; gap: 8px; }
</style>
