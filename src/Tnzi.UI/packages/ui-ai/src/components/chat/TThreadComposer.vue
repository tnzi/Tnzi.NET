<script setup lang="ts">
/**
 * @experimental
 * TThreadComposer — Manus-style sticky-bottom composer for in-progress
 * conversations.
 *
 * Unlike the landing-page composer which owns the entire page layout,
 * this composer is meant to sit INSIDE a scrollable chat thread and
 * stick to the bottom of the scrollport. When there are few messages it
 * pushes itself to the bottom of the thread via `margin-top: auto`; when
 * there are many messages it acts as a `position: sticky` footer.
 *
 * Two-way text binding via `v-model`. Left-side button cluster goes in
 * `#left`, right-side cluster goes in `#right` (before the send button).
 * The send button is built-in but can be replaced via the `#send` slot.
 *
 * Fires a `send` event when the built-in send button is clicked (only
 * when text is non-empty) or when Enter is pressed without Shift.
 */
import { computed } from 'vue'
import { Icon } from '@iconify/vue'

const props = withDefaults(
  defineProps<{
    /** Two-way bound text value. */
    modelValue: string
    /** Placeholder text for the textarea. */
    placeholder?: string
    /** Disable the send button regardless of text content. */
    disabled?: boolean
    /** Number of textarea rows (defaults to 1; auto-grows up to max-height). */
    rows?: number
  }>(),
  {
    placeholder: 'Send a message',
    disabled: false,
    rows: 1,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  send: [text: string]
}>()

const canSend = computed(() => !props.disabled && props.modelValue.trim().length > 0)

function onInput(e: Event): void {
  emit('update:modelValue', (e.target as HTMLTextAreaElement).value)
}

function doSend(): void {
  if (!canSend.value) return
  emit('send', props.modelValue.trim())
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    doSend()
  }
}
</script>

<template>
  <div class="t-thread-composer-wrap">
    <div class="t-thread-composer">
      <textarea
        class="t-thread-composer__input"
        :value="modelValue"
        :placeholder="placeholder"
        :rows="rows"
        @input="onInput"
        @keydown="onKeydown"
      />
      <div class="t-thread-composer__bar">
        <div class="t-thread-composer__left">
          <slot name="left" />
        </div>
        <div class="t-thread-composer__right">
          <slot name="right" />
          <slot name="send" :can-send="canSend" :send="doSend">
            <button
              type="button"
              class="t-thread-composer__send"
              :class="{ 'is-ready': canSend }"
              :disabled="!canSend"
              aria-label="Send"
              @click="doSend"
            >
              <Icon icon="lucide:arrow-up" />
            </button>
          </slot>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-thread-composer-wrap {
  /* Pushes itself to the bottom of the parent flex column. Combined with
     `position: sticky` it stays visible while the thread scrolls. */
  margin-top: auto;
  padding-top: 24px;
  position: sticky;
  bottom: 0;
  background: linear-gradient(
    to bottom,
    transparent 0%,
    var(--tnzi-ai-bg, #f8f8f7) 24px
  );
  display: flex;
  justify-content: center;
}
.t-thread-composer {
  width: 100%;
  background: var(--tnzi-ai-surface, #ffffff);
  border: 1px solid var(--tnzi-ai-border-strong, #d4d4d4);
  border-radius: var(--tnzi-ai-composer-radius, 16px);
  box-shadow: var(--tnzi-ai-composer-shadow, 0 1px 2px rgba(0, 0, 0, 0.04));
  padding: 14px 4px 10px;
  transition: border-color 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
.t-thread-composer:focus-within {
  border-color: var(--tnzi-ai-text-secondary, #6a6a6a);
}
.t-thread-composer__input {
  width: 100%;
  border: none;
  outline: none;
  resize: none;
  font-family: inherit;
  font-size: 15px;
  line-height: 22px;
  background: transparent;
  color: var(--tnzi-ai-text, #1a1a1a);
  padding: 0 18px;
  min-height: 22px;
  max-height: 200px;
}
.t-thread-composer__input::placeholder {
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
}
.t-thread-composer__bar {
  display: flex;
  align-items: center;
  padding: 8px 10px 0;
}
.t-thread-composer__left,
.t-thread-composer__right {
  display: flex;
  align-items: center;
  gap: 2px;
}
.t-thread-composer__right {
  margin-left: auto;
}
.t-thread-composer__send {
  width: 32px;
  height: 32px;
  border: none;
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  border-radius: 999px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  margin-left: 4px;
  transition:
    background 150ms cubic-bezier(0.4, 0, 0.2, 1),
    color 150ms cubic-bezier(0.4, 0, 0.2, 1);
}
.t-thread-composer__send.is-ready {
  background: var(--tnzi-ai-text, #1a1a1a);
  color: var(--tnzi-ai-bg, #ffffff);
}
.t-thread-composer__send:disabled {
  cursor: not-allowed;
}
</style>
