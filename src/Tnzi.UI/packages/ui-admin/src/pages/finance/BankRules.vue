<template>
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :title="title"
    :row-actions="rowActions"
    :translate="t"
    :detail-width="640"
  >
    <template #kpis>
      <!-- Order IS the rule (first match wins), so the page says so up front
           rather than leaving people to discover it by being surprised. -->
      <NAlert type="info" :bordered="false" class="fin-rules__hint">
        {{ t('orderHint') }}
      </NAlert>
    </template>

    <template #form="{ formData, mode }">
      <div class="fin-rules__form">
        <div class="fin-rules__field">
          <span class="fin-rules__label">{{ t('form.name') }}</span>
          <NInput v-model:value="form(formData).name" size="small" :disabled="mode === 'view'" :placeholder="t('form.namePlaceholder')" />
        </div>

        <div class="fin-rules__grid">
          <div class="fin-rules__field">
            <span class="fin-rules__label">{{ t('form.account') }}</span>
            <NSelect
              v-model:value="form(formData).accountId"
              :options="fundsAccountOptions"
              size="small"
              clearable
              :disabled="mode === 'view'"
              :placeholder="t('form.allAccounts')"
            />
          </div>
          <div class="fin-rules__field">
            <span class="fin-rules__label">{{ t('form.direction') }}</span>
            <NSelect v-model:value="form(formData).direction" :options="directionOptions" size="small" :disabled="mode === 'view'" />
          </div>
        </div>

        <TRuleBuilder
          v-model="form(formData).conditions"
          v-model:match-mode="form(formData).matchMode"
          :readonly="mode === 'view'"
          :translate="t"
        />

        <NDivider class="fin-rules__divider" />

        <span class="fin-rules__section">{{ t('form.then') }}</span>
        <div class="fin-rules__grid">
          <div class="fin-rules__field">
            <span class="fin-rules__label">{{ t('form.docType') }}</span>
            <NSelect v-model:value="form(formData).docType" :options="docTypeOptions" size="small" :disabled="mode === 'view'" />
          </div>
          <div class="fin-rules__field">
            <span class="fin-rules__label">{{ t('form.counterAccount') }}</span>
            <NSelect
              v-model:value="form(formData).counterAccountId"
              :options="leafAccountOptions"
              size="small"
              clearable
              filterable
              :disabled="mode === 'view'"
              :placeholder="t('form.counterAccountPlaceholder')"
            />
          </div>
        </div>

        <div class="fin-rules__field">
          <span class="fin-rules__label">{{ t('form.paymentMethod') }}</span>
          <NSelect
            v-model:value="form(formData).paymentMethod"
            :options="methodOptions"
            size="small"
            clearable
            tag
            filterable
            :disabled="mode === 'view'"
          />
        </div>

        <div class="fin-rules__switch">
          <NSwitch v-model:value="form(formData).autoApply" size="small" :disabled="mode === 'view'" />
          <div>
            <div class="fin-rules__switch-label">{{ t('form.autoApply') }}</div>
            <div class="fin-rules__switch-hint">{{ t('form.autoApplyHint') }}</div>
          </div>
        </div>

        <div class="fin-rules__switch">
          <NSwitch v-model:value="form(formData).isEnabled" size="small" :disabled="mode === 'view'" />
          <div class="fin-rules__switch-label">{{ t('form.isEnabled') }}</div>
        </div>
      </div>
    </template>
  </TCrudPage>

  <!-- Dry run: what this rule would take, and who actually wins each line. -->
  <TModalShell :show="testShow" :title="t('test.title')" :width="720" @update:show="(v: boolean) => (testShow = v)">
    <NSpin :show="testing">
      <div v-if="testResult" class="fin-rules__test">
        <p class="fin-rules__test-summary">
          {{ t('test.summary').replace('{matched}', String(testResult.matched)).replace('{evaluated}', String(testResult.evaluated)) }}
        </p>
        <NAlert v-if="stolenCount > 0" type="warning" :bordered="false">
          {{ t('test.stolen').replace('{count}', String(stolenCount)) }}
        </NAlert>
        <TResponsiveTable
          v-if="testResult.rows.length > 0"
          :columns="testColumns"
          :data="testResult.rows"
          size="small"
          mobile="scroll"
          :bordered="false"
          :pagination="false"
        />
        <TEmpty v-else :text="t('test.empty')" />
      </div>
    </NSpin>
    <template #footer>
      <NButton size="small" @click="testShow = false">{{ t('test.close') }}</NButton>
    </template>
  </TModalShell>
</template>

<script setup lang="ts">
/**
 * Bank rules: "the ledger has no counterpart for this line, but I know what it
 * is." The match engine answers the other half; rules only run when it comes up
 * empty, because booking a second entry for money already on the books is
 * double-entry in the bad sense.
 *
 * Order is the rule - first match wins (QuickBooks semantics) - so the list is
 * priority-ordered, movable, and says so at the top.
 */
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, h, onMounted, ref } from 'vue'
import { NAlert, NButton, NDivider, NInput, NSelect, NSpin, NSwitch, type DataTableColumns } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TModalShell from '../../components/overlay/TModalShell.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { TEmpty } from '@tnzi/ui'
import TRuleBuilder, { type RuleConditionRow } from '../../components/finance/TRuleBuilder.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/row-actions'
import { useSafeMessage } from '../_shared/safe-message'
import { makePageTranslator } from '../_shared/translate'
import { useAdminClient } from '../../plugin/client'
import { createFinanceOptionSources } from './options'
import { buildBankRuleColumns, type BankRuleRow } from './bank-rule-config'
import { fmtDate, moneyCell } from './money'
import {
  createFinanceBridge,
  BankFeedDocType,
  BankRuleDirection,
  PAYMENT_METHODS,
  type BankRuleTestResultDto,
  type CreateBankRuleDto,
} from '../../services/bridges/finance-bridge'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.bankRules')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const title = 'tnzi.admin.modules.finance.bankRules.title'

/** The form model carries the same shape the API takes. */
interface RuleForm {
  name: string
  accountId: string | null
  direction: string
  matchMode: string
  docType: string
  counterAccountId: string | null
  paymentMethod: string | null
  autoApply: boolean
  isEnabled: boolean
  conditions: RuleConditionRow[]
}

/** Seed the defaults a blank create form needs, in place, so v-model binds. */
function form(data: Record<string, unknown> | null | undefined): RuleForm {
  const d = (data ?? {}) as Record<string, unknown>
  d.name ??= ''
  d.accountId ??= null
  d.direction ??= BankRuleDirection.Any
  d.matchMode ??= 'All'
  d.docType ??= BankFeedDocType.Expense
  d.counterAccountId ??= null
  d.paymentMethod ??= null
  d.autoApply ??= false
  d.isEnabled ??= true
  d.conditions ??= []
  return d as unknown as RuleForm
}

function toPayload(d: Record<string, unknown>): CreateBankRuleDto {
  const f = form(d)
  return {
    name: String(f.name ?? '').trim(),
    isEnabled: f.isEnabled !== false,
    accountId: f.accountId || null,
    direction: f.direction as CreateBankRuleDto['direction'],
    matchMode: f.matchMode as CreateBankRuleDto['matchMode'],
    docType: f.docType as CreateBankRuleDto['docType'],
    counterAccountId: f.counterAccountId || null,
    paymentMethod: f.paymentMethod || null,
    autoApply: f.autoApply === true,
    conditions: (f.conditions ?? []).map((c) => ({ field: c.field, operator: c.operator, value: c.value })),
  } as CreateBankRuleDto
}

const columns = computed(() => buildBankRuleColumns(t))

const crud = useCrudPage<BankRuleRow>({
  pageId: 'finance.bankRules',
  permission: 'finance.bankRule',
  columns: columns.value,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.bankRules.fetch(q),
  loadDetailById: (id) => bridge.bankRules.getById(String(id)) as Promise<BankRuleRow | null>,
  createData: (d) => bridge.bankRules.create(toPayload(d)) as Promise<BankRuleRow>,
  updateData: (id, d) => bridge.bankRules.update(String(id), toPayload(d)) as Promise<BankRuleRow>,
  deleteData: (ids) => bridge.bankRules.delete(ids.map(String)),
})

// ── Option sources ──────────────────────────────────────────────
const fundsAccountOptions = computed(() => sources.fundsAccountOptions.value)
const leafAccountOptions = computed(() => sources.leafAccountOptions.value)
const methodOptions = computed(() => PAYMENT_METHODS.map((m) => ({ label: m, value: m })))

const directionOptions = computed(() => [
  // A bank rule scopes itself to one funds account's own side, so it speaks the
  // money-in / money-out vocabulary. The words come from the shared dictionary
  // rather than a page-local copy - three copies is how the wording drifted.
  { label: t('admin.shared.moneyFlow.any'), value: BankRuleDirection.Any },
  { label: t('admin.shared.moneyFlow.in'), value: BankRuleDirection.MoneyIn },
  { label: t('admin.shared.moneyFlow.out'), value: BankRuleDirection.MoneyOut },
])

const docTypeOptions = computed(() => [
  { label: t('docType.expense'), value: BankFeedDocType.Expense },
  { label: t('docType.paymentEntry'), value: BankFeedDocType.PaymentEntry },
  { label: t('docType.transfer'), value: BankFeedDocType.Transfer },
])

// ── Priority moves ──────────────────────────────────────────────
/**
 * Order is the rule, so moving one is a first-class action rather than an
 * editable number field: a spinner on "priority" invites two rules with the
 * same number, and then nobody can say which one wins.
 */
async function move(row: BankRuleRow, delta: number) {
  const ids = crud.items.value.map((r) => String(r.id ?? ''))
  const from = ids.indexOf(String(row.id ?? ''))
  const to = from + delta
  if (from < 0 || to < 0 || to >= ids.length) return

  const next = [...ids]
  next.splice(to, 0, ...next.splice(from, 1))
  try {
    await bridge.bankRules.reorder(next)
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

// ── Dry run ─────────────────────────────────────────────────────
const testShow = ref(false)
const testing = ref(false)
const testResult = ref<BankRuleTestResultDto | null>(null)
const testedRuleId = ref<string | null>(null)

/** Lines this rule matches but a higher-priority rule actually takes. */
const stolenCount = computed(
  () => testResult.value?.rows.filter((r) => r.winningRuleId !== testedRuleId.value).length ?? 0,
)

async function runTest(row: BankRuleRow) {
  testedRuleId.value = String(row.id ?? '')
  testResult.value = null
  testShow.value = true
  testing.value = true
  try {
    testResult.value = await bridge.bankRules.test(String(row.id ?? ''), { accountId: row.accountId ?? null })
  } catch (e) {
    testShow.value = false
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    testing.value = false
  }
}

const testColumns = computed<DataTableColumns<Record<string, unknown>>>(() => [
  { key: 'txnDate', title: t('test.date'), width: 110, render: (r) => fmtDate(r.txnDate as string) },
  { key: 'description', title: t('test.description'), minWidth: 200, render: (r) => (r.description as string) ?? EMPTY_DASH },
  { key: 'amount', title: t('test.amount'), width: 120, align: 'right', render: (r) => moneyCell(r.amount as number) },
  {
    key: 'winningRuleName',
    title: t('test.winner'),
    width: 160,
    render: (r) =>
      h(
        'span',
        { class: r.winningRuleId === testedRuleId.value ? undefined : 'fin-rules__stolen' },
        (r.winningRuleName as string) ?? EMPTY_DASH,
      ),
  },
])

const rowActions = computed<RowAction<BankRuleRow>[]>(() => [
  {
    key: 'test',
    label: 'actions.test',
    onClick: (row) => void runTest(row),
  },
  {
    key: 'moveUp',
    label: 'actions.moveUp',
    disabled: () => !can('finance.bankRule.update'),
    show: (row) => crud.items.value.findIndex((r) => r.id === row.id) > 0,
    onClick: (row) => void move(row, -1),
  },
  {
    key: 'moveDown',
    label: 'actions.moveDown',
    disabled: () => !can('finance.bankRule.update'),
    show: (row) => {
      const i = crud.items.value.findIndex((r) => r.id === row.id)
      return i >= 0 && i < crud.items.value.length - 1
    },
    onClick: (row) => void move(row, 1),
  },
  editAction(crud),
  deleteAction(crud),
])

onMounted(() => {
  void sources.ensureFundsAccounts()
  void sources.ensureLeafAccounts()
})
</script>

<style scoped>
.fin-rules__hint {
  font-size: 12px;
}

.fin-rules__form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-rules__grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.fin-rules__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.fin-rules__label {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}

.fin-rules__section {
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--tnzi-base-text-muted);
}

.fin-rules__divider {
  margin: 2px 0;
}

.fin-rules__switch {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}

.fin-rules__switch-label {
  font-size: 13px;
}

.fin-rules__switch-hint {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  margin-top: 2px;
}

.fin-rules__test {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-rules__test-summary {
  margin: 0;
  font-size: 13px;
}

:deep(.fin-rules__stolen) {
  color: var(--tnzi-warning);
  font-weight: 600;
}

@media (max-width: 767px) {
  .fin-rules__grid {
    grid-template-columns: 1fr;
  }
}
</style>
