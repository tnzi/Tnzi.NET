<script setup lang="ts">
/**
 * A single auth input.
 *
 * Deliberately a bare `<input>` rather than `NInput`: this page renders before
 * the app shell exists (it IS the pre-auth surface), so it must not depend on
 * a Naive UI provider tree being mounted. The visual is one border, one radius
 * and one focus ring - not worth a provider dependency.
 */
withDefaults(
  defineProps<{
    type?: 'text' | 'password' | 'email';
    placeholder?: string;
    name?: string;
    autocomplete?: string;
    inputmode?: 'text' | 'numeric' | 'email' | 'tel';
    disabled?: boolean;
    /** 16px text: iOS Safari zooms the page into any input set smaller than that. */
    zoomSafe?: boolean;
  }>(),
  {
    type: 'text',
    placeholder: '',
    name: undefined,
    autocomplete: 'off',
    inputmode: 'text',
    disabled: false,
    zoomSafe: false,
  },
);

import { isImeComposing } from '@tnzi/core/utils';

const model = defineModel<string>({ default: '' });

/** Enter submits - a one-field pane where Enter does nothing feels broken. */
const emit = defineEmits<{ (e: 'submit'): void }>();

/**
 * Except the Enter that commits an IME candidate: `v-model` has not seen the
 * composed text yet, so submitting then sends what was there before it.
 */
function onEnter(event: KeyboardEvent): void {
  if (isImeComposing(event)) return;
  event.preventDefault();
  emit('submit');
}
</script>

<template>
  <input
    v-model="model"
    class="t-auth-field"
    :class="{ 't-auth-field--zoom-safe': zoomSafe }"
    :type="type"
    :placeholder="placeholder"
    :name="name"
    :autocomplete="autocomplete"
    autocapitalize="off"
    autocorrect="off"
    spellcheck="false"
    :inputmode="inputmode"
    :disabled="disabled"
    @keydown.enter="onEnter"
  />
</template>

<style scoped>
.t-auth-field {
  height: 40px;
  padding: 0 12px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 8px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-size: 14px;
  font-family: inherit;
  outline: none;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
  width: 100%;
  box-sizing: border-box;
}

.t-auth-field--zoom-safe {
  font-size: 16px;
}

.t-auth-field::placeholder {
  color: var(--tnzi-ai-text-tertiary);
}

.t-auth-field:focus {
  border-color: var(--tnzi-ai-focus-border);
  box-shadow: var(--tnzi-ai-focus-ring);
}

/* A field the page marks wrong (`aria-invalid="true"`, which falls through to
   the input): a danger border, and the error focus ring when focused. */
.t-auth-field[aria-invalid='true'] {
  border-color: var(--tnzi-ai-danger);
}
.t-auth-field[aria-invalid='true']:focus {
  border-color: var(--tnzi-ai-focus-border-error);
  box-shadow: var(--tnzi-ai-focus-ring-error);
}

.t-auth-field:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
</style>
