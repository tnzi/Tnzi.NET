<template>
  <!--
    Address field group - street / unit / city / region / postal (+ optional
    country) in a responsive grid, v-model'd on one address object. Region and
    country are a filterable select when options are supplied, else free text,
    so it is NOT locale-locked: pass your own subdivision/country lists (or
    none) instead of a hard-wired province list.
  -->
  <div class="t-address-fields" :style="{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }">
    <div class="t-address-fields__field t-address-fields__field--full">
      <label class="t-address-fields__label">{{ labels.street ?? 'Street address' }}</label>
      <n-input :value="model.street ?? null" :disabled="disabled" :placeholder="labels.street ?? 'Street address'"
        @update:value="(v: string | null) => set('street', v)" />
    </div>
    <div class="t-address-fields__field">
      <label class="t-address-fields__label">{{ labels.unit ?? 'Unit / Suite' }}</label>
      <n-input :value="model.unit ?? null" :disabled="disabled" @update:value="(v: string | null) => set('unit', v)" />
    </div>
    <div class="t-address-fields__field">
      <label class="t-address-fields__label">{{ labels.city ?? 'City' }}</label>
      <n-input :value="model.city ?? null" :disabled="disabled" @update:value="(v: string | null) => set('city', v)" />
    </div>
    <div class="t-address-fields__field">
      <label class="t-address-fields__label">{{ labels.region ?? regionLabel }}</label>
      <n-select v-if="regionOptions && regionOptions.length" :value="model.region ?? null" :options="regionOptions"
        filterable clearable :disabled="disabled" @update:value="(v: string | null) => set('region', v)" />
      <n-input v-else :value="model.region ?? null" :disabled="disabled" @update:value="(v: string | null) => set('region', v)" />
    </div>
    <div class="t-address-fields__field">
      <label class="t-address-fields__label">{{ labels.postalCode ?? postalLabel }}</label>
      <n-input :value="model.postalCode ?? null" :disabled="disabled" @update:value="(v: string | null) => set('postalCode', v)" />
    </div>
    <div v-if="showCountry" class="t-address-fields__field">
      <label class="t-address-fields__label">{{ labels.country ?? 'Country' }}</label>
      <n-select v-if="countryOptions && countryOptions.length" :value="model.country ?? null" :options="countryOptions"
        filterable clearable :disabled="disabled" @update:value="(v: string | null) => set('country', v)" />
      <n-input v-else :value="model.country ?? null" :disabled="disabled" @update:value="(v: string | null) => set('country', v)" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NInput, NSelect, type SelectOption } from 'naive-ui'

export interface AddressValue {
  street?: string | null
  unit?: string | null
  city?: string | null
  /** Province / state / county. */
  region?: string | null
  postalCode?: string | null
  country?: string | null
}

interface Props {
  modelValue?: AddressValue | null
  /** Subdivision options; when supplied the region field becomes a select. */
  regionOptions?: SelectOption[]
  /** Country options; when supplied the country field becomes a select. */
  countryOptions?: SelectOption[]
  /** Show the country field. Default false. */
  showCountry?: boolean
  regionLabel?: string
  postalLabel?: string
  /** Per-field label overrides. */
  labels?: Partial<Record<keyof AddressValue, string>>
  /** Grid columns (fields span 1; street spans all). Default 2. */
  columns?: number
  disabled?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  regionLabel: 'Province / State',
  postalLabel: 'Postal / ZIP',
  columns: 2,
  labels: () => ({}),
})

const emit = defineEmits<{ 'update:modelValue': [value: AddressValue] }>()

const model = computed<AddressValue>(() => props.modelValue ?? {})

function set(key: keyof AddressValue, value: string | null): void {
  emit('update:modelValue', { ...model.value, [key]: value })
}
</script>

<style scoped>
.t-address-fields {
  display: grid;
  gap: 12px;
}
.t-address-fields__field--full {
  grid-column: 1 / -1;
}
.t-address-fields__label {
  display: block;
  margin-bottom: 4px;
  font-size: 13px;
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.6));
}
@media (max-width: 640px) {
  .t-address-fields {
    grid-template-columns: 1fr !important;
  }
}
</style>
