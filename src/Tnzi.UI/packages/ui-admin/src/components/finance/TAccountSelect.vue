<template>
  <NSelect
    :value="modelValue ?? null"
    :options="options"
    :loading="loading"
    :placeholder="placeholder"
    :disabled="disabled"
    :size="size"
    filterable
    clearable
    :aria-label="placeholder"
    @update:value="(v: string | null) => emit('update:modelValue', v)"
  />
</template>

<script setup lang="ts">
/**
 * `TAccountSelect` - chart-of-accounts picker with the three scopes finance
 * surfaces actually need, so pages stop re-deriving them from the account tree:
 *
 * - `postable` - every active leaf. Group accounts are excluded because
 *   posting to them is rejected by the ledger; offering them is offering a
 *   guaranteed error.
 * - `funds` - cash / bank leaves (`CashFlowActivity = CashEquivalent`).
 *   Bank profiles, transfers and the bank feed all require one.
 * - `expense` - expense-rooted leaves, for categorising a spend.
 *
 * Options load lazily on first mount and are cached per component instance.
 */
import { computed, onMounted, ref, watch } from 'vue'
import { NSelect, type SelectOption } from 'naive-ui'
import { CashFlowActivity, type AccountTreeDto, type FinanceBridge } from '../../services/bridges/finance-bridge'

const props = withDefaults(
  defineProps<{
    modelValue?: string | null
    bridge: FinanceBridge
    scope?: 'postable' | 'funds' | 'expense'
    placeholder?: string
    disabled?: boolean
    size?: 'tiny' | 'small' | 'medium' | 'large'
    /** Pre-loaded options (skips the fetch - pass when the page already has them). */
    preloaded?: SelectOption[]
  }>(),
  { scope: 'postable', size: 'small' },
)

const emit = defineEmits<{ 'update:modelValue': [value: string | null] }>()

const loaded = ref<SelectOption[]>([])
const loading = ref(false)

const options = computed(() => props.preloaded ?? loaded.value)

function collect(nodes: AccountTreeDto[], keep: (n: AccountTreeDto) => boolean, into: SelectOption[]) {
  for (const node of nodes) {
    if (!node.isGroup && node.isActive && keep(node)) {
      into.push({ label: `${node.code} ${node.name}`, value: node.id })
    }
    collect(node.children ?? [], keep, into)
  }
}

type AccountPredicate = (n: AccountTreeDto) => boolean

const PREDICATES: Record<'postable' | 'funds' | 'expense', AccountPredicate> = {
  postable: () => true,
  funds: (n) => n.cashFlowActivity === CashFlowActivity.CashEquivalent,
  // `rootType` is the five-root classification; expense leaves are the ones a
  // spend can be coded to.
  expense: (n) => String(n.rootType) === 'Expense',
}

async function load() {
  if (props.preloaded) return
  loading.value = true
  try {
    const tree = await props.bridge.accounts.tree(false)
    const out: SelectOption[] = []
    collect(tree, PREDICATES[props.scope], out)
    loaded.value = out
  } catch {
    // A picker that cannot load its options still has to mount - the page
    // around it (and its own error surface) is not this component's job.
    loaded.value = []
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch(() => props.scope, load)
</script>
