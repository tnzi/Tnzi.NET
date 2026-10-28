<template>
  <div class="ssv">
    <p class="ssv__hint">
      <TSvgIcon icon="mdi:information-outline" :size="13" />
      {{ t('senderValues.hint') }}
    </p>

    <div v-for="field in fields" :key="field.key" class="ssv-row">
      <span class="ssv-row__label">
        {{ field.label || field.key }}<span v-if="isRequiredAtCreate(field)" class="ssv-row__required">*</span>
      </span>
      <NCheckbox
        v-if="field.type === SigningFieldType.Checkbox"
        :checked="valueOf(field.key) === 'true'"
        @update:checked="(v: boolean) => patch(field.key, v ? 'true' : null)"
      />
      <NInput
        v-else
        :value="valueOf(field.key) ?? ''"
        size="small"
        :placeholder="field.binding ? t('senderValues.boundPlaceholder') : ''"
        @update:value="(v: string) => patch(field.key, v || null)"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * Values for the template fields the SENDER fills (fields with no recipient
 * role), entered while creating a request.
 *
 * ★ This is the only place the built-in admin can supply them. A request cannot
 *   be edited once created, and `send` refuses while a required sender field
 *   has no value - so a template with a required, unbound sender field used to
 *   produce drafts that could never be sent, only voided.
 *
 * A field bound to a merge variable is filled from the host record when left
 * empty; typing a value here overrides the record (the sender is
 * authoritative). Signature-like fields never appear: they always belong to a
 * recipient.
 */
import { NCheckbox, NInput } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { SigningFieldType, type TemplateFieldDto } from '@tnzi/core/services/signing'
import { isRequiredAtCreate } from '../signing-config'

const props = defineProps<{
  /** The chosen template's sender fields (see `isSenderField`). */
  fields: TemplateFieldDto[]
  modelValue?: Record<string, string | null> | null
  translate: (key: string) => string
}>()

const emit = defineEmits<{ 'update:modelValue': [Record<string, string | null>] }>()

const t = (key: string): string => props.translate(key)

function valueOf(key: string): string | null {
  return props.modelValue?.[key] ?? null
}

function patch(key: string, value: string | null): void {
  emit('update:modelValue', { ...(props.modelValue ?? {}), [key]: value })
}
</script>

<style scoped>
.ssv {
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: 100%;
}
.ssv__hint {
  display: flex;
  align-items: center;
  gap: 4px;
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.ssv-row {
  display: grid;
  grid-template-columns: minmax(120px, 200px) 1fr;
  align-items: center;
  gap: 8px;
}
.ssv-row__label {
  font-size: 13px;
  overflow-wrap: anywhere;
}
.ssv-row__required {
  margin-left: 2px;
  color: var(--tnzi-error, #d03050);
}

@media (max-width: 767px) {
  .ssv-row {
    grid-template-columns: 1fr;
  }
}
</style>
