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
    autocomplete?: string;
    inputmode?: 'text' | 'numeric' | 'email' | 'tel';
    disabled?: boolean;
  }>(),
  {
    type: 'text',
    placeholder: '',
    autocomplete: 'off',
    inputmode: 'text',
    disabled: false,
  },
);

const model = defineModel<string>({ default: '' });

/** Enter submits - a one-field pane where Enter does nothing feels broken. */
const emit = defineEmits<{ (e: 'submit'): void }>();
</script>

<template>
  <input
    v-model="model"
    class="t-auth-field"
    :type="type"
    :placeholder="placeholder"
    :autocomplete="autocomplete"
    :inputmode="inputmode"
    :disabled="disabled"
    @keydown.enter.prevent="emit('submit')"
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

.t-auth-field::placeholder {
  color: var(--tnzi-ai-text-tertiary);
}

.t-auth-field:focus {
  border-color: var(--tnzi-ai-accent);
}

.t-auth-field:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
</style>
