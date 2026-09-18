<script setup lang="ts">
import { ref } from 'vue';
import type { FieldRule, UploaderFileListItem } from 'vant';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IDynamicFormField, IFormRule } from '@tnzi/core/types/shared-ui';
import { useDynamicForm } from '../../headless/useDynamicForm';

interface IDynamicFormProps {
  /** Form model. Two-way bound: `v-model="model"`. */
  modelValue?: Record<string, unknown>;
  fields?: IDynamicFormField[];
  disabled?: boolean;
  /** Tab title of the date step in the datetime picker. Override to localize. */
  dateTabLabel?: string;
  /** Tab title of the time step in the datetime picker. Override to localize. */
  timeTabLabel?: string;
}

interface IDynamicFormEmits {
  'update:modelValue': [value: Record<string, unknown>];
  submit: [data: Record<string, unknown>];
  reset: [];
  fieldChange: [key: string, value: unknown];
}

const props = withDefaults(defineProps<IDynamicFormProps>(), {
  modelValue: () => ({}),
  fields: () => [],
  disabled: false,
  dateTabLabel: 'Date',
  timeTabLabel: 'Time',
});

const emit = defineEmits<IDynamicFormEmits>();

const { t } = useI18n();

// Model plumbing lives in the headless composable; the options are getters so
// the composable keeps reading the live prop.
const form = useDynamicForm({
  get modelValue() {
    return props.modelValue;
  },
  onUpdateModelValue: (next) => emit('update:modelValue', next),
  onFieldChange: (key, value) => emit('fieldChange', key, value),
  onSubmit: (data) => emit('submit', data),
  onReset: () => emit('reset'),
});

const formData = form.currentModel;

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

const getArrayValue = (key: string): unknown[] => {
  const value = formData.value[key];
  return Array.isArray(value) ? value : [];
};

/**
 * The value a slot-rendered field (radio / checkbox / switch / file) hands to
 * van-field for validation only; the control inside `#input` renders it. Vant
 * types the prop as string | number, but its required rule reads any value and
 * counts `false` and `[]` as missing, which is exactly what a consent switch and
 * an empty upload need. Not `getInputValue`: that stringifies `false` to 'false'.
 */
const getValidationValue = (key: string) => formData.value[key] as string | number | undefined;

const updateField = (key: string, value: unknown) => form.updateField({ key }, value);

const handleSubmit = () => form.handleSubmit();
const handleReset = () => form.handleReset();

// --- Validation -------------------------------------------------------------

const toVantTrigger = (trigger?: IFormRule['trigger']) => (trigger === 'blur' ? 'onBlur' : 'onChange');

/** Expand one contract rule into the Vant rules that express it. */
const toVantRules = (rule: IFormRule): FieldRule[] => {
  const trigger = toVantTrigger(rule.trigger);
  const rules: FieldRule[] = [];

  if (rule.required) rules.push({ required: true, message: rule.message ?? t('form.required'), trigger });
  if (rule.pattern) {
    rules.push({ pattern: rule.pattern, message: rule.message ?? t('form.invalidFormat'), trigger });
  }
  if (rule.min != null) {
    rules.push({
      validator: (value: unknown) => String(value ?? '').length >= rule.min!,
      message: rule.message ?? t('form.minLength', { min: rule.min }),
      trigger,
    });
  }
  if (rule.max != null) {
    rules.push({
      validator: (value: unknown) => String(value ?? '').length <= rule.max!,
      message: rule.message ?? t('form.maxLength', { max: rule.max }),
      trigger,
    });
  }
  if (rule.validator) {
    rules.push({
      // Vant treats a returned string as the error message, matching IFormRule.
      validator: (value: unknown) => rule.validator!(value),
      message: rule.message ?? t('common.invalid'),
      trigger,
    });
  }

  return rules;
};

/** Human-readable bound for numeric fields, built from keys that exist. */
const rangeMessage = (min?: number, max?: number) => {
  if (min != null && max != null) return `${t('common.invalid')} (${min} - ${max})`;
  if (min != null) return `${t('common.invalid')} (>= ${min})`;
  return `${t('common.invalid')} (<= ${max})`;
};

/**
 * Field rules derived from the contract.
 *
 * `required` applies to every type: Vant's required rule reads an empty string,
 * null / undefined, an empty array (file, multi-checkbox) and `false` as
 * missing, so on a switch or checkbox it means "must be on" (a consent gate).
 * `min` / `max` are read as numeric bounds for `number` fields, as a file count
 * for `file` fields (applied as the uploader's max-count, not as a rule), as a
 * selection count for a checkbox group (a checkbox with `options` holds an
 * array; the text-length rule would measure the joined string), and as text
 * length everywhere else.
 */
const fieldRules = (field: IDynamicFormField): FieldRule[] => {
  const rules: FieldRule[] = [];

  if (field.required) {
    rules.push({ required: true, message: t('form.required') });
  }

  const hasBound = field.min != null || field.max != null;
  if (hasBound && field.type === 'number') {
    rules.push({
      validator: (value: unknown) => {
        if (value === '' || value == null) return true;
        const numeric = Number(value);
        if (Number.isNaN(numeric)) return false;
        if (field.min != null && numeric < field.min) return false;
        if (field.max != null && numeric > field.max) return false;
        return true;
      },
      message: rangeMessage(field.min, field.max),
    });
  } else if (hasBound && field.type === 'checkbox' && field.options?.length) {
    const count = (value: unknown) => (Array.isArray(value) ? value.length : 0);
    if (field.min != null) {
      rules.push({
        validator: (value: unknown) => count(value) >= field.min!,
        message: t('form.minSelected', { min: field.min }),
      });
    }
    if (field.max != null) {
      rules.push({
        validator: (value: unknown) => count(value) <= field.max!,
        message: t('form.maxSelected', { max: field.max }),
      });
    }
  } else if (hasBound && field.type !== 'file') {
    if (field.min != null) {
      rules.push({
        validator: (value: unknown) => String(value ?? '').length >= field.min!,
        message: t('form.minLength', { min: field.min }),
      });
    }
    if (field.max != null) {
      rules.push({
        validator: (value: unknown) => String(value ?? '').length <= field.max!,
        message: t('form.maxLength', { max: field.max }),
      });
    }
  }

  for (const rule of field.rules ?? []) rules.push(...toVantRules(rule));

  return rules;
};

// --- Popup pickers (select / date / datetime) -------------------------------

const popupVisible = ref<Record<string, boolean>>({});
const dateDraft = ref<Record<string, string[]>>({});
const timeDraft = ref<Record<string, string[]>>({});

const pad = (value: number) => String(value).padStart(2, '0');

const toDateColumns = (value: InputValue): string[] => {
  const matched = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(value ?? ''));
  if (matched) return [matched[1]!, matched[2]!, matched[3]!];
  const now = new Date();
  return [String(now.getFullYear()), pad(now.getMonth() + 1), pad(now.getDate())];
};

const toTimeColumns = (value: InputValue): string[] => {
  const matched = /(\d{2}):(\d{2})/.exec(String(value ?? ''));
  if (matched) return [matched[1]!, matched[2]!];
  return ['00', '00'];
};

const openPopup = (field: IDynamicFormField) => {
  if (props.disabled || field.disabled) return;
  if (field.type === 'date' || field.type === 'datetime') {
    dateDraft.value = { ...dateDraft.value, [field.key]: toDateColumns(getInputValue(field.key)) };
  }
  if (field.type === 'datetime') {
    timeDraft.value = { ...timeDraft.value, [field.key]: toTimeColumns(getInputValue(field.key)) };
  }
  popupVisible.value = { ...popupVisible.value, [field.key]: true };
};

const closePopup = (key: string) => {
  popupVisible.value = { ...popupVisible.value, [key]: false };
};

const onSelectConfirm = (key: string, { selectedOptions }: { selectedOptions: Array<{ text: string; value: string | number }> }) => {
  const option = selectedOptions[0];
  if (option) {
    updateField(key, option.value);
  }
  closePopup(key);
};

const getSelectDisplayText = (field: IDynamicFormField) => {
  const value = formData.value[field.key];
  return field.options?.find(option => option.value === value)?.label ?? '';
};

const toPickerColumns = (options?: Array<{ label: string; value: string | number }>) => {
  return (options ?? []).map(option => ({ text: option.label, value: option.value }));
};

const setDateDraft = (key: string, value: string[]) => {
  dateDraft.value = { ...dateDraft.value, [key]: value };
};

const setTimeDraft = (key: string, value: string[]) => {
  timeDraft.value = { ...timeDraft.value, [key]: value };
};

const onDateConfirm = (key: string) => {
  updateField(key, (dateDraft.value[key] ?? []).join('-'));
  closePopup(key);
};

const onDateTimeConfirm = (key: string) => {
  const date = (dateDraft.value[key] ?? []).join('-');
  const time = (timeDraft.value[key] ?? []).join(':');
  updateField(key, `${date} ${time}`);
  closePopup(key);
};

// --- File upload ------------------------------------------------------------

const getFileList = (key: string): UploaderFileListItem[] => {
  const value = formData.value[key];
  return Array.isArray(value) ? (value as UploaderFileListItem[]) : [];
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
          :name="field.key"
          :type="toVanFieldType(field.type)"
          :label="field.label"
          :placeholder="field.placeholder"
          :required="field.required"
          :rules="fieldRules(field)"
          :disabled="props.disabled || field.disabled"
          v-bind="field.props"
          @update:model-value="updateField(field.key, $event)"
        />

        <!-- textarea -->
        <van-field
          v-else-if="field.type === 'textarea'"
          :model-value="getInputValue(field.key)"
          :name="field.key"
          type="textarea"
          rows="3"
          :label="field.label"
          :placeholder="field.placeholder"
          :required="field.required"
          :rules="fieldRules(field)"
          :disabled="props.disabled || field.disabled"
          v-bind="field.props"
          @update:model-value="updateField(field.key, $event)"
        />

        <!-- select -> van-field + van-popup + van-picker -->
        <template v-else-if="field.type === 'select'">
          <van-field
            :model-value="getSelectDisplayText(field)"
            :name="field.key"
            is-link
            readonly
            :label="field.label"
            :placeholder="field.placeholder || t('common.select')"
            :required="field.required"
            :rules="fieldRules(field)"
            :disabled="props.disabled || field.disabled"
            v-bind="field.props"
            @click="openPopup(field)"
          />
          <van-popup v-model:show="popupVisible[field.key]" round position="bottom">
            <van-picker
              :columns="toPickerColumns(field.options)"
              @confirm="onSelectConfirm(field.key, $event)"
              @cancel="closePopup(field.key)"
            />
          </van-popup>
        </template>

        <!-- radio -> van-radio-group. The field carries name / model-value / rules
             so van-form validates it like any other: a slot-rendered control
             without them gets the asterisk and nothing else. -->
        <van-field
          v-else-if="field.type === 'radio'"
          :name="field.key"
          :model-value="getValidationValue(field.key)"
          :label="field.label"
          :required="field.required"
          :rules="fieldRules(field)"
        >
          <template #input>
            <van-radio-group
              :model-value="formData[field.key]"
              direction="horizontal"
              v-bind="field.props"
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

        <!-- checkbox: a group over `options` (array value), else a single boolean -->
        <van-field
          v-else-if="field.type === 'checkbox'"
          :name="field.key"
          :model-value="getValidationValue(field.key)"
          :label="field.label"
          :required="field.required"
          :rules="fieldRules(field)"
        >
          <template #input>
            <van-checkbox-group
              v-if="field.options?.length"
              :model-value="getArrayValue(field.key)"
              direction="horizontal"
              :disabled="props.disabled || field.disabled"
              v-bind="field.props"
              @update:model-value="updateField(field.key, $event)"
            >
              <van-checkbox
                v-for="opt in field.options"
                :key="String(opt.value)"
                :name="opt.value"
                shape="square"
              >
                {{ opt.label }}
              </van-checkbox>
            </van-checkbox-group>
            <van-checkbox
              v-else
              :model-value="!!formData[field.key]"
              :disabled="props.disabled || field.disabled"
              v-bind="field.props"
              @update:model-value="updateField(field.key, $event)"
            />
          </template>
        </van-field>

        <!-- switch -->
        <van-field
          v-else-if="field.type === 'switch'"
          :name="field.key"
          :model-value="getValidationValue(field.key)"
          :label="field.label"
          :required="field.required"
          :rules="fieldRules(field)"
        >
          <template #input>
            <van-switch
              :model-value="!!formData[field.key]"
              :disabled="props.disabled || field.disabled"
              v-bind="field.props"
              @update:model-value="updateField(field.key, $event)"
            />
          </template>
        </van-field>

        <!-- date -> van-field + van-popup + van-date-picker -->
        <template v-else-if="field.type === 'date'">
          <van-field
            :model-value="getInputValue(field.key)"
            :name="field.key"
            is-link
            readonly
            :label="field.label"
            :placeholder="field.placeholder || 'YYYY-MM-DD'"
            :required="field.required"
            :rules="fieldRules(field)"
            :disabled="props.disabled || field.disabled"
            v-bind="field.props"
            @click="openPopup(field)"
          />
          <van-popup v-model:show="popupVisible[field.key]" round position="bottom">
            <van-date-picker
              :model-value="dateDraft[field.key] ?? []"
              :title="field.label"
              @update:model-value="setDateDraft(field.key, $event)"
              @confirm="onDateConfirm(field.key)"
              @cancel="closePopup(field.key)"
            />
          </van-popup>
        </template>

        <!-- datetime -> van-picker-group (date step + time step) -->
        <template v-else-if="field.type === 'datetime'">
          <van-field
            :model-value="getInputValue(field.key)"
            :name="field.key"
            is-link
            readonly
            :label="field.label"
            :placeholder="field.placeholder || 'YYYY-MM-DD HH:mm'"
            :required="field.required"
            :rules="fieldRules(field)"
            :disabled="props.disabled || field.disabled"
            v-bind="field.props"
            @click="openPopup(field)"
          />
          <van-popup v-model:show="popupVisible[field.key]" round position="bottom">
            <van-picker-group
              :title="field.label"
              :tabs="[props.dateTabLabel, props.timeTabLabel]"
              @confirm="onDateTimeConfirm(field.key)"
              @cancel="closePopup(field.key)"
            >
              <van-date-picker
                :model-value="dateDraft[field.key] ?? []"
                @update:model-value="setDateDraft(field.key, $event)"
              />
              <van-time-picker
                :model-value="timeDraft[field.key] ?? []"
                @update:model-value="setTimeDraft(field.key, $event)"
              />
            </van-picker-group>
          </van-popup>
        </template>

        <!-- file -> van-uploader -->
        <van-field
          v-else-if="field.type === 'file'"
          :name="field.key"
          :model-value="getValidationValue(field.key)"
          :label="field.label"
          :required="field.required"
          :rules="fieldRules(field)"
          :disabled="props.disabled || field.disabled"
        >
          <template #input>
            <van-uploader
              :model-value="getFileList(field.key)"
              :max-count="field.max"
              :disabled="props.disabled || field.disabled"
              v-bind="field.props"
              @update:model-value="updateField(field.key, $event)"
            />
          </template>
        </van-field>
      </template>
    </van-cell-group>

    <div class="mt-4 flex gap-2 px-4">
      <van-button plain block @click="handleReset">{{ t('common.reset') }}</van-button>
      <van-button type="primary" native-type="submit" block>{{ t('common.submit') }}</van-button>
    </div>
  </van-form>
</template>
