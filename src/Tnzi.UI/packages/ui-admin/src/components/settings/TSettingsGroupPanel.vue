<template>
  <TDetailSection :title="groupTitle" :icon="group.icon ?? undefined" :hint="group.description ?? undefined">
    <template #actions>
      <NTag v-if="readonly" size="small" type="warning" :bordered="false">
        {{ t('admin.modules.system.settings.state.viewOnly') }}
      </NTag>
      <NPopconfirm v-else @positive-click="onReset">
        <template #trigger>
          <NButton size="small" quaternary :loading="resetting" :disabled="saving">
            {{ t('admin.modules.system.settings.actions.resetDefaults') }}
          </NButton>
        </template>
        {{ t('admin.modules.system.settings.actions.resetConfirm') }}
      </NPopconfirm>
    </template>

    <NAlert
      v-if="stale"
      class="t-settings-group__stale"
      type="warning"
      size="small"
      :bordered="false"
    >
      {{ t('admin.modules.system.settings.state.stale') }}
      <NButton size="tiny" quaternary type="warning" @click="onStaleReload">
        {{ t('admin.modules.system.settings.actions.reload') }}
      </NButton>
    </NAlert>

    <div class="t-settings-group">
      <!-- Default area: fields without a subsection render at the top. -->
      <TSettingsField
        v-for="field in defaultFields"
        :key="field.key"
        :field="field"
        :value="form[field.key] ?? null"
        :error="errors[field.key]"
        :readonly="readonly"
        @update:value="(v) => setValue(field, v)"
        @preview="previewSound(field)"
      />

      <!-- Subsections: fields sharing a subsection collapse into one section
           (default-expanded). Purely presentational - still one Save/Discard bar
           and one whole-group save. -->
      <NCollapse
        v-if="subsectionGroups.length"
        :default-expanded-names="subsectionNames"
        class="t-settings-group__subsections"
      >
        <NCollapseItem
          v-for="sub in subsectionGroups"
          :key="sub.name"
          :title="sub.name"
          :name="sub.name"
        >
          <div class="t-settings-group__sub-fields">
            <TSettingsField
              v-for="field in sub.fields"
              :key="field.key"
              :field="field"
              :value="form[field.key] ?? null"
              :error="errors[field.key]"
              :readonly="readonly"
              @update:value="(v) => setValue(field, v)"
              @preview="previewSound(field)"
            />
          </div>
        </NCollapseItem>
      </NCollapse>
    </div>

    <template v-if="!readonly" #savebar>
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
import { NAlert, NButton, NCollapse, NCollapseItem, NPopconfirm, NTag } from 'naive-ui'
import type { SettingsCenterFieldDto, SettingsCenterGroupDto } from '@tnzi/core/services/system'
import TDetailSection from '../detail/TDetailSection.vue'
import TSettingsField from './TSettingsField.vue'
import { useSafeMessage } from '../../pages/_shared/safeMessage'
import { interpolate, resolveBackendLabel, translatePageKey } from '../../pages/_shared/translate'
import { playSoundEffect } from '../../headless/chatSounds'
import { lastSettingsChange } from '../../headless/useSettingsRealtime'

type FieldValue = string | number | boolean | null

const props = defineProps<{
  group: SettingsCenterGroupDto
  saveGroup: (groupKey: string, changed: Record<string, string | null>) => Promise<SettingsCenterGroupDto>
  resetGroup: (groupKey: string) => Promise<SettingsCenterGroupDto>
}>()

const emit = defineEmits<{ updated: [group: SettingsCenterGroupDto]; refresh: [] }>()

// ui-admin shells don't always wrap with NMessageProvider - useSafeMessage
// degrades to a no-op MessageApi instead of throwing.
const message = useSafeMessage()

const t = (key: string) => translatePageKey('', key)

const form = reactive<Record<string, FieldValue>>({})
const snapshot = reactive<Record<string, FieldValue>>({})
const errors = reactive<Record<string, string>>({})
const saving = ref(false)
const resetting = ref(false)

// Concurrent-edit awareness: another session changed a key belonging to this
// group. Clean panel → ask the page for a silent re-fetch; dirty panel → show
// a banner instead of clobbering the user's in-progress edits. The panel's own
// save triggers a broadcast loopback - suppressed via a short post-save window.
const stale = ref(false)
let lastLocalWriteAt = 0
watch(lastSettingsChange, (change) => {
  if (!change) return
  if (Date.now() - lastLocalWriteAt < 3000) return
  if (!props.group.fields.some((f) => f.key.toLowerCase() === change.key.toLowerCase())) return
  if (dirty.value) stale.value = true
  else emit('refresh')
})

function onStaleReload(): void {
  stale.value = false
  emit('refresh')
}

// The user holds view but not update permission for this group → render inputs
// disabled and hide write affordances. `canEdit` defaults true (fail-open when
// the backend predates the field or Authorization isn't loaded).
const readonly = computed(() => props.group.canEdit === false)

// 右侧面板标题带模块上下文（左侧菜单只显示去前缀的组名）：`{模块} {组名}`，如 "AI General"。
// 组名已以模块名开头时（i18n 缺失、回退到未去前缀的 DisplayName）不重复拼接。
const groupTitle = computed(() => {
  const name = resolveBackendLabel(props.group.i18nKey, props.group.displayName)
  const mod = props.group.moduleName
  return mod && !name.toLowerCase().startsWith(mod.toLowerCase()) ? `${mod} ${name}` : name
})

// Split fields into a default area (no subsection) and ordered subsection
// groups (first appearance wins the ordering). Presentation only - the form /
// dirty / save state below still spans the whole group.
const defaultFields = computed(() => props.group.fields.filter((f) => !f.subsection))

const subsectionGroups = computed<{ name: string; fields: SettingsCenterFieldDto[] }[]>(() => {
  const order: string[] = []
  const map = new Map<string, SettingsCenterFieldDto[]>()
  for (const f of props.group.fields) {
    const sub = f.subsection
    if (!sub) continue
    if (!map.has(sub)) {
      map.set(sub, [])
      order.push(sub)
    }
    map.get(sub)!.push(f)
  }
  return order.map((name) => ({ name, fields: map.get(name)! }))
})

const subsectionNames = computed(() => subsectionGroups.value.map((s) => s.name))

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
    // 后端 decimal.TryParse 不接受科学计数法（1e-7）- 强制十进制展开
    return value.toLocaleString('en-US', { useGrouping: false, maximumFractionDigits: 20 })
  }
  return String(value)
}

function hydrate(group: SettingsCenterGroupDto): void {
  for (const field of group.fields) {
    const parsed = parseValue(field)
    form[field.key] = parsed
    snapshot[field.key] = parsed
    delete errors[field.key]
  }
  stale.value = false
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
  delete errors[field.key]
}

// 保存前客户端校验（与后端 ValidateFieldValue 同源规则的子集）：required /
// min-max / pattern。Password 例外：已存密钥（isSet）清空序列化为 null 语义是
// "不变更"，不会进 changed 集，故 required 检查不会误伤。
function validateField(field: SettingsCenterFieldDto, serialized: string | null): string | null {
  const v = (key: string, params?: Record<string, unknown>) =>
    interpolate(t(`admin.modules.system.settings.validation.${key}`), params)

  if (serialized == null) return field.isRequired ? v('required') : null

  if (field.type === 'Int' || field.type === 'Decimal') {
    const n = Number(serialized)
    if (field.min != null && n < field.min) return v('min', { min: field.min })
    if (field.max != null && n > field.max) return v('max', { max: field.max })
  }

  if ((field.type === 'String' || field.type === 'Text') && field.pattern && serialized.length > 0) {
    try {
      if (!new RegExp(`^(?:${field.pattern})$`).test(serialized)) return v('pattern')
    } catch {
      // .NET-only regex syntax the JS engine can't parse - the backend still validates.
    }
  }

  // Duration: canonical TimeSpan string (d.hh:mm:ss / hh:mm:ss). Pre-check only;
  // the backend's TimeSpan.TryParse is the authority for any edge form.
  if (field.type === 'Duration' && serialized.length > 0 && !TIMESPAN_RE.test(serialized)) {
    return v('duration')
  }

  return null
}

// Accepts the common TimeSpan forms: hh:mm, hh:mm:ss[.fffffff], [d.]hh:mm:ss.
const TIMESPAN_RE = /^-?(\d+\.)?\d{1,2}:\d{2}(:\d{2}(\.\d{1,7})?)?$/

// Play the currently-selected chat sound preset (silent for 'None' / unknown).
function previewSound(field: SettingsCenterFieldDto): void {
  const v = form[field.key]
  if (typeof v === 'string') playSoundEffect(v)
}

function onDiscard(): void {
  for (const field of props.group.fields) form[field.key] = snapshot[field.key] ?? null
}

async function onSave(): Promise<void> {
  const changed: Record<string, string | null> = {}
  let hasErrors = false
  for (const field of props.group.fields) {
    if (!isFieldChanged(field)) continue
    const serialized = serializeValue(field, form[field.key] ?? null)
    const error = validateField(field, serialized)
    if (error) {
      errors[field.key] = error
      hasErrors = true
      continue
    }
    changed[field.key] = serialized
  }
  if (hasErrors) return
  if (Object.keys(changed).length === 0) return
  saving.value = true
  try {
    const updated = await props.saveGroup(props.group.key, changed)
    lastLocalWriteAt = Date.now()
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
    lastLocalWriteAt = Date.now()
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
.t-settings-group__stale {
  margin-bottom: 12px;
}
/* Subsections sit below the default fields; each collapsible section stacks its
   own fields with the same vertical rhythm as the default area. */
.t-settings-group__sub-fields {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
</style>
