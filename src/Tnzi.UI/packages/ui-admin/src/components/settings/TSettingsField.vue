<template>
  <div class="t-settings-field__row">
    <div class="t-settings-field__label">
      <span>{{ fieldLabel }}<span v-if="field.isRequired" class="t-settings-field__required">*</span></span>
      <NTag v-if="field.isOverridden" size="tiny" type="info" :bordered="false">
        {{ t('admin.modules.system.settings.state.modified') }}
      </NTag>
    </div>
    <div class="t-settings-field__control">
      <NSwitch
        v-if="field.type === 'Boolean'"
        :value="value === true"
        :disabled="field.isReadOnly || readonly"
        size="small"
        @update:value="(v: boolean) => emit('update:value', v)"
      />
      <NInputNumber
        v-else-if="field.type === 'Int' || field.type === 'Decimal'"
        :value="(value as number | null)"
        :min="field.min ?? undefined"
        :max="field.max ?? undefined"
        :precision="field.type === 'Int' ? 0 : undefined"
        :disabled="field.isReadOnly || readonly"
        size="small"
        class="t-settings-field__input"
        @update:value="(v: number | null) => emit('update:value', v)"
      />
      <div v-else-if="field.type === 'Select'" class="t-settings-field__select">
        <NSelect
          :value="(value as string | null)"
          :options="(field.options ?? []).map((o) => ({ label: o, value: o }))"
          :disabled="field.isReadOnly || readonly"
          size="small"
          class="t-settings-field__input"
          @update:value="(v: string | null) => emit('update:value', v)"
        />
        <!-- Chat sound fields get a preview so the admin can hear a preset
             before saving (the sounds are WebAudio-synthesised client-side). -->
        <NButton
          v-if="isChatSoundSettingKey(field.key)"
          quaternary
          circle
          size="small"
          :title="t('admin.modules.system.settings.actions.preview')"
          @click="emit('preview')"
        >
          <template #icon><Icon icon="mdi:play" :width="16" /></template>
        </NButton>
      </div>
      <NInput
        v-else-if="field.type === 'Password'"
        :value="(value as string | null) ?? ''"
        type="password"
        show-password-on="click"
        :placeholder="field.isSet ? t('admin.modules.system.settings.state.encryptedSet') : t('admin.modules.system.settings.state.notSet')"
        :disabled="field.isReadOnly || readonly"
        size="small"
        class="t-settings-field__input"
        @update:value="(v: string) => emit('update:value', v)"
      />
      <NInput
        v-else-if="field.type === 'Duration'"
        :value="(value as string | null) ?? ''"
        :placeholder="t('admin.modules.system.settings.durationFormat')"
        :disabled="field.isReadOnly || readonly"
        size="small"
        class="t-settings-field__input"
        @update:value="(v: string) => emit('update:value', v)"
      />
      <NInput
        v-else
        :value="(value as string | null) ?? ''"
        :type="field.type === 'Text' ? 'textarea' : 'text'"
        :autosize="field.type === 'Text' ? { minRows: 2, maxRows: 6 } : undefined"
        :placeholder="field.defaultValue ?? ''"
        :disabled="field.isReadOnly || readonly"
        size="small"
        class="t-settings-field__input"
        @update:value="(v: string) => emit('update:value', v)"
      />
      <div v-if="error" class="t-settings-field__error">{{ error }}</div>
      <div v-if="field.type === 'Duration' && !error" class="t-settings-field__hint">
        {{ t('admin.modules.system.settings.durationHint') }}
      </div>
      <div v-if="field.description" class="t-settings-field__hint">{{ field.description }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NButton, NInput, NInputNumber, NSelect, NSwitch, NTag } from 'naive-ui'
import { Icon } from '@iconify/vue'
import type { SettingsCenterFieldDto } from '@tnzi/core/services/system'
import { resolveBackendLabel, translatePageKey } from '../../i18n/translate'
import { isChatSoundSettingKey } from '../../headless/chat-sounds'

// Mirror of TSettingsGroupPanel's FieldValue - the parent owns the form state
// and passes the current value down; this component only renders the control.
type FieldValue = string | number | boolean | null

const props = defineProps<{
  field: SettingsCenterFieldDto
  value: FieldValue
  error?: string
  readonly: boolean
}>()

const emit = defineEmits<{ 'update:value': [value: FieldValue]; preview: [] }>()

const t = (key: string) => translatePageKey('', key)

const fieldLabel = computed(() => resolveBackendLabel(props.field.i18nKey, props.field.label))
</script>

<style scoped>
.t-settings-field__row {
  display: grid;
  grid-template-columns: 220px 1fr;
  gap: 12px;
  align-items: start;
}
.t-settings-field__label {
  display: flex;
  align-items: center;
  gap: 6px;
  padding-top: 4px;
  font-size: 13px;
  color: var(--tnzi-base-text);
}
.t-settings-field__input {
  width: 100%;
  max-width: 420px;
}
.t-settings-field__select {
  display: flex;
  align-items: center;
  gap: 8px;
  max-width: 420px;
}
.t-settings-field__select .t-settings-field__input {
  flex: 1;
  min-width: 0;
}
.t-settings-field__hint {
  margin-top: 4px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #999);
}
.t-settings-field__error {
  margin-top: 4px;
  font-size: 12px;
  color: var(--tnzi-error, #d03050);
}
.t-settings-field__required {
  margin-left: 2px;
  color: var(--tnzi-error, #d03050);
}
@media (max-width: 767px) {
  .t-settings-field__row {
    grid-template-columns: 1fr;
    gap: 4px;
  }
}
</style>
