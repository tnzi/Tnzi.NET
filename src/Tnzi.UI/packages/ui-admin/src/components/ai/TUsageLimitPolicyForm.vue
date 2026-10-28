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
 * window limits, the models a user may pick, and the default model.
 *
 * Every item can follow the level below it (`inheritLabel`: "Follow defaults"
 * for a user, "Follow configuration" for everyone's defaults), and the form
 * says what that currently resolves to, so "follow" is never a blank. Updates
 * are emitted as new objects; the caller owns the draft and the save.
 */
import { computed } from 'vue'
import { NInput, NInputNumber, NRadioButton, NRadioGroup, NSelect } from 'naive-ui'
import { UsageLimitMode, type ChatModelDto, type SaveUsagePolicyDto, type UsageLimitSettingDto } from '@tnzi/core/services/ai'
import { interpolate, translatePageKey } from '../../i18n/translate'
import { formatUsd, formatWindowLength } from './usage-limit-format'

/** What the items resolve to when they follow the level below. */
export interface InheritedUsagePolicy {
  sessionLimitUsd?: number | null
  weeklyLimitUsd?: number | null
  /** Model ids; empty = every model of the list. */
  allowedModels: string[]
  defaultModel?: string | null
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
