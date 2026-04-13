<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IDynamicFormField } from '@tnzi/core/types/shared-ui';

interface IDynamicFormProps {
  model: Record<string, unknown>;
  fields: IDynamicFormField[];
  inline?: boolean;
  disabled?: boolean;
  size?: 'small' | 'medium' | 'large';
  class?: string | string[];
  style?: string | Record<string, string | number>;
}

interface IDynamicFormEmits {
  submit: [data: Record<string, unknown>];
  reset: [];
  fieldChange: [key: string, value: unknown];
}

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
type InputValue = string | number | undefined;

const getInputValue = (key: string): InputValue => {
  const value = formData.value[key];
  if (typeof value === 'string' || typeof value === 'number') {
    return value;
  }
  if (value == null) {
    return undefined;
  }
  return String(value);
};

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

// --- Select popup state ---
const selectPopupVisible = ref<Record<string, boolean>>({});
const openSelectPopup = (key: string) => {
  selectPopupVisible.value = { ...selectPopupVisible.value, [key]: true };
};
const closeSelectPopup = (key: string) => {
  selectPopupVisible.value = { ...selectPopupVisible.value, [key]: false };
};
const onSelectConfirm = (key: string, { selectedOptions }: { selectedOptions: Array<{ text: string; value: string | number }> }) => {
  const option = selectedOptions[0];
  if (option) {
    updateField(key, option.value);
  }
  closeSelectPopup(key);
};

const getSelectDisplayText = (field: { key: string; options?: Array<{ label: string; value: string | number }> }) => {
  const val = formData.value[field.key];
  const opt = field.options?.find(o => o.value === val);
  return opt?.label ?? '';
};

const toPickerColumns = (options?: Array<{ label: string; value: string | number }>) => {
  return (options ?? []).map(o => ({ text: o.label, value: o.value }));
};
</script>

<template>
  <van-form @submit="handleSubmit">
    <van-cell-group inset>
      <template v-for="field in props.fields" :key="field.key">
        <!-- text / password / email / number -->
        <van-field
          v-if="['text', 'password', 'email', 'number'].includes(field.type)"
          :model-value="getInputValue(field.key)"
          :type="toVanFieldType(field.type)"
          :label="field.label"
          :placeholder="field.placeholder"
          :disabled="props.disabled || field.disabled"
          @update:model-value="updateField(field.key, $event)"
        />

        <!-- textarea -->
        <van-field
          v-else-if="field.type === 'textarea'"
          :model-value="getInputValue(field.key)"
          type="textarea"
          rows="3"
          :label="field.label"
          :placeholder="field.placeholder"
          :disabled="props.disabled || field.disabled"
          @update:model-value="updateField(field.key, $event)"
        />

        <!-- select -> van-field + van-popup + van-picker -->
        <template v-else-if="field.type === 'select'">
          <van-field
            :model-value="getSelectDisplayText(field)"
            is-link
            readonly
            :label="field.label"
            :placeholder="field.placeholder || t('common.select')"
            :disabled="props.disabled || field.disabled"
            @click="openSelectPopup(field.key)"
          />
          <van-popup v-model:show="selectPopupVisible[field.key]" round position="bottom">
            <van-picker
              :columns="toPickerColumns(field.options)"
              @confirm="onSelectConfirm(field.key, $event)"
              @cancel="closeSelectPopup(field.key)"
            />
          </van-popup>
        </template>

        <!-- radio -> van-radio-group -->
        <van-field v-else-if="field.type === 'radio'" :label="field.label">
          <template #input>
            <van-radio-group
              :model-value="formData[field.key]"
              direction="horizontal"
              @update:model-value="updateField(field.key, $event)"
            >
              <van-radio
                v-for="opt in field.options"
                :key="String(opt.value)"
                :name="opt.value"
                :disabled="props.disabled || field.disabled"
              >
                {{ opt.label }}
              </van-radio>
            </van-radio-group>
          </template>
        </van-field>

        <!-- checkbox (single boolean) -->
        <van-field v-else-if="field.type === 'checkbox'" :label="field.label">
          <template #input>
            <van-checkbox
              :model-value="!!formData[field.key]"
              :disabled="props.disabled || field.disabled"
              @update:model-value="updateField(field.key, $event)"
            />
          </template>
        </van-field>

        <!-- switch -->
        <van-field v-else-if="field.type === 'switch'" :label="field.label">
          <template #input>
            <van-switch
              :model-value="!!formData[field.key]"
              :disabled="props.disabled || field.disabled"
              @update:model-value="updateField(field.key, $event)"
            />
          </template>
        </van-field>

        <!-- date -- basic text field -->
        <!-- TODO: integrate van-date-picker popup for proper date selection -->
        <van-field
          v-else-if="field.type === 'date'"
          :model-value="getInputValue(field.key)"
          :label="field.label"
          :placeholder="field.placeholder || 'YYYY-MM-DD'"
          :disabled="props.disabled || field.disabled"
          @update:model-value="updateField(field.key, $event)"
        />

        <!-- datetime -- basic text field -->
        <!-- TODO: integrate van-date-picker popup for proper datetime selection -->
        <van-field
          v-else-if="field.type === 'datetime'"
          :model-value="getInputValue(field.key)"
          :label="field.label"
          :placeholder="field.placeholder || 'YYYY-MM-DD HH:mm'"
          :disabled="props.disabled || field.disabled"
          @update:model-value="updateField(field.key, $event)"
        />

        <!-- file -- basic text field (read-only display) -->
        <!-- TODO: integrate file upload component -->
        <van-field
          v-else-if="field.type === 'file'"
          :model-value="getInputValue(field.key)"
          :label="field.label"
          :placeholder="field.placeholder"
          :disabled="props.disabled || field.disabled"
          readonly
        />
      </template>
    </van-cell-group>

    <div class="mt-4 flex gap-2 px-4">
      <van-button plain block @click="handleReset">{{ t('common.reset') }}</van-button>
      <van-button type="primary" native-type="submit" block>{{ t('common.submit') }}</van-button>
    </div>
  </van-form>
</template>
