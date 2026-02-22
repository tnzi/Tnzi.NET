<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IFormEmits, IFormProps, IFormRule } from '@tnzi/core/components';
import { Button } from '../primitive/ui';

const props = withDefaults(defineProps<IFormProps<Record<string, any>>>(), {
  rules: () => ({}),
  labelWidth: '120px',
  labelPlacement: 'top',
  disabled: false,
  showRequireMark: true,
  size: 'medium',
});

const emit = defineEmits<IFormEmits<Record<string, any>> & {
  /** Emitted when submitting state changes */
  'update:submitting': [value: boolean];
}>();
const { t } = useI18n();

const errors = ref<Record<string, string[]>>({});
const isSubmitting = ref(false);

const formClass = computed(() => [
  'space-y-4',
  props.labelPlacement === 'left' ? 'grid gap-4 md:grid-cols-2' : '',
  props.class,
]);

const validateField = async (key: string): Promise<string[]> => {
  const rules = props.rules?.[key] ?? [];
  const value = props.model[key];
  const fieldErrors: string[] = [];

  for (const rule of rules) {
    const result = await applyRule(rule, value);
    if (result) fieldErrors.push(result);
  }

  return fieldErrors;
};

const applyRule = async (rule: IFormRule, value: any): Promise<string | undefined> => {
  if (rule.required && (value === undefined || value === null || value === '')) {
    return rule.message || t('common.required');
  }

  if (rule.min != null && typeof value === 'string' && value.length < rule.min) {
    return rule.message || t('common.minLength');
  }

  if (rule.max != null && typeof value === 'string' && value.length > rule.max) {
    return rule.message || t('common.maxLength');
  }

  if (rule.pattern && typeof value === 'string' && !rule.pattern.test(value)) {
    return rule.message || t('common.invalidFormat');
  }

  if (rule.validator) {
    const result = await rule.validator(value);
    if (result === false) return rule.message || t('common.invalid');
    if (typeof result === 'string') return result;
  }

  return undefined;
};

const validate = async () => {
  const nextErrors: Record<string, string[]> = {};
  const keys = Object.keys(props.rules ?? {});

  for (const key of keys) {
    const fieldErrors = await validateField(key);
    if (fieldErrors.length > 0) {
      nextErrors[key] = fieldErrors;
    }
  }

  errors.value = nextErrors;
  if (Object.keys(nextErrors).length > 0) {
    emit('validateError', nextErrors);
    return false;
  }

  return true;
};

const handleSubmit = async () => {
  if (isSubmitting.value) return;

  isSubmitting.value = true;
  emit('update:submitting', true);
  try {
    const ok = await validate();
    if (ok) emit('submit', props.model);
  } finally {
    isSubmitting.value = false;
    emit('update:submitting', false);
  }
};

const handleReset = () => {
  errors.value = {};
  emit('reset');
};
</script>

<template>
  <form :class="formClass" :style="props.style" @submit.prevent="handleSubmit">
    <slot
      :model="props.model"
      :rules="props.rules"
      :errors="errors"
      :disabled="props.disabled"
      :submitting="isSubmitting"
      :label-placement="props.labelPlacement"
      :label-width="props.labelWidth"
      :show-require-mark="props.showRequireMark"
      :size="props.size"
      :validate="validate"
      :validate-field="validateField"
      :reset="handleReset"
    />

    <div class="flex justify-end gap-2">
      <slot name="actions" :submit="handleSubmit" :reset="handleReset" :disabled="props.disabled" :submitting="isSubmitting">
        <Button type="button" variant="outline" :disabled="props.disabled || isSubmitting" @click="handleReset">
          {{ t('common.reset') }}
        </Button>
        <Button type="submit" :disabled="props.disabled || isSubmitting">
          {{ t('common.submit') }}
        </Button>
      </slot>
    </div>
  </form>
</template>

