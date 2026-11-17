<script setup lang="ts">
/**
 * TPlanCard - the agent's plan on one card (`PlanCardModel`): the plan as Markdown, in a box that
 * scrolls when long, under the card's note when it has one (a plan that is only the agent's
 * draft), or a line saying there is none when the agent handed over no text; then approve, approve
 * with edits accepted when offered, and revise, which opens a box for what to change and sends it
 * as the agent's feedback (it may be left empty). Built on `TApprovalCard`, so it looks, counts
 * down and leaves like the other cards; takes every word from its props.
 */
import { computed, nextTick, ref, watch } from 'vue'
import TStreamMarkdown from '../streaming/TStreamMarkdown.vue'
import TApprovalCard from './TApprovalCard.vue'
import { vScrollHint } from '../../headless/scroll-hint'
import type { ApprovalCardModel } from '../../headless/approval-card'
import { ANSWER_MAX_LENGTH, type PlanCardModel } from '../../headless/interaction-card'

export interface PlanCardLabels {
  /** Sends the plan back with the feedback written. */
  send: string
  cancel: string
  feedback: string
  feedbackPlaceholder: string
  /** Said instead of the plan when the agent handed over none. */
  empty: string
}

const props = defineProps<{
  card: PlanCardModel
  labels: PlanCardLabels
  busy?: boolean
  expiresText: (time: string) => string
}>()

const emit = defineEmits<{
  /** An approval, or a refusal with the feedback (null when none was written). */
  choose: [optionId: string, message: string | null]
  expired: []
}>()

const SEND = 'send-feedback'
const CANCEL = 'cancel-feedback'

/** The refusal being written: its option id, while the feedback box is open. */
const revising = ref<string | null>(null)
const feedback = ref('')
const box = ref<HTMLTextAreaElement | null>(null)

watch(
  () => props.card.key,
  () => {
    revising.value = null
    feedback.value = ''
  },
)

const shell = computed<ApprovalCardModel>(() => ({
  key: props.card.key,
  icon: props.card.icon,
  title: props.card.title,
  fields: [],
  choices: revising.value
    ? [
        { id: SEND, label: props.labels.send, tone: 'deny', primary: true },
        { id: CANCEL, label: props.labels.cancel, tone: 'deny' },
      ]
    : props.card.choices,
  expiresAt: props.card.expiresAt,
}))

async function choose(id: string): Promise<void> {
  if (id === CANCEL) {
    revising.value = null
    return
  }
  if (id === SEND && revising.value) {
    emit('choose', revising.value, feedback.value.trim() || null)
    return
  }
  const choice = props.card.choices.find((c) => c.id === id)
  if (choice?.tone === 'deny') {
    revising.value = id
    await nextTick()
    box.value?.focus()
    return
  }
  emit('choose', id, null)
}
</script>

<template>
  <TApprovalCard :card="shell" :busy="busy" :expires-text="expiresText" @choose="choose" @expired="emit('expired')">
    <p v-if="card.note && card.plan.trim()" class="t-plan-card__note" data-plan-note>{{ card.note }}</p>
    <div v-if="card.plan.trim()" v-scroll-hint class="t-plan-card__plan t-ai-scroll" data-plan>
      <TStreamMarkdown :content="card.plan" />
    </div>
    <p v-else class="t-plan-card__empty" data-plan-empty>{{ labels.empty }}</p>
    <label v-if="revising" class="t-plan-card__feedback">
      <span class="t-plan-card__feedback-label">{{ labels.feedback }}</span>
      <textarea
        ref="box"
        v-model="feedback"
        class="t-input"
        rows="3"
        :maxlength="ANSWER_MAX_LENGTH"
        :placeholder="labels.feedbackPlaceholder"
        :disabled="busy"
        data-plan-feedback
      />
    </label>
  </TApprovalCard>
</template>

<style scoped>
.t-plan-card__plan {
  position: relative;
  margin: 10px 0 0;
  max-height: min(420px, 45dvh);
  overflow: auto;
  padding: 8px 14px;
  border-radius: 8px;
  background: var(--tnzi-ai-hover);
  overscroll-behavior: contain;
  /* Gives way, down to about three lines, when the card would outgrow its ceiling (TApprovalCard). */
  flex-shrink: 1;
  min-height: 72px;
}
/* More below: the last lines fade out, so a cut-off code block reads as "scroll for more". */
.t-plan-card__plan[data-more-below] {
  mask-image: linear-gradient(to bottom, #000 calc(100% - 32px), transparent);
}
.t-plan-card__note {
  margin: 8px 0 0;
  color: var(--tnzi-ai-text-secondary);
  font-size: 12px;
}
.t-plan-card__note + .t-plan-card__plan {
  margin-top: 4px;
}
.t-plan-card__empty {
  margin: 6px 0 0;
  color: var(--tnzi-ai-text-secondary);
}
.t-plan-card__feedback {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-top: 10px;
}
.t-plan-card__feedback-label {
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
}
@media (max-width: 480px) {
  .t-plan-card__plan {
    max-height: 40dvh;
  }
}
</style>
