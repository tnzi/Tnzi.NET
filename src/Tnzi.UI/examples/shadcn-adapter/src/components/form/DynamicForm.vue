<script setup lang="ts">
import { computed } from 'vue';
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
import { Input } from '../primitive/ui/input';
import { Textarea } from '../primitive/ui/textarea';
import { Button } from '../primitive/ui/button';
import { Checkbox } from '../primitive/ui/checkbox';
import { Switch } from '../primitive/ui/switch';
import { Label } from '../primitive/ui/label';
import { RadioGroup, RadioGroupItem } from '../primitive/ui/radio-group';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../primitive/ui/select';

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

const currentModel = computed(() => props.modelValue ?? props.model);
const fieldValue = (key: string) => currentModel.value[key];

const updateField = (field: IDynamicFormField, value: any) => {
  const next = { ...currentModel.value, [field.key]: value };
  emit('update:modelValue', next);
  emit('fieldChange', field.key, value);
};

const onInputField = (field: IDynamicFormField, value: string | number) => {
  updateField(field, String(value));
};

const onTextareaField = (field: IDynamicFormField, value: string | number) => {
  updateField(field, String(value));
};

const onFileInput = (field: IDynamicFormField, event: Event) => {
  const target = event.target as HTMLInputElement;
  updateField(field, target.value);
};

const handleSubmit = () => emit('submit', currentModel.value);
const handleReset = () => emit('reset');
</script>

<template>
  <form class="space-y-4" @submit.prevent="handleSubmit">
    <div :class="containerClass">
      <div v-for="field in props.fields" :key="field.key" class="space-y-2">
        <!-- Label (except for checkbox/switch which have inline labels) -->
        <Label
          v-if="field.label && !['checkbox', 'switch'].includes(field.type)"
          :for="field.key"
          class="text-sm font-medium"
        >
          {{ field.label }}
          <span v-if="field.required" class="text-destructive">*</span>
        </Label>

        <!-- Text / Password / Email / Number / Date / DateTime -->
        <Input
          v-if="['text', 'password', 'email', 'number', 'date', 'datetime'].includes(field.type)"
          :id="field.key"
          :type="field.type === 'datetime' ? 'datetime-local' : field.type"
          :model-value="fieldValue(field.key) ?? ''"
          :placeholder="field.placeholder"
          :disabled="props.disabled || field.disabled"
          @update:model-value="onInputField(field, $event)"
        />

        <!-- Textarea -->
        <Textarea
          v-else-if="field.type === 'textarea'"
          :id="field.key"
          :model-value="fieldValue(field.key) ?? ''"
          :placeholder="field.placeholder"
          :disabled="props.disabled || field.disabled"
          class="min-h-24"
          @update:model-value="onTextareaField(field, $event)"
        />

        <!-- Select -->
        <Select
          v-else-if="field.type === 'select'"
          :model-value="fieldValue(field.key) != null ? String(fieldValue(field.key)) : undefined"
          :disabled="props.disabled || field.disabled"
          @update:model-value="updateField(field, $event)"
        >
          <SelectTrigger class="w-full">
            <SelectValue :placeholder="field.placeholder || t('common.select')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem
              v-for="option in field.options || []"
              :key="String(option.value)"
              :value="String(option.value)"
            >
              {{ option.label }}
            </SelectItem>
          </SelectContent>
        </Select>

        <!-- Checkbox -->
        <div v-else-if="field.type === 'checkbox'" class="flex items-center gap-2">
          <Checkbox
            :id="field.key"
            :checked="Boolean(fieldValue(field.key))"
            :disabled="props.disabled || field.disabled"
            @update:checked="updateField(field, $event)"
          />
          <Label :for="field.key" class="text-sm text-muted-foreground">
            {{ field.placeholder || field.label }}
          </Label>
        </div>

        <!-- Radio Group -->
        <RadioGroup
          v-else-if="field.type === 'radio'"
          :model-value="fieldValue(field.key) != null ? String(fieldValue(field.key)) : undefined"
          :disabled="props.disabled || field.disabled"
          class="flex flex-wrap gap-3"
          @update:model-value="updateField(field, $event)"
        >
          <div
            v-for="option in field.options || []"
            :key="String(option.value)"
            class="flex items-center gap-2"
          >
            <RadioGroupItem :id="`${field.key}-${option.value}`" :value="String(option.value)" />
            <Label :for="`${field.key}-${option.value}`" class="text-sm">
              {{ option.label }}
            </Label>
          </div>
        </RadioGroup>

        <!-- Switch -->
        <div v-else-if="field.type === 'switch'" class="flex items-center gap-2">
          <Switch
            :id="field.key"
            :checked="Boolean(fieldValue(field.key))"
            :disabled="props.disabled || field.disabled"
            @update:checked="updateField(field, $event)"
          />
          <Label :for="field.key" class="text-sm text-muted-foreground">
            {{ field.placeholder || field.label }}
          </Label>
        </div>

        <!-- File -->
        <input
          v-else-if="field.type === 'file'"
          :id="field.key"
          type="file"
          :disabled="props.disabled || field.disabled"
          class="w-full cursor-pointer rounded-md border bg-background px-3 py-2 text-sm"
          @change="onFileInput(field, $event)"
        />
      </div>
    </div>

    <div class="flex justify-end gap-2 pt-2">
      <Button
        type="button"
        variant="default"
        :disabled="props.disabled"
        @click="handleReset"
      >
        {{ t('common.reset') }}
      </Button>
      <Button
        type="submit"
        :disabled="props.disabled"
      >
        {{ t('common.submit') }}
      </Button>
    </div>
  </form>
</template>
