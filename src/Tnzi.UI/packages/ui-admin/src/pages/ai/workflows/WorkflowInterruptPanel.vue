<template>
  <!--
    Pending human-in-the-loop interrupt of one execution (status AwaitingInput).

    The node that paused the run declares the input it wants as
    `requestedInput` (key to a free-form description such as
    "bool: whether to approve"). The operator answers with a JSON object that
    is handed to the node as its resume data; the backend checks the step id
    against the pending interrupt, so the answer always targets the step that
    is actually waiting.
  -->
  <section class="t-wf-interrupt" data-test="wf-interrupt">
    <h4 class="t-wf-interrupt__title">
      <TSvgIcon icon="mdi:account-clock-outline" :size="16" />
      {{ t('interrupt.title') }}
      <NTag v-if="interrupt" size="small" type="warning" :bordered="false">{{ interrupt.type }}</NTag>
    </h4>

    <NSpin :show="loading">
      <NAlert v-if="loadError" type="error" :title="t('interrupt.loadError')" data-test="wf-interrupt-error">
        {{ loadError }}
      </NAlert>
      <div v-else-if="interrupt" class="t-wf-interrupt__body">
        <div class="t-wf-interrupt__meta">
          <div><span>{{ t('interrupt.step') }}:</span> <code>{{ interrupt.stepId }}</code></div>
          <div v-if="interrupt.timeoutSeconds"><span>{{ t('interrupt.timeout') }}:</span> {{ t('interrupt.seconds', { n: Math.round(interrupt.timeoutSeconds) }) }}</div>
        </div>
        <p class="t-wf-interrupt__reason">{{ interrupt.reason }}</p>

        <div v-if="requestedFields.length" class="t-wf-interrupt__fields">
          <div class="t-wf-interrupt__label">{{ t('interrupt.requested') }}</div>
          <ul>
            <li v-for="f in requestedFields" :key="f.key"><code>{{ f.key }}</code> {{ f.description }}</li>
          </ul>
        </div>

        <template v-if="canExecute">
          <div class="t-wf-interrupt__label">{{ t('interrupt.answer') }}</div>
          <NInput
            v-model:value="answer"
            type="textarea"
            size="small"
            :rows="6"
            class="t-wf-interrupt__input"
            data-test="wf-interrupt-input"
          />
          <NAlert v-if="answerError" type="error" :show-icon="false" class="mt-8px" data-test="wf-interrupt-answer-error">
            {{ answerError }}
          </NAlert>
          <div class="t-wf-interrupt__actions">
            <NButton
              size="small"
              type="primary"
              :loading="submitting"
              data-test="wf-interrupt-submit"
              @click="submit"
            >
              <template #icon><TSvgIcon icon="mdi:play" :size="14" /></template>
              {{ t('interrupt.submit') }}
            </NButton>
          </div>
        </template>
      </div>
    </NSpin>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NAlert, NButton, NInput, NSpin, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { useSafeMessage } from '../../_shared/safe-message'
import { makePageTranslator } from '../../_shared/translate'
import type { WorkflowInterruptDto } from '@tnzi/core/services/ai'

const props = defineProps<{
  executionId: string
  /** Hold `ai.workflow.execute`: without it the interrupt is shown read-only. */
  canExecute: boolean
}>()

const emit = defineEmits<{
  /** The execution was resumed; its status changed. */
  resumed: []
}>()

const t = makePageTranslator('ai.workflowRuns')
const bridge = createAiBridge({ client: useAdminClient() })
const message = useSafeMessage()

const interrupt = ref<WorkflowInterruptDto | null>(null)
const loading = ref(false)
const loadError = ref('')
const answer = ref('{}')
const answerError = ref('')
const submitting = ref(false)

const requestedFields = computed(() =>
  Object.entries(interrupt.value?.requestedInput ?? {}).map(([key, value]) => ({
    key,
    description: typeof value === 'string' ? value : JSON.stringify(value),
  })),
)

/** Starting answer: one key per requested field, typed from its description where it says so. */
function answerTemplate(requested: Record<string, unknown> | null | undefined): string {
  const template: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(requested ?? {})) {
    const hint = typeof value === 'string' ? value.trim().toLowerCase() : ''
    if (hint.startsWith('bool')) template[key] = false
    else if (hint.startsWith('int') || hint.startsWith('number')) template[key] = 0
    else template[key] = ''
  }
  return JSON.stringify(template, null, 2)
}

async function load(executionId: string): Promise<void> {
  loading.value = true
  loadError.value = ''
  try {
    const result = await bridge.workflowRuns.getInterrupt(executionId)
    if (executionId !== props.executionId) return
    interrupt.value = result
    answer.value = answerTemplate(result.requestedInput)
    answerError.value = ''
  } catch (err) {
    if (executionId !== props.executionId) return
    interrupt.value = null
    loadError.value = err instanceof Error ? err.message : String(err)
  } finally {
    if (executionId === props.executionId) loading.value = false
  }
}

watch(() => props.executionId, (id) => { if (id) void load(id) }, { immediate: true })

/** The resume data must be a JSON object: the node reads it as a key/value bag. */
function parseAnswer(): Record<string, unknown> | null {
  try {
    const parsed = JSON.parse(answer.value) as unknown
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) return parsed as Record<string, unknown>
  } catch {
    // fall through to the shared message
  }
  answerError.value = t('interrupt.answerInvalid')
  return null
}

async function submit(): Promise<void> {
  if (!props.canExecute || !interrupt.value) return
  answerError.value = ''
  const input = parseAnswer()
  if (!input) return
  submitting.value = true
  try {
    const result = await bridge.workflowRuns.resumeWithInput(props.executionId, interrupt.value.stepId, input)
    message.success(t('interrupt.resumed', { status: result.status }))
    emit('resumed')
  } catch (err) {
    message.error(err instanceof Error ? err.message : t('interrupt.resumeError'))
  } finally {
    submitting.value = false
  }
}
</script>

<style scoped>
.t-wf-interrupt {
  margin: 16px 0;
  padding: 12px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 4px);
}
.t-wf-interrupt__title {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 0 0 8px;
  font-size: 14px;
  font-weight: 600;
}
.t-wf-interrupt__meta {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 16px;
  font-size: 13px;
}
.t-wf-interrupt__meta span,
.t-wf-interrupt__label {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
}
.t-wf-interrupt__reason {
  margin: 8px 0;
  font-size: 13px;
  white-space: pre-wrap;
  word-break: break-word;
}
.t-wf-interrupt__fields ul {
  margin: 4px 0 8px;
  padding-left: 16px;
  font-size: 13px;
}
.t-wf-interrupt__input :deep(textarea) {
  font-family: ui-monospace, SFMono-Regular, monospace;
  font-size: 12px;
}
.t-wf-interrupt__actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 8px;
}
</style>
