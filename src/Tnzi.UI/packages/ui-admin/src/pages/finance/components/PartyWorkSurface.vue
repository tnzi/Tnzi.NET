<template>
  <TTabsPage
    v-model:section="section"
    :sections="sections"
    :title="party?.name ?? tp('loading')"
    :back="{ fallback: listPath }"
    :translate="tp"
  >
    <!-- Identity: name + the two facts that change how you read the page. -->
    <template #title>
      <span class="fin-party__title">
        <span class="fin-party__name">{{ party?.name ?? tp('loading') }}</span>
        <NTag v-if="party?.code" size="small" :bordered="false" class="fin-party__code">{{ party.code }}</NTag>
        <NTag v-if="party && party.isActive === false" size="small" type="warning" :bordered="false">
          {{ tp('inactive') }}
        </NTag>
      </span>
    </template>

    <!-- Context actions: what people came here to do, in the bar, not in a menu. -->
    <template #actions>
      <NButton v-if="canCreateDoc" type="primary" tertiary size="small" @click="newDocument">
        <template #icon><TSvgIcon :icon="isCustomer ? 'mdi:file-document-plus-outline' : 'mdi:receipt-text-plus-outline'" :size="16" /></template>
        {{ isCustomer ? tp('actions.newInvoice') : tp('actions.newBill') }}
      </NButton>
      <NButton v-if="canCreateDoc" tertiary size="small" @click="newPayment">
        <template #icon><TSvgIcon icon="mdi:cash-multiple" :size="16" /></template>
        {{ isCustomer ? tp('actions.receivePayment') : tp('actions.payVendor') }}
      </NButton>
      <NButton tertiary size="small" @click="reload">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="16" /></template>
        {{ tp('actions.refresh') }}
      </NButton>
    </template>

    <!-- The balance strip stays visible on every tab: it is the answer to the
         question that brought the user here, and it should not scroll away when
         they go looking for the evidence. -->
    <template #kpis>
      <TKpiRow cols="1 s:2 m:4">
        <TKpiCard
          :label="isCustomer ? tp('kpi.owedToYou') : tp('kpi.youOwe')"
          :value="moneyOrDash(summary?.openBalance)"
          :animated="false"
          icon="mdi:scale-balance"
          :tone="(summary?.openBalance ?? 0) > 0 ? 'default' : 'success'"
        />
        <TKpiCard
          :label="tp('kpi.overdue')"
          :value="moneyOrDash(summary?.overdue)"
          :animated="false"
          icon="mdi:clock-alert-outline"
          :tone="(summary?.overdue ?? 0) > 0 ? 'error' : 'success'"
        />
        <TKpiCard
          :label="tp('kpi.openDocuments')"
          :value="summary?.openDocumentCount ?? null"
          icon="mdi:file-clock-outline"
        />
        <TKpiCard
          :label="isCustomer ? tp('kpi.salesThisPeriod') : tp('kpi.spendThisPeriod')"
          :value="moneyOrDash(summary?.periodTotal)"
          :animated="false"
          icon="mdi:chart-timeline-variant"
        />
      </TKpiRow>
    </template>

    <!-- ── Overview ── -->
    <template #overview>
      <div class="fin-party__overview">
        <NAlert v-if="loadError" type="error" :bordered="false" closable @close="loadError = null">
          {{ loadError }}
        </NAlert>

        <section class="fin-party__block">
          <h4 class="fin-party__block-title">{{ tp('aging.title') }}</h4>
          <NSpin :show="summaryLoading">
            <TAgingBar v-if="summary" :buckets="summary.buckets" :currency="summary.baseCurrency" :translate="tp" />
            <TEmpty v-else-if="!summaryLoading" :text="tp('aging.empty')" />
          </NSpin>
        </section>

        <section class="fin-party__block">
          <h4 class="fin-party__block-title">{{ tp('recent.title') }}</h4>
          <TTransactionList
            :entries="recent"
            :loading="txnLoading"
            :total="recent.length"
            :page-size="RECENT_SIZE"
            scope="all"
            :show-scope="false"
            :translate="tp"
            :doc-type-label="financeSourceTypeLabel"
            :status-translate="tFinance"
            @open="openDocument"
          />
          <div class="fin-party__more">
            <NButton text type="primary" size="small" @click="section = 'transactions'">
              {{ tp('recent.viewAll') }}
            </NButton>
          </div>
        </section>

        <section class="fin-party__block">
          <h4 class="fin-party__block-title">{{ tp('contact.title') }}</h4>
          <NDescriptions :column="2" size="small" bordered>
            <NDescriptionsItem :label="tp('fields.email')">{{ party?.email ?? EMPTY_DASH }}</NDescriptionsItem>
            <NDescriptionsItem :label="tp('fields.phone')">{{ party?.phone ?? EMPTY_DASH }}</NDescriptionsItem>
            <NDescriptionsItem :label="tp('fields.currency')">{{ party?.currency ?? summary?.baseCurrency ?? EMPTY_DASH }}</NDescriptionsItem>
            <NDescriptionsItem :label="tp('fields.paymentTerms')">
              {{ party?.paymentTermsDays == null ? EMPTY_DASH : tp('fields.netDays').replace('{days}', String(party.paymentTermsDays)) }}
            </NDescriptionsItem>
            <NDescriptionsItem :label="tp('fields.lastActivity')">
              {{ summary?.lastTransactionDate ? fmtDate(summary.lastTransactionDate) : EMPTY_DASH }}
            </NDescriptionsItem>
            <NDescriptionsItem :label="tp('fields.address')" :span="2">{{ primaryAddress ?? EMPTY_DASH }}</NDescriptionsItem>
          </NDescriptions>
        </section>
      </div>
    </template>

    <!-- ── Transactions ── -->
    <template #transactions>
      <TTransactionList
        :entries="entries"
        :loading="txnLoading"
        :total="txnTotal"
        :page-index="pageIndex"
        :page-size="PAGE_SIZE"
        :scope="scope"
        :open-count="summary?.openDocumentCount"
        :translate="tp"
        :doc-type-label="financeSourceTypeLabel"
        :status-translate="tFinance"
        @update:scope="onScope"
        @update:page-index="onPage"
        @open="openDocument"
      />
    </template>

    <!-- ── Banking (remit-to details) ── -->
    <template #banking>
      <PartyBankAccountsPanel
        v-if="id"
        :bridge="bridge"
        :party-type="partyType"
        :party-id="id"
        :t="tp"
        :can-manage="canManageRemit"
      />
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
/**
 * The customer / vendor work surface.
 *
 * A party page answers three questions in order: **what is the position**
 * (balance strip + aging), **what happened** (the transaction ledger, every row
 * drillable to its source document), and **who are they** (contact + remit-to).
 * A list row that only opens an edit form answers none of them, which is why
 * this page exists rather than a drawer.
 *
 * Customer and vendor share this component: the domain is mirror-imaged
 * (they owe us / we owe them), not different. Everything that differs is
 * derived from `partyType`, so the two pages cannot drift apart.
 */
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton, NDescriptions, NDescriptionsItem, NSpin, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TTabsPage, { type TabSection } from '../../../components/layout/TTabsPage.vue'
import TKpiRow from '../../../components/data/TKpiRow.vue'
import TKpiCard from '../../../components/data/TKpiCard.vue'
import { TEmpty } from '@tnzi/ui'
import TAgingBar from '../../../components/finance/TAgingBar.vue'
import TTransactionList from '../../../components/finance/TTransactionList.vue'
import PartyBankAccountsPanel from './PartyBankAccountsPanel.vue'
import { useAdminClient } from '../../../plugin/client'
import { usePermissionGuard } from '../../../headless/usePermissionGuard'
import { useTabTitle } from '../../../headless/useTabTitle'
import { useBreadcrumbLabel } from '../../../headless/use-breadcrumb'
import { makePageTranslator } from '../../_shared/translate'
import { financeSourceTypeLabel } from '../source-type'
import { fmtDate, fmtMoney } from '../money'
import {
  createFinanceBridge,
  FinancePartyType,
  type CustomerDto,
  type PartyLedgerEntryDto,
  type PartyLedgerSummaryDto,
  type VendorDto,
} from '../../../services/bridges/finance-bridge'

const props = defineProps<{
  /** Route param. */
  id: string
  partyType: FinancePartyType
}>()

const PAGE_SIZE = 20
const RECENT_SIZE = 5

const bridge = createFinanceBridge({ client: useAdminClient() })
const router = useRouter()
const { can } = usePermissionGuard()
const tp = makePageTranslator('finance.party')
/** Rooted at `finance` so `TDocStatusBadge` resolves `docs.status.*`. */
const tFinance = makePageTranslator('finance')

const isCustomer = computed(() => props.partyType === FinancePartyType.Customer)
const api = computed(() => (isCustomer.value ? bridge.customers : bridge.vendors))
// 经路由名解析而非拼路径：`defineAdminApp({ basePath })` 重写前缀后，
// 写死的 `/admin/...` 只在冷深链（无站内历史，正好是刷新那一刻）才会被用到，
// 也正是那一刻会落到 404。
const listPath = computed(
  () => router.resolve({ name: isCustomer.value ? 'finance.customers' : 'finance.vendors' }).path,
)

const party = ref<(CustomerDto & VendorDto) | null>(null)
const summary = ref<PartyLedgerSummaryDto | null>(null)
const entries = ref<PartyLedgerEntryDto[]>([])
const recent = ref<PartyLedgerEntryDto[]>([])
const txnTotal = ref(0)
const pageIndex = ref(1)
const scope = ref<'all' | 'open'>('all')
const summaryLoading = ref(false)
const txnLoading = ref(false)
const loadError = ref<string | null>(null)
const section = ref('overview')

// `scroll: true` on every tab: these are variable-height mixed blocks, not a
// single flex-filling table. Without it the pane inherits `min-height: 0` from
// the fill-height chain, collapses to zero, and the tab renders BLANK on a short
// viewport - not "content below the fold", nothing at all.
const sections = computed<TabSection[]>(() => [
  { name: 'overview', label: tp('tabs.overview'), scroll: true },
  { name: 'transactions', label: tp('tabs.transactions'), scroll: true },
  { name: 'banking', label: tp('tabs.banking'), scroll: true },
])

// The tab strip and the breadcrumb leaf both show the record, not the route
// title - two open party tabs are otherwise indistinguishable.
useTabTitle(() => party.value?.name)
useBreadcrumbLabel(() => party.value?.name)

const canCreateDoc = computed(() => can('finance.document.create'))
const canManageRemit = computed(() => can('finance.partyBank.create') || can('finance.partyBank.update'))
const primaryAddress = computed(() => party.value?.billingAddress ?? party.value?.address ?? null)

/** Money in the party's own currency, falling back to the ledger's base. */
function moneyOrDash(amount?: number | null): string {
  if (amount == null) return EMPTY_DASH
  return fmtMoney(amount, party.value?.currency ?? summary.value?.baseCurrency)
}

function message(e: unknown): string {
  return e instanceof Error ? e.message : String(e)
}

async function loadParty(): Promise<void> {
  try {
    party.value = (await api.value.get(props.id)) as CustomerDto & VendorDto
  } catch (e) {
    loadError.value = message(e)
  }
}

async function loadSummary(): Promise<void> {
  summaryLoading.value = true
  try {
    summary.value = await api.value.summary(props.id)
  } catch (e) {
    loadError.value = message(e)
  } finally {
    summaryLoading.value = false
  }
}

async function loadTransactions(): Promise<void> {
  txnLoading.value = true
  try {
    const result = await api.value.transactions(props.id, {
      pageIndex: pageIndex.value,
      pageSize: PAGE_SIZE,
      openOnly: scope.value === 'open' ? true : undefined,
    })
    entries.value = result.items ?? []
    txnTotal.value = result.totalCount ?? 0
    // The overview's "recent activity" is the first page's head - no second
    // round-trip for what we already hold.
    if (pageIndex.value === 1 && scope.value === 'all') recent.value = entries.value.slice(0, RECENT_SIZE)
  } catch (e) {
    loadError.value = message(e)
  } finally {
    txnLoading.value = false
  }
}

async function reload(): Promise<void> {
  loadError.value = null
  await Promise.all([loadParty(), loadSummary(), loadTransactions()])
}

function onScope(next: 'all' | 'open'): void {
  scope.value = next
  pageIndex.value = 1
  void loadTransactions()
}

function onPage(next: number): void {
  pageIndex.value = next
  void loadTransactions()
}

/** Route name + read-only detail deep link per source token. */
const DRILL_ROUTES: Record<string, string> = {
  Invoice: 'finance.invoices',
  Bill: 'finance.bills',
  CreditMemo: 'finance.creditMemos',
  Expense: 'finance.expenses',
  PaymentEntry: 'finance.payments',
}

function openDocument(entry: PartyLedgerEntryDto): void {
  const name = DRILL_ROUTES[entry.docType]
  if (!name) return
  void router.push({ name, query: { detail: `view:${entry.docId}` } })
}

function newDocument(): void {
  void router.push({
    name: isCustomer.value ? 'finance.invoices' : 'finance.bills',
    // `new` is useDetail's create token (see its URL codec) - `create` is the
    // action name, not the wire value, and would be dropped as unparseable.
    query: { entry: 'new', party: props.id },
  })
}

function newPayment(): void {
  void router.push({
    name: 'finance.payments',
    query: { party: props.id, direction: isCustomer.value ? 'Inbound' : 'Outbound' },
  })
}

watch(
  () => [props.id, props.partyType] as const,
  () => {
    pageIndex.value = 1
    scope.value = 'all'
    void reload()
  },
  { immediate: true },
)
</script>

<style scoped>
.fin-party__title {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.fin-party__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.fin-party__code {
  font-family: var(--tnzi-font-mono);
}

.fin-party__overview {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.fin-party__block {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.fin-party__block-title {
  margin: 0;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--tnzi-base-text-muted);
}

.fin-party__more {
  display: flex;
  justify-content: flex-end;
}
</style>
