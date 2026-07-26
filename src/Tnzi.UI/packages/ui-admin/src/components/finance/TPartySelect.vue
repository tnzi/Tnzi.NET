<template>
  <NSelect
    :value="modelValue ?? null"
    :options="options"
    :loading="loading"
    :placeholder="placeholder ?? defaultPlaceholder"
    :disabled="disabled"
    :size="size"
    filterable
    clearable
    remote
    :aria-label="placeholder ?? defaultPlaceholder"
    @search="onSearch"
    @update:value="(v: string | null) => emit('update:modelValue', v)"
  />
</template>

<script setup lang="ts">
/**
 * `TPartySelect` - customer / vendor picker.
 *
 * `kind="auto"` resolves from the money direction, which is how these two
 * always get chosen in practice: money in is a customer receipt, money out is
 * a vendor payment. Getting this wrong is the classic North-American mixup
 * (Invoice ≠ Bill, Customer ≠ Vendor), so the component derives it rather than
 * leaving each call site to remember.
 *
 * Search is server-side with a locally-accumulated option map: naive-ui only
 * renders labels for options currently in the list, so a remote search that
 * replaces the array wholesale makes the *already selected* tag lose its
 * label. Accumulating every option we have seen keeps selections readable.
 */
import { computed, onMounted, ref, watch } from 'vue'
import { NSelect, type SelectOption } from 'naive-ui'
import type { FinanceBridge } from '../../services/bridges/finance-bridge'

const props = withDefaults(
  defineProps<{
    modelValue?: string | null
    bridge: FinanceBridge
    /** `auto` picks customer for inbound money, vendor for outbound. */
    kind?: 'customer' | 'vendor' | 'auto'
    /** Signed amount, used when `kind="auto"`. Positive = money in. */
    amount?: number | null
    placeholder?: string
    disabled?: boolean
    size?: 'tiny' | 'small' | 'medium' | 'large'
  }>(),
  { kind: 'auto', size: 'small' },
)

const emit = defineEmits<{ 'update:modelValue': [value: string | null] }>()

const seen = ref<Map<string, SelectOption>>(new Map())
const current = ref<SelectOption[]>([])
const loading = ref(false)

const resolvedKind = computed<'customer' | 'vendor'>(() => {
  if (props.kind !== 'auto') return props.kind
  return (props.amount ?? 0) >= 0 ? 'customer' : 'vendor'
})

const defaultPlaceholder = computed(() => (resolvedKind.value === 'customer' ? 'Customer' : 'Vendor'))

const options = computed<SelectOption[]>(() => {
  const list = [...current.value]
  const present = new Set(list.map((o) => String(o.value)))
  // Re-attach the selected option when the latest search filtered it out.
  const selected = props.modelValue ? seen.value.get(props.modelValue) : undefined
  if (selected && !present.has(String(selected.value))) list.unshift(selected)
  return list
})

async function fetchPage(searchText?: string) {
  loading.value = true
  try {
    const source = resolvedKind.value === 'customer' ? props.bridge.customers : props.bridge.vendors
    const page = await source.fetch({
      pageIndex: 1,
      pageSize: 50,
      searchText,
      filters: { isActive: true },
    })
    const next = page.items.map((p) => ({ label: p.name, value: p.id }))
    current.value = next
    const map = new Map(seen.value)
    for (const o of next) map.set(String(o.value), o)
    seen.value = map
  } catch {
    current.value = []
  } finally {
    loading.value = false
  }
}

let searchTimer: ReturnType<typeof setTimeout> | undefined
function onSearch(text: string) {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => void fetchPage(text || undefined), 250)
}

onMounted(() => void fetchPage())
// Flipping direction changes which list is authoritative; a stale customer id
// left selected on a vendor field would submit a party of the wrong type.
watch(resolvedKind, () => {
  emit('update:modelValue', null)
  void fetchPage()
})
</script>
