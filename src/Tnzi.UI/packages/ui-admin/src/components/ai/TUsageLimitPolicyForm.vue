<template>
  <div class="t-ulp">
    <!-- The two windows: follow the level below, no limit, or an amount. -->
    <div v-for="item in limitItems" :key="item.key" class="t-ulp__row">
      <div class="t-ulp__label">
        <span class="t-ulp__name">{{ item.label }}</span>
        <span class="t-ulp__sub">{{ item.length }}</span>
      </div>
      <div class="t-ulp__control">
        <NRadioGroup
          :value="item.setting.mode"
          :disabled="readonly"
          @update:value="(mode: UsageLimitMode) => setLimit(item.key, { ...item.setting, mode })"
        >
          <NRadioButton :value="UsageLimitMode.Inherit">{{ inheritLabel }}</NRadioButton>
          <NRadioButton :value="UsageLimitMode.Unlimited">{{ t('mode.unlimited') }}</NRadioButton>
          <NRadioButton :value="UsageLimitMode.Limited">{{ t('mode.limited') }}</NRadioButton>
        </NRadioGroup>
        <NInputNumber
          v-if="item.setting.mode === UsageLimitMode.Limited"
          class="t-ulp__amount"
          :value="item.setting.amountUsd ?? null"
          :min="0"
          :precision="2"
          :step="1"
          :disabled="readonly"
          :placeholder="t('policy.amountPlaceholder')"
          @update:value="(v: number | null) => setLimit(item.key, { ...item.setting, amountUsd: v })"
        >
          <template #prefix>$</template>
        </NInputNumber>
        <span v-else-if="item.setting.mode === UsageLimitMode.Inherit" class="t-ulp__inherited">
          {{ t('policy.inheritedValue', { value: item.inheritedText }) }}
        </span>
      </div>
    </div>

    <!-- Which models the user may pick, and the one they start on. -->
    <div class="t-ulp__row">
      <div class="t-ulp__label">
        <span class="t-ulp__name">{{ t('policy.models') }}</span>
        <span class="t-ulp__sub">{{ t('policy.modelsHint') }}</span>
      </div>
      <div class="t-ulp__control t-ulp__control--stack">
        <p v-if="catalog.length === 0" class="t-ulp__note">{{ t('policy.noCatalog') }}</p>
        <template v-else>
          <NRadioGroup
            :value="modelValue.allowedModels ? 'custom' : 'inherit'"
            :disabled="readonly"
            @update:value="onModelsMode"
          >
            <NRadioButton value="inherit">{{ inheritLabel }}</NRadioButton>
            <NRadioButton value="custom">{{ t('policy.modelsCustom') }}</NRadioButton>
          </NRadioGroup>
          <NSelect
            v-if="modelValue.allowedModels"
            :value="modelValue.allowedModels"
            multiple
            :options="catalogOptions"
            :disabled="readonly"
            :placeholder="t('policy.modelsPlaceholder')"
            @update:value="(ids: string[]) => update({ allowedModels: ids })"
          />
          <span v-else class="t-ulp__inherited">{{ t('policy.inheritedValue', { value: inheritedModelsText }) }}</span>
        </template>
      </div>
    </div>

    <div v-if="catalog.length > 0" class="t-ulp__row">
      <div class="t-ulp__label">
        <span class="t-ulp__name">{{ t('policy.defaultModel') }}</span>
        <span class="t-ulp__sub">{{ t('policy.defaultModelHint') }}</span>
      </div>
      <div class="t-ulp__control">
        <NSelect
          :value="modelValue.defaultModel ?? null"
          clearable
          :options="defaultModelOptions"
          :disabled="readonly"
          :placeholder="t('policy.defaultModelPlaceholder', { source: inheritLabel, value: inheritedDefaultText })"
          @update:value="(id: string | null) => update({ defaultModel: id })"
        />
      </div>
    </div>

    <!-- Per model: the thinking efforts users can choose, and the one they start on. -->
    <div v-if="effortModels.length > 0" class="t-ulp__row">
      <div class="t-ulp__label">
        <span class="t-ulp__name">{{ t('policy.efforts') }}</span>
        <span class="t-ulp__sub">{{ t('policy.effortsHint') }}</span>
      </div>
      <div class="t-ulp__control t-ulp__control--stack">
        <div v-for="m in effortModels" :key="m.id" class="t-ulp__effort" :data-effort-model="m.id">
          <div class="t-ulp__effort-head">
            <span class="t-ulp__effort-model">{{ m.displayName || m.id }}</span>
            <NRadioGroup
              size="small"
              :value="ownEfforts(m.id) ? 'custom' : 'inherit'"
              :disabled="readonly"
              @update:value="(mode: 'inherit' | 'custom') => onEffortsMode(m.id, mode)"
            >
              <NRadioButton value="inherit">{{ inheritLabel }}</NRadioButton>
              <NRadioButton value="custom">{{ t('policy.effortsSet') }}</NRadioButton>
            </NRadioGroup>
          </div>
          <template v-if="ownEfforts(m.id)">
            <div class="t-ulp__effort-fields">
              <NSelect
                class="t-ulp__effort-list"
                :value="ownEfforts(m.id)!.efforts"
                multiple
                filterable
                tag
                :options="effortValueOptions(m.id)"
                :disabled="readonly"
                :placeholder="t('policy.effortsPlaceholder')"
                @update:value="(values: string[]) => setEfforts(m.id, values)"
              />
              <NSelect
                class="t-ulp__effort-default"
                :value="ownEfforts(m.id)!.defaultEffort ?? null"
                clearable
                :options="ownEfforts(m.id)!.efforts.map((v) => ({ label: v, value: v }))"
                :disabled="readonly || ownEfforts(m.id)!.efforts.length === 0"
                :placeholder="t('policy.effortDefaultPlaceholder')"
                @update:value="(v: string | null) => setEffortDefault(m.id, v)"
              />
            </div>
            <span v-if="ownEfforts(m.id)!.efforts.length === 0" class="t-ulp__inherited">{{ t('policy.effortsNoChoice') }}</span>
          </template>
          <span v-else class="t-ulp__inherited">{{ t('policy.inheritedValue', { value: inheritedEffortsText(m.id) }) }}</span>
        </div>
      </div>
    </div>

    <div class="t-ulp__row">
      <div class="t-ulp__label">
        <span class="t-ulp__name">{{ t('policy.note') }}</span>
      </div>
      <div class="t-ulp__control">
        <NInput
          :value="modelValue.note ?? ''"
          :disabled="readonly"
          :maxlength="500"
          :placeholder="t('policy.notePlaceholder')"
          @update:value="(v: string) => update({ note: v })"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * `TUsageLimitPolicyForm` - one level of the usage policy as a form: the two
 * window limits, the models a user may pick, the default model, and per model
 * the thinking efforts users can choose (runtime-native values, typed freely:
 * each CLI has its own vocabulary) with the one they start on.
 *
 * Every item can follow the level below it (`inheritLabel`: "Follow defaults"
 * for a user, "Follow configuration" for everyone's defaults), and the form
 * says what that currently resolves to, so "follow" is never a blank. Updates
 * are emitted as new objects; the caller owns the draft and the save.
 */
import { computed } from 'vue'
import { NInput, NInputNumber, NRadioButton, NRadioGroup, NSelect } from 'naive-ui'
import {
  UsageLimitMode,
  type ChatModelDto,
  type ChatModelEffortsDto,
  type SaveUsagePolicyDto,
  type UsageLimitSettingDto,
} from '@tnzi/core/services/ai'
import { interpolate, translatePageKey } from '../../i18n/translate'
import { formatUsd, formatWindowLength } from './usage-limit-format'

/** What the items resolve to when they follow the level below. */
export interface InheritedUsagePolicy {
  sessionLimitUsd?: number | null
  weeklyLimitUsd?: number | null
  /** Model ids; empty = every model of the list. */
  allowedModels: string[]
  defaultModel?: string | null
  /** Each model's efforts at the level below; a model missing here follows the catalog's (the model list's). */
  modelEfforts?: Record<string, ChatModelEffortsDto>
}

const props = withDefaults(
  defineProps<{
    modelValue: SaveUsagePolicyDto
    inherited: InheritedUsagePolicy
    catalog: ChatModelDto[]
    inheritLabel: string
    sessionWindowMinutes?: number
    weeklyWindowMinutes?: number
    readonly?: boolean
  }>(),
  { sessionWindowMinutes: 300, weeklyWindowMinutes: 7 * 24 * 60, readonly: false },
)

const emit = defineEmits<{
  'update:modelValue': [value: SaveUsagePolicyDto]
}>()

defineOptions({ name: 'TUsageLimitPolicyForm' })

const t = (key: string, params?: Record<string, unknown>): string => interpolate(translatePageKey('ai.quota', key), params)

function update(patch: Partial<SaveUsagePolicyDto>): void {
  emit('update:modelValue', { ...props.modelValue, ...patch })
}

function setLimit(key: 'sessionLimit' | 'weeklyLimit', setting: UsageLimitSettingDto): void {
  update({ [key]: setting })
}

const limitText = (usd: number | null | undefined) => (usd == null ? t('mode.unlimited') : formatUsd(usd))

const limitItems = computed(() => [
  {
    key: 'sessionLimit' as const,
    label: t('policy.sessionLimit'),
    length: formatWindowLength(props.sessionWindowMinutes, t),
    setting: props.modelValue.sessionLimit,
    inheritedText: limitText(props.inherited.sessionLimitUsd),
  },
  {
    key: 'weeklyLimit' as const,
    label: t('policy.weeklyLimit'),
    length: formatWindowLength(props.weeklyWindowMinutes, t),
    setting: props.modelValue.weeklyLimit,
    inheritedText: limitText(props.inherited.weeklyLimitUsd),
  },
])

const nameOf = (id: string) => props.catalog.find((m) => m.id === id)?.displayName || id

const catalogOptions = computed(() => props.catalog.map((m) => ({ label: m.displayName || m.id, value: m.id })))

const inheritedModelsText = computed(() =>
  props.inherited.allowedModels.length === 0
    ? t('policy.modelsAll')
    : props.inherited.allowedModels.map(nameOf).join(', '),
)

/** The default must be one the user may pick: the chosen list, else what the level below allows. */
const defaultModelOptions = computed(() => {
  const ids = props.modelValue.allowedModels ?? props.inherited.allowedModels
  const pool = ids.length === 0 ? props.catalog : props.catalog.filter((m) => ids.includes(m.id))
  return pool.map((m) => ({ label: m.displayName || m.id, value: m.id }))
})

const inheritedDefaultText = computed(() =>
  props.inherited.defaultModel ? nameOf(props.inherited.defaultModel) : t('policy.defaultModelAgent'),
)

/** The values Claude Code takes, offered as suggestions; any other runtime value can be typed. */
const KNOWN_EFFORTS = ['low', 'medium', 'high', 'xhigh', 'max']

/** The models efforts can be set for: the ones the user may pick at this level. */
const effortModels = computed(() => {
  const ids = props.modelValue.allowedModels ?? props.inherited.allowedModels
  return ids.length === 0 ? props.catalog : props.catalog.filter((m) => ids.includes(m.id))
})

const ownEfforts = (id: string): ChatModelEffortsDto | null => props.modelValue.modelEfforts?.[id] ?? null

function inheritedEfforts(id: string): ChatModelEffortsDto {
  const below = props.inherited.modelEfforts?.[id]
  if (below) return below
  const listed = props.catalog.find((m) => m.id === id)
  return { efforts: listed?.efforts ?? [], defaultEffort: listed?.defaultEffort ?? null }
}

function inheritedEffortsText(id: string): string {
  const e = inheritedEfforts(id)
  if (e.efforts.length === 0) return t('policy.effortsNone')
  const list = e.efforts.join(', ')
  return e.defaultEffort ? t('policy.effortsWithDefault', { efforts: list, default: e.defaultEffort }) : list
}

function effortValueOptions(id: string) {
  const values = [...new Set([...inheritedEfforts(id).efforts, ...KNOWN_EFFORTS, ...(ownEfforts(id)?.efforts ?? [])])]
  return values.map((v) => ({ label: v, value: v }))
}

function setModelEfforts(id: string, value: ChatModelEffortsDto | null): void {
  const next = { ...(props.modelValue.modelEfforts ?? {}) }
  if (value) next[id] = value
  else delete next[id]
  update({ modelEfforts: Object.keys(next).length > 0 ? next : null })
}

function onEffortsMode(id: string, mode: 'inherit' | 'custom'): void {
  if (mode === 'inherit') {
    setModelEfforts(id, null)
    return
  }
  // Start from what the model offers now, so "set" edits from there.
  const below = inheritedEfforts(id)
  setModelEfforts(id, { efforts: [...below.efforts], defaultEffort: below.defaultEffort ?? null })
}

function setEfforts(id: string, values: string[]): void {
  const efforts = [...new Set(values.map((v) => v.trim()).filter((v) => v.length > 0))]
  const current = ownEfforts(id)?.defaultEffort ?? null
  setModelEfforts(id, { efforts, defaultEffort: current && efforts.includes(current) ? current : null })
}

function setEffortDefault(id: string, value: string | null): void {
  setModelEfforts(id, { efforts: [...(ownEfforts(id)?.efforts ?? [])], defaultEffort: value })
}

function onModelsMode(mode: 'inherit' | 'custom'): void {
  if (mode === 'inherit') {
    update({ allowedModels: null })
    return
  }
  // Start the list from what the user can use now, so switching to "choose"
  // narrows from there instead of from nothing.
  const start = props.inherited.allowedModels.length > 0 ? props.inherited.allowedModels : props.catalog.map((m) => m.id)
  update({ allowedModels: [...start] })
}
</script>

<style scoped>
.t-ulp {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.t-ulp__row {
  display: grid;
  grid-template-columns: minmax(150px, 220px) minmax(0, 1fr);
  gap: 8px 16px;
  align-items: start;
}
.t-ulp__label {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding-top: 6px;
}
.t-ulp__name {
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-base-text);
}
.t-ulp__sub {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-ulp__control {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
  min-width: 0;
}
.t-ulp__control--stack {
  flex-direction: column;
  align-items: stretch;
}
.t-ulp__control--stack > :first-child {
  align-self: flex-start;
}
.t-ulp__effort {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.t-ulp__effort-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px 12px;
}
.t-ulp__effort-model {
  min-width: 120px;
  font-size: 13px;
  color: var(--tnzi-base-text);
}
.t-ulp__effort-fields {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.t-ulp__effort-list {
  flex: 1 1 240px;
  min-width: 0;
}
.t-ulp__effort-default {
  flex: 0 1 180px;
  min-width: 140px;
}
.t-ulp__amount {
  width: 180px;
  max-width: 100%;
}
.t-ulp__inherited,
.t-ulp__note {
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
@media (max-width: 767px) {
  .t-ulp__row {
    grid-template-columns: 1fr;
  }
  .t-ulp__label {
    padding-top: 0;
  }
}
</style>
