<script setup lang="ts">
/**
 * TField - a labelled form field: the label above, the control in the default
 * slot, then either the error (in the danger colour, announced) or the hint.
 *
 * The slot receives `id` (put it on the control: the label's `for` points at
 * it), `describedBy` (the id of the line under it, for `aria-describedby`;
 * undefined when there is none) and `invalid` (for `aria-invalid`):
 *
 * ```vue
 * <TField label="Name" :error="nameError">
 *   <template #default="{ id, describedBy, invalid }">
 *     <input :id="id" v-model="name" class="t-input" :aria-describedby="describedBy" :aria-invalid="invalid" />
 *   </template>
 * </TField>
 * ```
 */
import { computed, useId } from 'vue'

const props = withDefaults(
  defineProps<{
    label: string
    hint?: string
    error?: string | null
    /** Marks the label with an asterisk. The control's own `required` is the caller's. */
    required?: boolean
  }>(),
  { hint: '', error: null, required: false },
)

const id = useId()
const describedBy = computed(() => (props.error || props.hint ? `${id}-desc` : undefined))
</script>

<template>
  <div class="t-field" :class="{ 'is-invalid': !!error }">
    <label class="t-field__label" :for="id">
      {{ label }}<span v-if="required" class="t-field__required" aria-hidden="true">*</span>
    </label>
    <slot :id="id" :described-by="describedBy" :invalid="!!error" />
    <p v-if="error" :id="`${id}-desc`" class="t-field__error" role="alert">{{ error }}</p>
    <p v-else-if="hint" :id="`${id}-desc`" class="t-field__hint">{{ hint }}</p>
  </div>
</template>

<style scoped>
.t-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}
.t-field__label {
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-ai-text);
}
.t-field__required {
  margin-left: 2px;
  color: var(--tnzi-ai-danger);
}
.t-field__hint,
.t-field__error {
  margin: 0;
  font-size: 12px;
  line-height: 1.5;
  overflow-wrap: anywhere;
}
.t-field__hint {
  color: var(--tnzi-ai-text-tertiary);
}
.t-field__error {
  color: var(--tnzi-ai-danger);
}
</style>
