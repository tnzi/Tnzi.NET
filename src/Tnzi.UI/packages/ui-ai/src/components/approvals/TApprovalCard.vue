<script setup lang="ts">
/**
 * TApprovalCard - one approval card (`ApprovalCardModel`): what is asked, the fields and the text
 * at stake, the answers, and the time left. Says `expired` once that time is up, so the card can
 * leave without waiting for the server. Knows nothing of what it approves and takes every word it
 * shows from its model or its props. A card of its own kind (questions, a plan) puts what it adds
 * in the default slot, between the text and the answers. On a narrow screen its buttons share the
 * row, each as tall as a thumb needs; three answers or more each take the whole width.
 *
 * Never taller than `--t-pending-approvals-max-height` (set by `TPendingApprovals`; no limit
 * elsewhere): the card is a column whose parts keep their height, except its long text (and the
 * long part a card of its own kind adds, which says so itself) that gives way down to a few lines
 * and scrolls inside. So the title, the answers and the time left stay in view however long the
 * text is; only past even that does the card itself scroll.
 */
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { Icon } from '@iconify/vue'
import TButton from '../controls/TButton.vue'
import { codeDigits, formatApprovalCountdown, secondsLeft, type ApprovalCardModel, type ApprovalTone } from '../../headless/approval-card'
import { vScrollHint } from '../../headless/scroll-hint'

const props = defineProps<{
  card: ApprovalCardModel
  /** An answer is on its way: the buttons wait. */
  busy?: boolean
  /** The line under the answers, given the time left as `m:ss` ("2:13 left; no answer by then counts as no"). */
  expiresText: (time: string) => string
}>()

const emit = defineEmits<{
  /** With the typed code when the card asks for one and the answer allows. */
  choose: [choiceId: string, code?: string]
  expired: []
}>()

const typed = ref('')
const code = computed(() => (props.card.code ? codeDigits(typed.value, props.card.code.length) : null))

/** An allowing answer of a card that asks for a code waits for the code. */
function waitsForCode(tone: ApprovalTone): boolean {
  return !!props.card.code && tone === 'allow' && code.value === null
}

function choose(choiceId: string, tone: ApprovalTone): void {
  emit('choose', choiceId, props.card.code && tone === 'allow' ? (code.value ?? undefined) : undefined)
}

const now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null
const left = computed(() => secondsLeft(props.card.expiresAt, now.value))

function tick(): void {
  now.value = Date.now()
  if (left.value > 0 || timer === null) return
  clearInterval(timer)
  timer = null
  emit('expired')
}

onMounted(() => {
  timer = setInterval(tick, 1000)
  tick()
})

onBeforeUnmount(() => {
  if (timer !== null) clearInterval(timer)
})
</script>

<template>
  <section class="t-approval-card t-ai-scroll" role="group" :aria-label="card.title" :data-approval="card.key">
    <header class="t-approval-card__head">
      <Icon v-if="card.icon" :icon="card.icon" class="t-approval-card__icon" aria-hidden="true" />
      <div class="t-approval-card__titles">
        <h3 class="t-approval-card__title">{{ card.title }}</h3>
        <p v-if="card.summary" class="t-approval-card__summary">{{ card.summary }}</p>
      </div>
    </header>
    <p v-if="card.warning" class="t-approval-card__warning" role="note" data-approval-warning>
      <Icon icon="lucide:triangle-alert" class="t-approval-card__warning-icon" aria-hidden="true" />
      <span>{{ card.warning }}</span>
    </p>
    <dl v-if="card.fields.length" class="t-approval-card__fields">
      <div v-for="(field, index) in card.fields" :key="index" class="t-approval-card__field">
        <dt>{{ field.label }}</dt>
        <dd :class="{ 'is-mono': field.mono }">
          {{ field.value }}
          <details v-if="field.more" class="t-approval-card__more" data-approval-more>
            <summary>{{ field.more.label }}</summary>
            <ul>
              <li v-for="(line, i) in field.more.items" :key="i">{{ line }}</li>
            </ul>
          </details>
        </dd>
      </div>
    </dl>
    <pre v-if="card.detail" v-scroll-hint class="t-approval-card__detail t-ai-scroll" :class="{ 'is-mono': card.detail.mono }">{{ card.detail.text }}</pre>
    <!-- What a card of its own kind adds between the text and the answers (questions, a plan). -->
    <slot />
    <label v-if="card.code" class="t-approval-card__code">
      <span class="t-approval-card__code-label">{{ card.code.label }}</span>
      <input
        v-model="typed"
        class="t-input t-input--md t-input--code t-approval-card__code-input"
        type="text"
        inputmode="numeric"
        autocomplete="one-time-code"
        spellcheck="false"
        :maxlength="card.code.length + 4"
        :placeholder="'0'.repeat(card.code.length)"
        :disabled="busy || left === 0"
        data-approval-code
      />
      <span v-if="card.code.hint" class="t-approval-card__code-hint">{{ card.code.hint }}</span>
    </label>
    <div class="t-approval-card__choices" :class="{ 'is-many': card.choices.length > 2 }">
      <TButton
        v-for="choice in card.choices"
        :key="choice.id"
        class="t-approval-card__choice"
        :class="`is-${choice.tone}`"
        :variant="choice.primary ? 'primary' : 'secondary'"
        :disabled="busy || left === 0 || choice.disabled || waitsForCode(choice.tone)"
        :data-choice="choice.id"
        @click="choose(choice.id, choice.tone)"
      >
        {{ choice.label }}
      </TButton>
    </div>
    <p class="t-approval-card__expires" aria-live="off">{{ expiresText(formatApprovalCountdown(left)) }}</p>
  </section>
</template>

<style scoped>
.t-approval-card {
  position: relative;
  display: flex;
  flex-direction: column;
  width: 100%;
  /* Its own height, up to the ceiling, in a column of cards too: squeezed below that, its answers would scroll away. */
  flex-shrink: 0;
  max-height: var(--t-pending-approvals-max-height, none);
  overflow-y: auto;
  overscroll-behavior: contain;
  box-sizing: border-box;
  padding: 12px 14px;
  border: 1px solid color-mix(in srgb, var(--tnzi-ai-accent) 45%, var(--tnzi-ai-border));
  border-radius: 14px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  text-align: left;
  font-size: 13px;
  line-height: 1.5;
}
.t-approval-card > * {
  flex-shrink: 0;
}
.t-approval-card__head {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}
.t-approval-card__icon {
  flex-shrink: 0;
  margin-top: 2px;
  font-size: 18px;
  color: var(--tnzi-ai-accent-text);
}
.t-approval-card__titles {
  min-width: 0;
}
.t-approval-card__title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
}
.t-approval-card__summary {
  margin: 2px 0 0;
  color: var(--tnzi-ai-text-secondary);
  overflow-wrap: anywhere;
}
.t-approval-card__warning {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  margin: 10px 0 0;
  padding: 8px 10px;
  border-radius: 8px;
  border: 1px solid var(--tnzi-ai-accent);
  background: color-mix(in srgb, var(--tnzi-ai-accent) 18%, var(--tnzi-ai-surface));
  font-weight: 600;
  overflow-wrap: anywhere;
}
.t-approval-card__warning-icon {
  flex-shrink: 0;
  margin-top: 2px;
  font-size: 16px;
}
.t-approval-card__fields {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 2px 12px;
  margin: 10px 0 0;
}
.t-approval-card__field {
  display: contents;
}
.t-approval-card__field dt {
  color: var(--tnzi-ai-text-tertiary);
}
.t-approval-card__field dd {
  margin: 0;
  min-width: 0;
  overflow-wrap: anywhere;
}
.t-approval-card__more {
  margin-top: 2px;
}
.t-approval-card__more summary {
  width: fit-content;
  color: var(--tnzi-ai-text-secondary);
  font-size: 12px;
  cursor: pointer;
}
.t-approval-card__more ul {
  margin: 4px 0 0;
  padding-left: 18px;
  color: var(--tnzi-ai-text-secondary);
  font-size: 12px;
}
.t-approval-card__detail {
  position: relative;
  margin: 10px 0 0;
  max-height: 220px;
  overflow: auto;
  padding: 8px 10px;
  border-radius: 8px;
  background: var(--tnzi-ai-hover);
  font-family: inherit;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
/* The part that gives way when the card would outgrow its ceiling (down to about three lines). */
.t-approval-card > .t-approval-card__detail {
  flex-shrink: 1;
  min-height: 72px;
}
/* More below: the last lines fade out, so a cut-off text reads as "scroll for more". */
.t-approval-card__detail[data-more-below] {
  mask-image: linear-gradient(to bottom, #000 calc(100% - 32px), transparent);
}
.t-approval-card__field dd.is-mono,
.t-approval-card__detail.is-mono {
  font-family: var(--tnzi-ai-font-mono);
  font-size: 12px;
}
.t-approval-card__code {
  display: grid;
  gap: 4px;
  margin-top: 10px;
}
.t-approval-card__code-label {
  color: var(--tnzi-ai-text-secondary);
}
.t-approval-card__code-input {
  max-width: 220px;
}
.t-approval-card__code-hint {
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-approval-card__choices {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 10px;
}
.t-approval-card__expires {
  margin: 8px 0 0;
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
}
@media (max-width: 480px) {
  .t-approval-card {
    padding: 10px 12px;
  }
  .t-approval-card__detail {
    max-height: 140px;
  }
  .t-approval-card__code-input {
    max-width: none;
    min-height: 44px;
  }
  .t-approval-card__choice {
    flex: 1 1 0;
    min-height: 44px;
    padding: 0 8px;
  }
  /* Three answers or more do not share a row: each gets the whole width, the main one first. */
  .t-approval-card__choices.is-many {
    flex-direction: column;
  }
  .t-approval-card__choices.is-many .t-approval-card__choice {
    flex: none;
    width: 100%;
  }
}
</style>
