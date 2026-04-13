<template>
  <div class="t-field" :class="{ 't-field--required': required, 't-field--error': !!error }">
    <label v-if="label" class="t-field__label">
      {{ label }}
      <span v-if="required" class="t-field__required-mark">*</span>
    </label>
    <div class="t-field__control">
      <slot :error="error" />
    </div>
    <div v-if="error" class="t-field__error">{{ error }}</div>
    <div v-else-if="hint" class="t-field__hint">{{ hint }}</div>
  </div>
</template>

<script setup lang="ts">
interface Props {
  label?: string
  error?: string
  hint?: string
  required?: boolean
}

defineProps<Props>()
</script>

<style scoped>
.t-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.t-field__label {
  font-size: 14px;
  color: var(--tnzi-base-text);
  font-weight: 500;
}
.t-field__required-mark {
  color: var(--tnzi-error-500);
  margin-left: 2px;
}
.t-field__error {
  font-size: 13px;
  color: var(--tnzi-error-500);
}
.t-field__hint {
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
</style>
