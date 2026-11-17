<script setup lang="ts">
/**
 * TMessageFeedback - Thumbs up/down feedback
 *
 * Allows users to rate assistant messages as positive or negative.
 * Thumbs-down shows an inline reason input on first click.
 */

import { ref, computed, nextTick } from 'vue';
import { isImeComposing } from '@tnzi/core/utils';
import TIconButton from '../controls/TIconButton.vue';
import { useAiI18n } from '../../i18n/index';
export type FeedbackValue = 'positive' | 'negative' | null;

const props = defineProps<{
  value: FeedbackValue;
}>();

const emit = defineEmits<{
  feedback: [type: FeedbackValue, reason?: string];
}>();

const t = useAiI18n();
const showReasonInput = ref(false);
const reason = ref('');
const reasonRef = ref<HTMLInputElement | null>(null);

const isPositive = computed(() => props.value === 'positive');
const isNegative = computed(() => props.value === 'negative');

function handlePositive(): void {
  closeReason();
  emit('feedback', isPositive.value ? null : 'positive');
}

function handleNegative(): void {
  if (isNegative.value) {
    // Toggle off
    closeReason();
    emit('feedback', null);
  } else if (showReasonInput.value) {
    // A second press before sending puts the box away again.
    closeReason();
  } else {
    // The reason box opens with the caret in it: the press already said what
    // the user is about to do.
    showReasonInput.value = true;
    void nextTick(() => reasonRef.value?.focus());
  }
}

function closeReason(): void {
  showReasonInput.value = false;
  reason.value = '';
}

// The Enter that commits an IME candidate is part of typing the reason.
function onReasonEnter(event: KeyboardEvent): void {
  if (isImeComposing(event)) return;
  submitReason();
}

function submitReason(): void {
  emit('feedback', 'negative', reason.value.trim() || undefined);
  closeReason();
}
</script>

<template>
  <div class="t-message-feedback">
    <TIconButton
      icon="lucide:thumbs-up"
      class="t-message-feedback__up"
      :label="t.feedback.helpful"
      :active="isPositive"
      @click="handlePositive"
    />
    <TIconButton
      icon="lucide:thumbs-down"
      class="t-message-feedback__down"
      :label="t.feedback.notHelpful"
      :active="isNegative"
      @click="handleNegative"
    />

    <!-- Reason input (inline, appears when thumbs-down first clicked) -->
    <div v-if="showReasonInput" class="t-message-feedback__reason">
      <input
        ref="reasonRef"
        v-model="reason"
        type="text"
        class="t-input t-message-feedback__input"
        :placeholder="t.feedback.reasonPlaceholder"
        :aria-label="t.feedback.reasonPlaceholder"
        @keydown.enter="onReasonEnter"
        @keydown.esc.stop="closeReason"
      />
      <TIconButton icon="lucide:send" :label="t.feedback.sendReason" @click="submitReason" />
    </div>
  </div>
</template>

<style scoped>
.t-message-feedback {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
}
/* Pressed: the thumb takes the colour of what it says, on the pressed tint. */
.t-message-feedback__up.t-icon-button--active {
  color: var(--tnzi-ai-success);
}
.t-message-feedback__down.t-icon-button--active {
  color: var(--tnzi-ai-danger);
}
.t-message-feedback__reason {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-left: 4px;
  min-width: 0;
}
.t-message-feedback__input {
  width: 220px;
  max-width: 100%;
  min-width: 0;
}
</style>
