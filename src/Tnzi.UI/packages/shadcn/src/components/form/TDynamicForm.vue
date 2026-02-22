<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IDynamicFormEmits, IDynamicFormField, IDynamicFormProps } from '@tnzi/core/components';

type DynamicFormCompatProps = IDynamicFormProps & { modelValue?: Record<string, any> };

const props = withDefaults(defineProps<DynamicFormCompatProps>(), {
  model: () => ({}),
  modelValue: undefined,
  fields: () => [],
  inline: false,
  disabled: false,
  size: 'medium',
});

const emit = defineEmits<IDynamicFormEmits & { 'update:modelValue': [value: Record<string, any>] }>();

const { t } = useI18n();

const containerClass = computed(() => {
  if (props.inline) {
    return 'grid gap-4 md:grid-cols-2';
  }
  return 'space-y-4';
});

const inputSizeClass = computed(() => {
  const map = {
    small: 'h-8 text-xs',
    medium: 'h-10 text-sm',
    large: 'h-11 text-base',
  } as const;

  return map[props.size];
});

const currentModel = computed(() => props.modelValue ?? props.model);
const fieldValue = (key: string) => currentModel.value[key];

const updateField = (field: IDynamicFormField, value: any) => {
  const next = { ...currentModel.value, [field.key]: value };
  emit('update:modelValue', next);
  emit('fieldChange', field.key, value);
};

const onFieldInput = (field: IDynamicFormField, event: Event) => {
  const target = event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;
  if (field.type === 'checkbox') {
    updateField(field, (target as HTMLInputElement).checked);
    return;
  }
  updateField(field, target.value);
};

const handleSubmit = () => emit('submit', currentModel.value);
const handleReset = () => emit('reset');
</script>

<template>
  <form class="space-y-4" @submit.prevent="handleSubmit">
    <div :class="containerClass">
      <div v-for="field in props.fields" :key="field.key" class="space-y-2">
        <label v-if="field.label" class="text-sm font-medium">
          {{ field.label }}
          <span v-if="field.required" class="text-destructive">*</span>
        </label>

        <input
          v-if="['text', 'password', 'email', 'number', 'date', 'datetime'].includes(field.type)"
          :type="field.type === 'datetime' ? 'datetime-local' : field.type"
          :value="fieldValue(field.key)"
          :placeholder="field.placeholder"
          :disabled="props.disabled || field.disabled"
          class="w-full rounded-md border bg-background px-3 outline-none ring-offset-background transition focus-visible:ring-2 focus-visible:ring-ring"
          :class="inputSizeClass"
          @input="onFieldInput(field, $event)"
        />

        <textarea
          v-else-if="field.type === 'textarea'"
          :value="fieldValue(field.key)"
          :placeholder="field.placeholder"
          :disabled="props.disabled || field.disabled"
          class="min-h-24 w-full rounded-md border bg-background px-3 py-2 text-sm outline-none ring-offset-background transition focus-visible:ring-2 focus-visible:ring-ring"
          @input="onFieldInput(field, $event)"
        />

        <select
          v-else-if="field.type === 'select'"
          :value="fieldValue(field.key)"
          :disabled="props.disabled || field.disabled"
          class="w-full rounded-md border bg-background px-3 outline-none ring-offset-background transition focus-visible:ring-2 focus-visible:ring-ring"
          :class="inputSizeClass"
          @change="onFieldInput(field, $event)"
        >
          <option value="">{{ field.placeholder || t('common.select') }}</option>
          <option v-for="option in field.options || []" :key="String(option.value)" :value="option.value">
            {{ option.label }}
          </option>
        </select>

        <label v-else-if="field.type === 'checkbox'" class="flex items-center gap-2 text-sm text-muted-foreground">
          <input
            type="checkbox"
            :checked="Boolean(fieldValue(field.key))"
            :disabled="props.disabled || field.disabled"
            @change="onFieldInput(field, $event)"
          />
          <span>{{ field.placeholder || field.label }}</span>
        </label>

        <div v-else-if="field.type === 'radio'" class="flex flex-wrap gap-3">
          <label
            v-for="option in field.options || []"
            :key="String(option.value)"
            class="flex items-center gap-2 text-sm"
          >
            <input
              type="radio"
              :name="field.key"
              :value="option.value"
              :checked="fieldValue(field.key) === option.value"
              :disabled="props.disabled || field.disabled"
              @change="updateField(field, option.value)"
            />
            {{ option.label }}
          </label>
        </div>

        <label v-else-if="field.type === 'switch'" class="flex items-center gap-2 text-sm text-muted-foreground">
          <input
            type="checkbox"
            :checked="Boolean(fieldValue(field.key))"
            :disabled="props.disabled || field.disabled"
            @change="onFieldInput(field, $event)"
          />
          <span>{{ field.placeholder || field.label }}</span>
        </label>

        <input
          v-else-if="field.type === 'file'"
          type="file"
          :disabled="props.disabled || field.disabled"
          class="w-full cursor-pointer rounded-md border bg-background px-3 py-2 text-sm"
          @change="onFieldInput(field, $event)"
        />
      </div>
    </div>

    <div class="flex justify-end gap-2 pt-2">
      <button
        type="button"
        class="rounded-md border bg-background px-4 py-2 text-sm transition hover:bg-accent disabled:opacity-50"
        :disabled="props.disabled"
        @click="handleReset"
      >
        {{ t('common.reset') }}
      </button>
      <button
        type="submit"
        class="rounded-md bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
        :disabled="props.disabled"
      >
        {{ t('common.submit') }}
      </button>
    </div>
  </form>
</template>

