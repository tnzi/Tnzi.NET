<script setup lang="ts">
/**
 * TPendingApprovals - the cards a conversation waits on its user for (an `ApprovalQueue`), one
 * under the other, oldest first: an external agent's permission requests, questions and plans with
 * the framework's cards, and any kind of the application's own. An answer goes through the item's
 * source and the card leaves at once; a card that no longer waited (answered elsewhere, timed out)
 * leaves with a note, and a refused answer keeps the card with the reason. Several cards scroll
 * among themselves, and no card grows taller than the band (`--tnzi-ai-pending-approvals-max-height`,
 * 40dvh by default): a long one keeps its title and answers in view and scrolls its text inside.
 *
 * `describe` gives a card model for an item (an application's own kind, or its own wording of a
 * built-in one): its answers come back as `optionId` (with the typed code). An item it gives no
 * model for, of a kind the framework does not draw, goes to the `#card` slot.
 */
import { computed } from 'vue'
import { useAiI18n, formatAiMessage } from '../../i18n/index'
import { runRequestCardLabels } from './labels'
import { useToast } from '../controls/toast'
import TApprovalCard from './TApprovalCard.vue'
import TPlanCard from './TPlanCard.vue'
import TQuestionCard from './TQuestionCard.vue'
import type { ApprovalCardModel } from '../../headless/approval-card'
import type { ApprovalAnswer, ApprovalQueue, PendingInteraction, QueuedInteraction } from '../../headless/approval-queue'
import { questionOptionIds } from '../../headless/interaction-card'
import {
  permissionCardModel,
  planCardModel,
  questionCardModel,
  type RunRequestItem,
} from '../../headless/run-requests'
import { vScrollHint } from '../../headless/scroll-hint'

const props = defineProps<{
  queue: ApprovalQueue
  /** A card model for an item; null leaves it to the framework's card or the `#card` slot. */
  describe?: ((item: PendingInteraction) => ApprovalCardModel | null) | null
}>()

defineSlots<{
  /** An item no card is drawn for: answer it with `answer`, take it off with `remove`. */
  card?(props: { item: QueuedInteraction; busy: boolean; answer: (reply: ApprovalAnswer) => Promise<void>; remove: () => void; expiresText: (time: string) => string }): unknown
}>()

const t = useAiI18n()
const toast = useToast()

const labels = computed(() => runRequestCardLabels(t.value.approvals))
const questionLabels = computed(() => ({
  submit: t.value.approvals.submit,
  decline: t.value.approvals.decline,
  own: t.value.approvals.own,
  ownPlaceholder: t.value.approvals.ownPlaceholder,
  multiple: t.value.approvals.multiple,
}))
const planLabels = computed(() => ({
  send: t.value.approvals.send,
  cancel: t.value.approvals.cancel,
  feedback: t.value.approvals.feedback,
  feedbackPlaceholder: t.value.approvals.feedbackPlaceholder,
  empty: t.value.approvals.planEmpty,
}))
const expiresText = (time: string) => formatAiMessage(t.value.approvals.expires, { time })

type Entry =
  | { item: QueuedInteraction; view: 'approval'; card: ApprovalCardModel }
  | { item: QueuedInteraction; view: 'question'; question: ReturnType<typeof questionCardModel> }
  | { item: QueuedInteraction; view: 'plan'; plan: ReturnType<typeof planCardModel> }
  | { item: QueuedInteraction; view: 'slot' }

const entries = computed<Entry[]>(() =>
  props.queue.items.value.map((item): Entry => {
    const own = props.describe?.(item) ?? null
    if (own) return { item, view: 'approval', card: own }
    const request = item as unknown as RunRequestItem
    if (item.kind === 'question') return { item, view: 'question', question: questionCardModel(request, labels.value) }
    if (item.kind === 'plan') return { item, view: 'plan', plan: planCardModel(request, labels.value) }
    if (item.kind === 'permission') return { item, view: 'approval', card: permissionCardModel(request, labels.value) }
    return { item, view: 'slot' }
  }),
)

const busy = (key: string) => props.queue.answering.value.has(key)

async function answer(item: QueuedInteraction, reply: ApprovalAnswer): Promise<void> {
  try {
    const outcome = await props.queue.answer(item.key, reply)
    if (outcome === 'closed') toast.info(t.value.approvals.closed)
  } catch (error) {
    const message = error instanceof Error && error.message ? error.message : t.value.chat.errorGeneric
    toast.error(formatAiMessage(t.value.approvals.failed, { message }))
  }
}

function questionIds(item: QueuedInteraction): { submit: string; decline: string } {
  return questionOptionIds((item as unknown as RunRequestItem).data.options ?? [])
}
</script>

<template>
  <div
    v-if="entries.length"
    v-scroll-hint
    class="t-pending-approvals t-ai-scroll"
    role="region"
    :aria-label="t.approvals.region"
    aria-live="polite"
    data-pending-approvals
  >
    <template v-for="entry in entries" :key="entry.item.key">
      <TQuestionCard
        v-if="entry.view === 'question'"
        :card="entry.question"
        :labels="questionLabels"
        :busy="busy(entry.item.key)"
        :expires-text="expiresText"
        @submit="(answers) => answer(entry.item, { optionId: questionIds(entry.item).submit, answers })"
        @decline="answer(entry.item, { optionId: questionIds(entry.item).decline })"
        @expired="queue.remove(entry.item.key)"
      />
      <TPlanCard
        v-else-if="entry.view === 'plan'"
        :card="entry.plan"
        :labels="planLabels"
        :busy="busy(entry.item.key)"
        :expires-text="expiresText"
        @choose="(optionId, message) => answer(entry.item, { optionId, message })"
        @expired="queue.remove(entry.item.key)"
      />
      <TApprovalCard
        v-else-if="entry.view === 'approval'"
        :card="entry.card"
        :busy="busy(entry.item.key)"
        :expires-text="expiresText"
        @choose="(optionId, code) => answer(entry.item, { optionId, code })"
        @expired="queue.remove(entry.item.key)"
      />
      <slot
        v-else
        name="card"
        :item="entry.item"
        :busy="busy(entry.item.key)"
        :answer="(reply: ApprovalAnswer) => answer(entry.item, reply)"
        :remove="() => queue.remove(entry.item.key)"
        :expires-text="expiresText"
      />
    </template>
  </div>
</template>

<style scoped>
.t-pending-approvals {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: 100%;
  flex-shrink: 0;
  /* Several cards scroll among themselves: the composer's band must never outgrow the screen.
     The cards read the same height as their own ceiling (TApprovalCard). */
  --t-pending-approvals-max-height: var(--tnzi-ai-pending-approvals-max-height, 40dvh);
  max-height: var(--t-pending-approvals-max-height);
  overflow-y: auto;
  overscroll-behavior: contain;
}
/* Every card keeps its own height (each caps itself): the band scrolls, the cards are not squeezed. */
.t-pending-approvals > *,
.t-pending-approvals > :slotted(*) {
  flex-shrink: 0;
}
.t-pending-approvals[data-more-below] {
  mask-image: linear-gradient(to bottom, #000 calc(100% - 32px), transparent);
}
</style>
