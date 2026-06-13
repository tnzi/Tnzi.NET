<template>
  <TDetailSection :title="groupTitle" :hint="group.description ?? undefined">
    <template #actions>
      <NPopconfirm @positive-click="onReset">
        <template #trigger>
          <NButton size="small" quaternary :loading="resetting" :disabled="saving">
            {{ t('admin.modules.system.settings.actions.resetDefaults') }}
          </NButton>
        </template>
        {{ t('admin.modules.system.settings.actions.resetConfirm') }}
      </NPopconfirm>
    </template>

    <div class="t-settings-group">
      <div v-for="field in group.fields" :key="field.key" class="t-settings-group__row">
        <div class="t-settings-group__label">
          <span>{{ fieldLabel(field) }}</span>
          <NTag v-if="field.isOverridden" size="tiny" type="info" :bordered="false">
            {{ t('admin.modules.system.settings.state.modified') }}
          </NTag>
        </div>
        <div class="t-settings-group__control">
          <NSwitch
            v-if="field.type === 'Boolean'"
            :value="form[field.key] === true"
            :disabled="field.isReadOnly"
            size="small"
            @update:value="(v: boolean) => setValue(field, v)"
          />
          <NInputNumber
            v-else-if="field.type === 'Int' || field.type === 'Decimal'"
            :value="(form[field.key] as number | null)"
            :min="field.min ?? undefined"
            :max="field.max ?? undefined"
            :precision="field.type === 'Int' ? 0 : undefined"
            :disabled="field.isReadOnly"
            size="small"
            class="t-settings-group__input"
            @update:value="(v: number | null) => setValue(field, v)"
          />
          <NSelect
            v-else-if="field.type === 'Select'"
            :value="(form[field.key] as string | null)"
            :options="(field.options ?? []).map((o) => ({ label: o, value: o }))"
            :disabled="field.isReadOnly"
            size="small"
            class="t-settings-group__input"
            @update:value="(v: string | null) => setValue(field, v)"
          />
          <NInput
            v-else-if="field.type === 'Password'"
            :value="(form[field.key] as string | null) ?? ''"
            type="password"
            show-password-on="click"
            :placeholder="field.isSet ? t('admin.modules.system.settings.state.encryptedSet') : t('admin.modules.system.settings.state.notSet')"
            :disabled="field.isReadOnly"
            size="small"
            class="t-settings-group__input"
            @update:value="(v: string) => setValue(field, v)"
          />
          <NInput
            v-else
            :value="(form[field.key] as string | null) ?? ''"
            :type="field.type === 'Text' ? 'textarea' : 'text'"
            :autosize="field.type === 'Text' ? { minRows: 2, maxRows: 6 } : undefined"
            :placeholder="field.defaultValue ?? ''"
            :disabled="field.isReadOnly"
            size="small"
            class="t-settings-group__input"
            @update:value="(v: string) => setValue(field, v)"
          />
          <div v-if="field.description" class="t-settings-group__hint">{{ field.description }}</div>
        </div>
      </div>
    </div>

    <template #savebar>
      <NButton size="small" :disabled="!dirty || saving || resetting" @click="onDiscard">
        {{ t('admin.modules.system.settings.actions.discard') }}
      </NButton>
      <NButton size="small" type="primary" :loading="saving" :disabled="!dirty || resetting" @click="onSave">
        {{ t('admin.modules.system.settings.actions.save') }}
      </NButton>
    </template>
  </TDetailSection>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NInput, NInputNumber, NPopconfirm, NSelect, NSwitch, NTag } from 'naive-ui'
import type { SettingsCenterFieldDto, SettingsCenterGroupDto } from '@tnzi/core/services/system'
import TDetailSection from '../detail/TDetailSection.vue'
import { useSafeMessage } from '../../pages/_shared/safeMessage'
import { resolveBackendLabel, translatePageKey } from '../../pages/_shared/translate'

type FieldValue = string | number | boolean | null

const props = defineProps<{
  group: SettingsCenterGroupDto
  saveGroup: (groupKey: string, changed: Record<string, string | null>) => Promise<SettingsCenterGroupDto>
  resetGroup: (groupKey: string) => Promise<SettingsCenterGroupDto>
}>()

const emit = defineEmits<{ updated: [group: SettingsCenterGroupDto] }>()

// ui-admin shells don't always wrap with NMessageProvider — useSafeMessage
// degrades to a no-op MessageApi instead of throwing.
const message = useSafeMessage()

const t = (key: string) => translatePageKey('', key)

const form = reactive<Record<string, FieldValue>>({})
const snapshot = reactive<Record<string, FieldValue>>({})
const saving = ref(false)
const resetting = ref(false)

const groupTitle = computed(() => resolveBackendLabel(props.group.i18nKey, props.group.displayName))

function fieldLabel(field: SettingsCenterFieldDto): string {
  return resolveBackendLabel(field.i18nKey, field.label)
}

function parseValue(field: SettingsCenterFieldDto): FieldValue {
  const raw = field.value ?? null
  if (field.type === 'Boolean') return raw != null ? raw.toLowerCase() === 'true' : false
  if (field.type === 'Int' || field.type === 'Decimal') {
    if (raw == null || raw === '') return null
    const n = Number(raw)
    return Number.isNaN(n) ? null : n
  }
  if (field.type === 'Password') return null
  return raw
}

function serializeValue(field: SettingsCenterFieldDto, value: FieldValue): string | null {
  if (value == null || value === '') return null
  if (field.type === 'Boolean') return value === true ? 'true' : 'false'
  if (typeof value === 'number') {
    // 后端 decimal.TryParse 不接受科学计数法（1e-7）— 强制十进制展开
    return value.toLocaleString('en-US', { useGrouping: false, maximumFractionDigits: 20 })
  }
  return String(value)
}

function hydrate(group: SettingsCenterGroupDto): void {
  for (const field of group.fields) {
    const parsed = parseValue(field)
    form[field.key] = parsed
    snapshot[field.key] = parsed
  }
}

watch(() => props.group, hydrate, { immediate: true })

// dirty 与变更收集都比较「序列化后」的值：'' 与 null 等价，避免假 dirty，
// 更重要的是防止 Password 框误敲再清空后把 '' 序列化成 null 静默删除已存密钥。
function isFieldChanged(field: SettingsCenterFieldDto): boolean {
  return serializeValue(field, form[field.key] ?? null) !== serializeValue(field, snapshot[field.key] ?? null)
}

const dirty = computed(() => props.group.fields.some(isFieldChanged))

function setValue(field: SettingsCenterFieldDto, value: FieldValue): void {
  form[field.key] = value
}

function onDiscard(): void {
  for (const field of props.group.fields) form[field.key] = snapshot[field.key] ?? null
}

async function onSave(): Promise<void> {
  const changed: Record<string, string | null> = {}
  for (const field of props.group.fields) {
    if (isFieldChanged(field)) changed[field.key] = serializeValue(field, form[field.key] ?? null)
  }
  if (Object.keys(changed).length === 0) return
  saving.value = true
  try {
    const updated = await props.saveGroup(props.group.key, changed)
    emit('updated', updated)
    message.success(t('admin.modules.system.settings.feedback.saved'))
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    saving.value = false
  }
}

async function onReset(): Promise<void> {
  resetting.value = true
  try {
    const updated = await props.resetGroup(props.group.key)
    emit('updated', updated)
    message.success(t('admin.modules.system.settings.feedback.resetDone'))
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    resetting.value = false
  }
}
</script>

<style scoped>
.t-settings-group {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.t-settings-group__row {
  display: grid;
  grid-template-columns: 220px 1fr;
  gap: 12px;
  align-items: start;
}
.t-settings-group__label {
  display: flex;
  align-items: center;
  gap: 6px;
  padding-top: 4px;
  font-size: 13px;
  color: var(--tnzi-base-text);
}
.t-settings-group__input {
  width: 100%;
  max-width: 420px;
}
.t-settings-group__hint {
  margin-top: 4px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #999);
}
@media (max-width: 767px) {
  .t-settings-group__row {
    grid-template-columns: 1fr;
    gap: 4px;
  }
}
</style>
