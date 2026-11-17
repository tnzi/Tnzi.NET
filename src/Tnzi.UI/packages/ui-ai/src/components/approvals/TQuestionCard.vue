<script setup lang="ts">
/**
 * TQuestionCard - the agent's questions on one card (`QuestionCardModel`): each with its short
 * label, its options (one, or several when it says so) and room for the user's own answer, then
 * submit once every question has an answer, or decline. Built on `TApprovalCard`, so it looks,
 * counts down and leaves like the other cards; takes every word from its props. Many questions
 * scroll inside the card, so its answers stay in view.
 */
import { computed, ref, watch } from 'vue'
import type { CliQuestion } from '@tnzi/core/services/ai'
import TApprovalCard from './TApprovalCard.vue'
import { vScrollHint } from '../../headless/scroll-hint'
import type { ApprovalCardModel } from '../../headless/approval-card'
import {
  ANSWER_MAX_LENGTH,
  EMPTY_PICK,
  isAnswered,
  pickOption,
  pickOwn,
  questionAnswers,
  type QuestionCardModel,
  type QuestionPick,
} from '../../headless/interaction-card'

export interface QuestionCardLabels {
  submit: string
  decline: string
  /** The choice of writing one's own answer ("Other"). */
  own: string
  ownPlaceholder: string
  /** Said by a question that takes several options. */
  multiple: string
}

const props = defineProps<{
  card: QuestionCardModel
  labels: QuestionCardLabels
  busy?: boolean
  expiresText: (time: string) => string
}>()

const emit = defineEmits<{
  submit: [answers: Record<string, string[]>]
  decline: []
  expired: []
}>()

const SUBMIT = 'submit'
const DECLINE = 'decline'

const picks = ref<QuestionPick[]>(props.card.questions.map(() => EMPTY_PICK))
watch(
  () => props.card.key,
  () => (picks.value = props.card.questions.map(() => EMPTY_PICK)),
)

const answers = computed(() => questionAnswers(props.card.questions, picks.value))

const shell = computed<ApprovalCardModel>(() => ({
  key: props.card.key,
  icon: props.card.icon,
  title: props.card.title,
  fields: [],
  choices: [
    { id: SUBMIT, label: props.labels.submit, tone: 'allow', primary: true, disabled: answers.value === null },
    { id: DECLINE, label: props.labels.decline, tone: 'deny' },
  ],
  expiresAt: props.card.expiresAt,
}))

function update(index: number, next: QuestionPick): void {
  picks.value = picks.value.map((pick, i) => (i === index ? next : pick))
}

function choose(id: string): void {
  if (id === DECLINE) {
    emit('decline')
    return
  }
  if (answers.value) emit('submit', answers.value)
}

const pickAt = (index: number): QuestionPick => picks.value[index] ?? EMPTY_PICK
const name = (index: number) => `${props.card.key}:q${index}`
const answered = (question: CliQuestion, index: number) => isAnswered(question, pickAt(index))
</script>

<template>
  <TApprovalCard :card="shell" :busy="busy" :expires-text="expiresText" @choose="choose" @expired="emit('expired')">
    <div v-scroll-hint class="t-question-card t-ai-scroll" data-questions>
      <fieldset
        v-for="(question, index) in card.questions"
        :key="index"
        class="t-question-card__question"
        :disabled="busy"
        :data-question="index"
        :data-answered="answered(question, index) ? '' : undefined"
      >
        <legend class="t-question-card__legend">
          <span v-if="question.header" class="t-question-card__header">{{ question.header }}</span>
          <span class="t-question-card__text">{{ question.question }}</span>
          <span v-if="question.multiSelect" class="t-question-card__multiple">{{ labels.multiple }}</span>
        </legend>
        <label v-for="option in question.options" :key="option.label" class="t-question-card__option">
          <input
            :type="question.multiSelect ? 'checkbox' : 'radio'"
            :name="name(index)"
            :checked="pickAt(index).selected.includes(option.label)"
            :data-option="option.label"
            @change="update(index, pickOption(question, pickAt(index), option.label))"
          />
          <span class="t-question-card__option-body">
            <span class="t-question-card__label">{{ option.label }}</span>
            <span v-if="option.description" class="t-question-card__description">{{ option.description }}</span>
          </span>
        </label>
        <label class="t-question-card__option">
          <input
            :type="question.multiSelect ? 'checkbox' : 'radio'"
            :name="name(index)"
            :checked="pickAt(index).own"
            data-option-own
            @change="update(index, pickOwn(question, pickAt(index), question.multiSelect ? !pickAt(index).own : true))"
          />
          <span class="t-question-card__option-body">
            <span class="t-question-card__label">{{ labels.own }}</span>
          </span>
        </label>
        <textarea
          v-if="pickAt(index).own"
          class="t-input t-question-card__own"
          rows="2"
          :maxlength="ANSWER_MAX_LENGTH"
          :placeholder="labels.ownPlaceholder"
          :value="pickAt(index).text"
          :aria-label="labels.own"
          data-own-answer
          @input="update(index, { ...pickAt(index), text: ($event.target as HTMLTextAreaElement).value })"
        />
      </fieldset>
    </div>
  </TApprovalCard>
</template>

<style scoped>
.t-question-card {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-top: 10px;
  /* Gives way, down to about three lines, when the card would outgrow its ceiling (TApprovalCard). */
  flex-shrink: 1;
  min-height: 72px;
  overflow-y: auto;
  overscroll-behavior: contain;
}
/* More below: the last lines fade out, so cut-off questions read as "scroll for more". */
.t-question-card[data-more-below] {
  mask-image: linear-gradient(to bottom, #000 calc(100% - 32px), transparent);
}
.t-question-card__question {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  margin: 0;
  padding: 0;
  border: none;
}
.t-question-card__legend {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px 8px;
  margin-bottom: 4px;
  padding: 0;
}
.t-question-card__header {
  padding: 0 6px;
  border-radius: 6px;
  background: var(--tnzi-ai-accent-soft);
  color: var(--tnzi-ai-accent-text);
  font-size: 12px;
  font-weight: 500;
  line-height: 20px;
}
.t-question-card__text {
  font-weight: 500;
  overflow-wrap: anywhere;
}
.t-question-card__multiple {
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-question-card__option {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 6px 8px;
  border-radius: 8px;
  cursor: pointer;
}
.t-question-card__option:hover {
  background: var(--tnzi-ai-hover);
}
.t-question-card__option input {
  flex-shrink: 0;
  margin: 3px 0 0;
  accent-color: var(--tnzi-ai-accent);
}
.t-question-card__option-body {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-question-card__label {
  overflow-wrap: anywhere;
}
.t-question-card__description {
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
  overflow-wrap: anywhere;
}
.t-question-card__own {
  margin: 2px 0 0;
}
fieldset:disabled .t-question-card__option {
  cursor: default;
  opacity: 0.6;
}
@media (max-width: 480px) {
  .t-question-card__option {
    padding: 8px;
  }
}
</style>
