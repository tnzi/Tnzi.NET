<script setup lang="ts">
/**
 * @experimental
 * TTaskDoneRow — Manus-style task-completed row with inline rating.
 *
 * A thin top-bordered row that reads as part of the thread flow: left
 * side has a green check + completion label, right side has an optional
 * rating prompt + 5-star rating control. Keep it quiet; it's meant to
 * feel like a divider with information, not a separate object.
 *
 * Two-way bind the rating via `v-model:rating`. Set `:showRating="false"`
 * to hide the rating half entirely.
 */
import { Icon } from '@iconify/vue'

withDefaults(
  defineProps<{
    /** Label shown next to the green check. */
    label?: string
    /** Prompt shown before the rating stars. */
    ratingLabel?: string
    /** Current rating (0-5). Two-way bound via `v-model:rating`. */
    rating?: number
    /** Hide the rating half. Defaults to `true`. */
    showRating?: boolean
  }>(),
  {
    label: 'Task completed',
    ratingLabel: 'How was this result?',
    rating: 0,
    showRating: true,
  },
)

const emit = defineEmits<{
  'update:rating': [value: number]
}>()

function setRating(n: number): void {
  emit('update:rating', n)
}
</script>

<template>
  <div class="t-task-done-row">
    <span class="t-task-done-row__label">
      <Icon icon="lucide:check" class="t-task-done-row__check" />
      <span>{{ label }}</span>
    </span>

    <div v-if="showRating" class="t-task-done-row__rating">
      <span class="t-task-done-row__rating-label">{{ ratingLabel }}</span>
      <div
        class="t-task-done-row__stars"
        role="radiogroup"
        :aria-label="ratingLabel"
      >
        <button
          v-for="n in 5"
          :key="n"
          type="button"
          class="t-task-done-row__star"
          :class="{ 'is-on': rating >= n }"
          :aria-label="`${n} star${n > 1 ? 's' : ''}`"
          @click="setRating(n)"
        >
          <Icon icon="lucide:star" />
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-task-done-row {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 10px 2px 6px;
  border-top: 1px solid var(--tnzi-ai-divider, #ececec);
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
}
.t-task-done-row__label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex: 1;
  color: var(--tnzi-ai-text, #1a1a1a);
}
.t-task-done-row__check {
  color: #2b9c5f;
  font-size: 15px;
}
.t-task-done-row__rating {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.t-task-done-row__rating-label {
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
}
.t-task-done-row__stars {
  display: inline-flex;
  gap: 2px;
}
.t-task-done-row__star {
  width: 24px;
  height: 24px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-border-strong, #d4d4d4);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  border-radius: 4px;
  transition: transform 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
.t-task-done-row__star:hover { transform: scale(1.1); }
.t-task-done-row__star.is-on { color: #f0b429; }
.t-task-done-row__star.is-on :deep(svg) { fill: currentColor; }
</style>
