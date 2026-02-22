<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IDynamicFormEmits, IDynamicFormProps } from '@tnzi/core/components';

const props = withDefaults(defineProps<IDynamicFormProps>(), {
  model: () => ({}),
  fields: () => [],
  inline: false,
  disabled: false,
  size: 'medium',
});

const emit = defineEmits<IDynamicFormEmits & { 'update:modelValue': [value: Record<string, unknown>] }>();

const { t } = useI18n();

const formData = computed(() => props.model);

const toVanFieldType = (type: string): 'text' | 'number' | 'password' | 'textarea' => {
  if (type === 'number') return 'number';
  if (type === 'password') return 'password';
  if (type === 'textarea') return 'textarea';
  return 'text';
};

const updateField = (key: string, value: unknown) => {
  const next = { ...formData.value, [key]: value };
  emit('update:modelValue', next);
  emit('fieldChange', key, value);
};

const handleSubmit = () => emit('submit', formData.value);
const handleReset = () => emit('reset');
</script>

<template>
  <van-form @submit="handleSubmit">
    <van-cell-group inset>
      <template v-for="field in props.fields" :key="field.key">
        <van-field
          v-if="['text', 'password', 'email', 'number'].includes(field.type)"
          :model-value="formData[field.key]"
          :type="toVanFieldType(field.type)"
          :label="field.label"
          :placeholder="field.placeholder"
          :disabled="props.disabled || field.disabled"
          @update:model-value="updateField(field.key, $event)"
        />

        <van-field
          v-else-if="field.type === 'textarea'"
          :model-value="formData[field.key]"
          type="textarea"
          rows="3"
          :label="field.label"
          :placeholder="field.placeholder"
          :disabled="props.disabled || field.disabled"
          @update:model-value="updateField(field.key, $event)"
        />
      </template>
    </van-cell-group>

    <div class="mt-4 flex gap-2 px-4">
      <van-button plain block @click="handleReset">{{ t('common.reset') }}</van-button>
      <van-button type="primary" native-type="submit" block>{{ t('common.submit') }}</van-button>
    </div>
  </van-form>
</template>

