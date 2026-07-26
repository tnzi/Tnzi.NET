<script setup lang="ts">
import { ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IFormRule } from '@tnzi/core/types/shared-ui';

interface IFormProps<T = Record<string, unknown>> {
  model: T;
  rules?: Record<string, IFormRule[]>;
  labelWidth?: number | string;
  labelPlacement?: 'left' | 'top';
  disabled?: boolean;
  showRequireMark?: boolean;
  size?: 'small' | 'medium' | 'large';
}

interface IFormEmits<T = Record<string, unknown>> {
  submit: [data: T];
  reset: [];
  validateError: [errors: Record<string, string[]>];
}

const props = withDefaults(defineProps<IFormProps<Record<string, unknown>>>(), {
  rules: () => ({}),
  labelWidth: '100px',
  labelPlacement: 'top',
  disabled: false,
  showRequireMark: true,
  size: 'medium',
});

const emit = defineEmits<IFormEmits<Record<string, unknown>>>();
const { t } = useI18n();

const errors = ref<Record<string, string[]>>({});

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

const applyRule = async (rule: IFormRule, value: unknown): Promise<string | undefined> => {
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
    if (fieldErrors.length > 0) nextErrors[key] = fieldErrors;
  }

  errors.value = nextErrors;
  if (Object.keys(nextErrors).length > 0) {
    emit('validateError', nextErrors);
    return false;
  }
  return true;
};

const handleSubmit = async () => {
  const ok = await validate();
  if (ok) emit('submit', props.model);
};

const handleReset = () => {
  errors.value = {};
  emit('reset');
};
</script>

<template>
  <van-form @submit="handleSubmit">
    <slot
      :model="props.model"
      :rules="props.rules"
      :errors="errors"
      :disabled="props.disabled"
      :label-placement="props.labelPlacement"
      :label-width="props.labelWidth"
      :show-require-mark="props.showRequireMark"
      :size="props.size"
      :validate="validate"
      :validate-field="validateField"
      :reset="handleReset"
    />

    <div class="actions">
      <slot name="actions" :submit="handleSubmit" :reset="handleReset" :disabled="props.disabled">
        <van-button size="small" plain :disabled="props.disabled" @click="handleReset">
          {{ t('common.reset') }}
        </van-button>
        <van-button native-type="submit" type="primary" size="small" :disabled="props.disabled">
          {{ t('common.submit') }}
        </van-button>
      </slot>
    </div>
  </van-form>
</template>

<style scoped>
.actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 12px;
}
</style>

