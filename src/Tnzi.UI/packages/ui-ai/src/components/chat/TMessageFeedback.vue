<script setup lang="ts">
/**
 * TMessageFeedback - Thumbs up/down feedback
 *
 * Allows users to rate assistant messages as positive or negative.
 * Thumbs-down shows an inline reason input on first click.
 */

import { NButton, NTooltip, NInput } from 'naive-ui';
import { ref, computed } from 'vue';
import { Icon } from '@iconify/vue';
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

const isPositive = computed(() => props.value === 'positive');
const isNegative = computed(() => props.value === 'negative');

function handlePositive(): void {
  showReasonInput.value = false;
  emit('feedback', isPositive.value ? null : 'positive');
}

function handleNegative(): void {
  if (isNegative.value) {
    // Toggle off
    showReasonInput.value = false;
    emit('feedback', null);
  } else {
    // Show reason input
    showReasonInput.value = true;
  }
}

function submitReason(): void {
  emit('feedback', 'negative', reason.value || undefined);
  showReasonInput.value = false;
  reason.value = '';
}
</script>

<template>
  <div class="flex items-center gap-1">
    <!-- Thumbs up -->
    <NTooltip>
      <template #trigger>
        <NButton
          quaternary
          size="tiny"
          :class="isPositive ? 'text-ai-node-completed' : ''"
          @click="handlePositive"
        >
          <template #icon><Icon icon="lucide:thumbs-up" /></template>
        </NButton>
      </template>
      {{ t.feedback.helpful }}
    </NTooltip>

    <!-- Thumbs down -->
    <NTooltip>
      <template #trigger>
        <NButton
          quaternary
          size="tiny"
          :class="isNegative ? 'text-ai-node-failed' : ''"
          @click="handleNegative"
        >
          <template #icon><Icon icon="lucide:thumbs-down" /></template>
        </NButton>
      </template>
      {{ t.feedback.notHelpful }}
    </NTooltip>

    <!-- Reason input (inline, appears when thumbs-down first clicked) -->
    <div
      v-if="showReasonInput"
      class="ml-1 flex items-center gap-1"
    >
      <NInput
        v-model:value="reason"
        size="tiny"
        style="width: 192px"
        :placeholder="t.feedback.reasonPlaceholder"
        @keydown.enter="submitReason"
      />
      <NButton quaternary size="tiny" @click="submitReason">
        <template #icon><Icon icon="lucide:send" /></template>
      </NButton>
    </div>
  </div>
</template>
