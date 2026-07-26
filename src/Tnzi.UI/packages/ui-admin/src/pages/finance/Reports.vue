<template>
  <TTabsPage
    v-model:section="activeSection"
    :title="title"
    icon="mdi:chart-box-outline"
    :translate="t"
    :sections="tabs"
    default-section="trial-balance"
  >
      <!-- Per-tab CSV export (server-generated, UTF-8 BOM) + balance-summary maintenance -->
      <!-- Cross-tab controls live on the canvas strip: they belong to the whole
           report set, and the header bar cannot hold them without eating the title. -->
      <template #kpis>
        <div class="fin-reports__controls">
          <TFinanceViewToggle :translate="t" />
          <TPeriodPicker
            :mode="asOfTabs.includes(activeSection) ? 'as-of' : 'range'"
            :show-comparison="activeSection === 'profit-and-loss'"
            :translate="t"
            @change="onPeriodChange(activeSection)"
          />
        </div>
      </template>

      <template #actions="{ active }">
        <NButton size="small" :loading="exporting" :disabled="!canExport(active)" @click="exportCsv(active)">
          {{ t('actions.export') }}
        </NButton>
        <NDropdown
          v-if="showMaintenance"
          trigger="click"
          :options="maintenanceOptions"
          @select="onMaintenanceSelect"
        >
          <NButton size="small" :loading="maintenanceLoading">{{ t('maintenance.menu') }}</NButton>
        </NDropdown>
      </template>

      <!-- ── Trial Balance ─────────────────────────────────── -->
      <template #trial-balance>
        <div class="fin-reports__toolbar">
          <NButton size="small" type="primary" :loading="tbLoading" @click="runTrialBalance">{{ t('actions.run') }}</NButton>
        </div>
        <template v-if="tb">
          <TResponsiveTable :columns="tbColumns" :data="tb.rows" size="small" mobile="scroll" :pagination="false" :bordered="false" />
          <div class="fin-reports__totals">
            <span>{{ t('trialBalance.totalPeriodDebit') }}: <strong>{{ fmtAmount(tb.totalPeriodDebit) }}</strong></span>
            <span>{{ t('trialBalance.totalPeriodCredit') }}: <strong>{{ fmtAmount(tb.totalPeriodCredit) }}</strong></span>
            <span>{{ t('trialBalance.totalClosing') }}: <strong>{{ fmtAmount(tb.totalClosingBalance) }}</strong></span>
          </div>
        </template>
        <TEmpty v-else-if="!tbLoading" :text="t('runHint')" />
      </template>

      <!-- ── Balance Sheet ─────────────────────────────────── -->
      <template #balance-sheet>
        <div class="fin-reports__toolbar">
          <NButton size="small" type="primary" :loading="bsLoading" @click="runBalanceSheet">{{ t('actions.run') }}</NButton>
        </div>
        <template v-if="bs">
          <div class="fin-reports__bs-grid">
            <div class="fin-reports__section">
              <h4>{{ t('balanceSheet.assets') }}</h4>
              <TResponsiveTable :columns="rowColumns" :data="bs.assets" size="small" mobile="scroll" :pagination="false" :bordered="false" />
              <div class="fin-reports__section-total">{{ t('balanceSheet.totalAssets') }}: <strong>{{ fmtAmount(bs.totalAssets) }}</strong></div>
            </div>
            <div class="fin-reports__section">
              <h4>{{ t('balanceSheet.liabilities') }}</h4>
              <TResponsiveTable :columns="rowColumns" :data="bs.liabilities" size="small" mobile="scroll" :pagination="false" :bordered="false" />
              <div class="fin-reports__section-total">{{ t('balanceSheet.totalLiabilities') }}: <strong>{{ fmtAmount(bs.totalLiabilities) }}</strong></div>
              <h4>{{ t('balanceSheet.equity') }}</h4>
              <TResponsiveTable :columns="rowColumns" :data="bs.equity" size="small" mobile="scroll" :pagination="false" :bordered="false" />
              <div class="fin-reports__section-total">
                {{ t('balanceSheet.currentEarnings') }}: <strong>{{ fmtAmount(bs.currentEarnings) }}</strong>
                · {{ t('balanceSheet.totalEquity') }}: <strong>{{ fmtAmount(bs.totalEquity) }}</strong>
              </div>
            </div>
          </div>
          <div class="fin-reports__totals">
            <span :class="bs.balanceCheck === 0 ? 'fin-reports__check--ok' : 'fin-reports__check--bad'">
              {{ t('balanceSheet.balanceCheck') }}: {{ fmtAmount(bs.balanceCheck) }}
            </span>
          </div>
        </template>
        <TEmpty v-else-if="!bsLoading" :text="t('runHint')" />
      </template>

      <!-- ── Profit & Loss ─────────────────────────────────── -->
      <template #profit-and-loss>
        <div class="fin-reports__toolbar">
          <NButton size="small" type="primary" :loading="plLoading" @click="runProfitAndLoss">{{ t('actions.run') }}</NButton>
        </div>
        <template v-if="pl">
          <div class="fin-reports__bs-grid">
            <div class="fin-reports__section">
              <h4>{{ t('profitAndLoss.income') }}</h4>
              <TResponsiveTable :columns="plColumns" :data="pl.income" size="small" mobile="scroll" :pagination="false" :bordered="false" />
              <div class="fin-reports__section-total">{{ t('profitAndLoss.totalIncome') }}: <strong>{{ fmtAmount(pl.totalIncome) }}</strong></div>
            </div>
            <div class="fin-reports__section">
              <h4>{{ t('profitAndLoss.expenses') }}</h4>
              <TResponsiveTable :columns="plColumns" :data="pl.expenses" size="small" mobile="scroll" :pagination="false" :bordered="false" />
              <div class="fin-reports__section-total">{{ t('profitAndLoss.totalExpenses') }}: <strong>{{ fmtAmount(pl.totalExpenses) }}</strong></div>
            </div>
          </div>
          <div class="fin-reports__totals">
            <span>{{ t('profitAndLoss.netProfit') }}: <strong :class="pl.netProfit >= 0 ? 'fin-reports__check--ok' : 'fin-reports__check--bad'">{{ fmtAmount(pl.netProfit) }}</strong></span>
            <span v-if="comparing && plPrior">
              {{ priorLabel }}: <strong>{{ formatMoney(plPrior.netProfit) }}</strong>
              <component :is="renderVariance(pl.netProfit, plPrior.netProfit)" />
            </span>
          </div>
        </template>
        <TEmpty v-else-if="!plLoading" :text="t('runHint')" />
      </template>

      <!-- ── Cash Flow (indirect method) ───────────────────── -->
      <template #cash-flow>
        <div class="fin-reports__toolbar">
          <NButton size="small" type="primary" :loading="cfLoading" @click="runCashFlow">{{ t('actions.run') }}</NButton>
        </div>
        <template v-if="cf">
          <div class="fin-reports__section">
            <h4>{{ t('cashFlow.operating') }}</h4>
            <div class="fin-reports__section-total">{{ t('cashFlow.netProfit') }}: <strong>{{ fmtAmount(cf.netProfit) }}</strong></div>
            <TResponsiveTable v-if="cf.operating.length" :columns="rowColumns" :data="cf.operating" size="small" mobile="scroll" :pagination="false" :bordered="false" />
            <div class="fin-reports__section-total">{{ t('cashFlow.totalOperating') }}: <strong>{{ fmtAmount(cf.totalOperating) }}</strong></div>
          </div>
          <div class="fin-reports__section">
            <h4>{{ t('cashFlow.investing') }}</h4>
            <TResponsiveTable v-if="cf.investing.length" :columns="rowColumns" :data="cf.investing" size="small" mobile="scroll" :pagination="false" :bordered="false" />
            <div class="fin-reports__section-total">{{ t('cashFlow.totalInvesting') }}: <strong>{{ fmtAmount(cf.totalInvesting) }}</strong></div>
          </div>
          <div class="fin-reports__section">
            <h4>{{ t('cashFlow.financing') }}</h4>
            <TResponsiveTable v-if="cf.financing.length" :columns="rowColumns" :data="cf.financing" size="small" mobile="scroll" :pagination="false" :bordered="false" />
            <div class="fin-reports__section-total">{{ t('cashFlow.totalFinancing') }}: <strong>{{ fmtAmount(cf.totalFinancing) }}</strong></div>
          </div>
          <div v-if="cf.unclassified.length" class="fin-reports__section">
            <h4>{{ t('cashFlow.unclassified') }}</h4>
            <TResponsiveTable :columns="rowColumns" :data="cf.unclassified" size="small" mobile="scroll" :pagination="false" :bordered="false" />
            <div class="fin-reports__section-total">{{ t('cashFlow.totalUnclassified') }}: <strong>{{ fmtAmount(cf.totalUnclassified) }}</strong></div>
          </div>
          <div class="fin-reports__totals">
            <span>{{ t('cashFlow.netCashFlow') }}: <strong>{{ fmtAmount(cf.netCashFlow) }}</strong></span>
            <span>{{ t('cashFlow.openingCash') }}: <strong>{{ fmtAmount(cf.openingCash) }}</strong></span>
            <span>{{ t('cashFlow.closingCash') }}: <strong>{{ fmtAmount(cf.closingCash) }}</strong></span>
            <span :class="cf.checkDifference === 0 ? 'fin-reports__check--ok' : 'fin-reports__check--bad'">
              {{ t('cashFlow.check') }}: {{ fmtAmount(cf.checkDifference) }}
            </span>
          </div>
        </template>
        <TEmpty v-else-if="!cfLoading" :text="t('runHint')" />
      </template>

      <!-- ── General Ledger ────────────────────────────────── -->
      <template #general-ledger>
        <div class="fin-reports__toolbar">
          <NSelect
            v-model:value="glAccountId"
            :options="accountOptions"
            size="small"
            filterable
            :placeholder="t('generalLedger.accountPlaceholder')"
            class="fin-reports__account-select"
          />
          <NButton size="small" type="primary" :loading="glLoading" :disabled="!glAccountId" @click="runGeneralLedger(1)">{{ t('actions.run') }}</NButton>
        </div>
        <template v-if="gl">
          <div class="fin-reports__totals fin-reports__totals--top">
            <span>{{ gl.code }} {{ gl.name }}</span>
            <span>{{ t('generalLedger.opening') }}: <strong>{{ fmtAmount(gl.openingBalance) }}</strong></span>
            <span>{{ t('generalLedger.closing') }}: <strong>{{ fmtAmount(gl.closingBalance) }}</strong></span>
          </div>
          <TResponsiveTable :columns="glColumns" :data="gl.lines.items" size="small" mobile="scroll" :pagination="false" :bordered="false" />
          <div class="fin-reports__pagination">
            <NPagination
              :page="gl.lines.pageIndex"
              :page-size="gl.lines.pageSize"
              :item-count="gl.lines.totalCount"
              size="small"
              @update:page="runGeneralLedger"
            />
          </div>
        </template>
        <TEmpty v-else-if="!glLoading" :text="t('runHint')" />
      </template>

      <!-- ── AR Aging ──────────────────────────────────────── -->
      <template #ar-aging>
        <div class="fin-reports__toolbar">
          <NButton size="small" type="primary" :loading="arLoading" @click="runAging('ar')">{{ t('actions.run') }}</NButton>
        </div>
        <template v-if="arAging">
          <TResponsiveTable :columns="agingColumns" :data="arAging.rows" size="small" mobile="scroll" :pagination="false" :bordered="false" />
          <div class="fin-reports__totals">
            <span>{{ t('aging.total') }}: <strong>{{ fmtAmount(arAging.totals.total) }}</strong></span>
            <span>{{ t('aging.over90') }}: <strong>{{ fmtAmount(arAging.totals.over90) }}</strong></span>
          </div>
        </template>
        <TEmpty v-else-if="!arLoading" :text="t('runHint')" />
      </template>

      <!-- ── AP Aging ──────────────────────────────────────── -->
      <template #ap-aging>
        <div class="fin-reports__toolbar">
          <NButton size="small" type="primary" :loading="apLoading" @click="runAging('ap')">{{ t('actions.run') }}</NButton>
        </div>
        <template v-if="apAging">
          <TResponsiveTable :columns="agingColumns" :data="apAging.rows" size="small" mobile="scroll" :pagination="false" :bordered="false" />
          <div class="fin-reports__totals">
            <span>{{ t('aging.total') }}: <strong>{{ fmtAmount(apAging.totals.total) }}</strong></span>
            <span>{{ t('aging.over90') }}: <strong>{{ fmtAmount(apAging.totals.over90) }}</strong></span>
          </div>
        </template>
        <TEmpty v-else-if="!apLoading" :text="t('runHint')" />
      </template>

      <!-- ── Tax Summary ───────────────────────────────────── -->
      <template #tax-summary>
        <div class="fin-reports__toolbar">
          <NButton size="small" type="primary" :loading="taxLoading" @click="runTaxSummary">{{ t('actions.run') }}</NButton>
        </div>
        <template v-if="tax">
          <TResponsiveTable :columns="taxColumns" :data="tax.rows" size="small" mobile="scroll" :pagination="false" :bordered="false" />
          <div class="fin-reports__totals">
            <span>{{ t('taxSummary.totalOutput') }}: <strong>{{ fmtAmount(tax.totalOutputTax) }}</strong></span>
            <span>{{ t('taxSummary.totalInput') }}: <strong>{{ fmtAmount(tax.totalInputTax) }}</strong></span>
            <span>{{ t('taxSummary.totalNet') }}: <strong :class="tax.totalNetTax >= 0 ? 'fin-reports__check--ok' : 'fin-reports__check--bad'">{{ fmtAmount(tax.totalNetTax) }}</strong></span>
          </div>
        </template>
        <TEmpty v-else-if="!taxLoading" :text="t('runHint')" />
      </template>

      <!-- ── Balance-summary verify result (tab-agnostic overlay) ── -->
      <template #overlays>
        <!-- Drill-down: the ledger rows behind whichever figure was clicked. -->
        <TDetailHost :state="drillDetail" :title="drillTitle" :width="900" :footer="false" :translate="t">
          <div class="fin-reports__drill">
            <div class="fin-reports__drill-head">
              <NInput
                :value="drill.keyword.value"
                size="small"
                clearable
                :placeholder="t('drilldown.search')"
                class="fin-reports__drill-search"
                @update:value="(v: string) => void drill.search(v)"
              />
              <NButton size="tiny" quaternary @click="void drill.toggleOrder()">
                {{ drill.descending.value ? t('drilldown.newestFirst') : t('drilldown.oldestFirst') }}
              </NButton>
              <span class="fin-reports__drill-period">{{ formatAccountingDateRange(period.from, period.to) }}</span>
            </div>

            <!-- A filtered ledger has no continuous running balance; the backend
                 zeroes the balances and flags it, so say so instead of showing 0. -->
            <NAlert v-if="!drill.balancesApply.value && drill.report.value" type="info" :bordered="false" class="fin-reports__drill-alert">
              {{ t('drilldown.filteredNoBalance') }}
            </NAlert>

            <NSpin :show="drill.loading.value">
              <TResponsiveTable
                :columns="drillColumnsWithBalance"
                :data="drill.rows.value"
                :row-key="(r: GeneralLedgerLineDto) => `${r.journalEntryId}-${r.postingDate}-${r.debit}-${r.credit}`"
                size="small"
                mobile="scroll"
                :pagination="false"
                :bordered="false"
              />
              <TEmpty v-if="!drill.loading.value && drill.rows.value.length === 0" :text="t('drilldown.empty')" />
            </NSpin>

            <NPagination
              v-if="drill.total.value > drill.pageSize"
              :page="drill.pageIndex.value"
              :page-size="drill.pageSize"
              :item-count="drill.total.value"
              size="small"
              @update:page="(n: number) => void drill.goToPage(n)"
            />
          </div>
        </TDetailHost>

        <TModalShell
          v-model:show="verifyShow"
          :title="t('maintenance.verifyTitle')"
          :width="760"
        >
          <template v-if="verifyResult">
            <div class="fin-reports__totals fin-reports__totals--top">
              <TStatusBadge
                :value="verifyResult.isConsistent"
                :type="verifyResult.isConsistent ? 'success' : 'error'"
                :label="verifyResult.isConsistent ? t('maintenance.consistent') : t('maintenance.inconsistent')"
              />
              <span>{{ t('maintenance.checkedBuckets') }}: <strong>{{ verifyResult.checkedBuckets }}</strong></span>
              <span>{{ t('maintenance.totalDifferences') }}: <strong>{{ verifyResult.totalDifferences }}</strong></span>
            </div>
            <TResponsiveTable
              v-if="verifyResult.differences.length"
              :columns="diffColumns"
              :data="verifyResult.differences"
              size="small"
              mobile="scroll"
              :pagination="false"
              :bordered="false"
            />
            <p v-if="verifyResult.totalDifferences > verifyResult.differences.length" class="fin-reports__truncation">
              {{ t('maintenance.truncated', { shown: verifyResult.differences.length, total: verifyResult.totalDifferences }) }}
            </p>
            <TEmpty v-else-if="verifyResult.isConsistent" :text="t('maintenance.consistentHint')" />
          </template>
        </TModalShell>
      </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, h, onMounted, ref } from 'vue'
import { NAlert, NButton, NDropdown, NInput, NPagination, NSelect, NSpin, useDialog, type DataTableColumns } from 'naive-ui'
import TTabsPage, { type TabSection } from '../../components/layout/TTabsPage.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TEmpty from '../../components/data/TEmpty.vue'
import TModalShell from '../../components/overlay/TModalShell.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import {
  createFinanceBridge,
  BalanceSummaryDifferenceKind,
  type AgingReportDto,
  type AgingRowDto,
  type AccountTreeDto,
  type BalanceSheetReportDto,
  type BalanceSummaryDifferenceDto,
  type BalanceSummaryVerifyDto,
  type GeneralLedgerLineDto,
  type GeneralLedgerReportDto,
  type ProfitAndLossReportDto,
  type ReportAccountRowDto,
  type CashFlowReportDto,
  type TaxSummaryReportDto,
  type TaxSummaryRowDto,
  type TrialBalanceReportDto,
  type TrialBalanceRowDto,
} from '../../services/bridges/finance-bridge'
import TPeriodPicker from '../../components/finance/TPeriodPicker.vue'
import TFinanceViewToggle from '../../components/finance/TFinanceViewToggle.vue'
import TMoney from '../../components/finance/TMoney.vue'
import { moneyPairColumns } from '../../components/finance/money-columns'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import { useFinancePeriod } from '../../headless/useFinancePeriod'
import { useGlDrilldown, type GlDrilldownTarget } from '../../headless/useGlDrilldown'
import { useDetail } from '../../headless/useDetail'
import { formatAccountingDateRange, formatMoney, formatPercent, srMoney, variance } from '../../utils/finance-format'
import { useAdminClient } from '../../plugin/client'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { downloadBlob } from '@tnzi/core'
import { amountCell, fmtAmount, tsToIsoDate, fmtDate } from './money'
import { financeSourceTypeLabel } from './source-type'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.reports')
const message = useSafeMessage()

const title = 'tnzi.admin.modules.finance.reports.title'
// Primary tabs. TTabsPage owns the `?section=` deep-linking + Back/Forward.
// Every pane holds mixed variable-height content (toolbar + tables + totals),
// so each owns its own scroll.
const tabs: TabSection[] = [
  { name: 'trial-balance', label: t('tabs.trialBalance'), scroll: true },
  { name: 'balance-sheet', label: t('tabs.balanceSheet'), scroll: true },
  { name: 'profit-and-loss', label: t('tabs.profitAndLoss'), scroll: true },
  { name: 'cash-flow', label: t('tabs.cashFlow'), scroll: true },
  { name: 'general-ledger', label: t('tabs.generalLedger'), scroll: true },
  { name: 'ar-aging', label: t('tabs.arAging'), scroll: true },
  { name: 'ap-aging', label: t('tabs.apAging'), scroll: true },
  { name: 'tax-summary', label: t('tabs.taxSummary'), scroll: true },
]

// One reporting period for the whole page (and for every other finance
// surface): `useFinancePeriod` is module-level, so moving from P&L to the
// general ledger keeps the window instead of silently resetting it.
const { period, comparisonPeriod, comparison } = useFinancePeriod()

/** Point-in-time reports: the control collapses to a single date. */
const asOfTabs = ['balance-sheet', 'ar-aging', 'ap-aging']

/** Mirrors the active tab so the canvas-strip controls can adapt to it. */
const activeSection = ref('trial-balance')

/** Re-run whichever report the user is looking at when the period changes. */
function onPeriodChange(active: string) {
  const runners: Record<string, () => void> = {
    'trial-balance': () => void runTrialBalance(),
    'balance-sheet': () => void runBalanceSheet(),
    'profit-and-loss': () => void runProfitAndLoss(),
    'cash-flow': () => void runCashFlow(),
    'general-ledger': () => void runGeneralLedger(1),
    'ar-aging': () => void runAging('ar'),
    'ap-aging': () => void runAging('ap'),
    'tax-summary': () => void runTaxSummary(),
  }
  runners[active]?.()
}

// ── Drill-down ──────────────────────────────────────────────────
// Stripe's rule: users do not trust numbers they cannot verify. Every account
// row's balance is a button that opens the ledger rows behind it, for the same
// period, without re-navigating and retyping the range.
const drill = useGlDrilldown({ bridge, period: () => period.value })
const drillDetail = useDetail<{ id: string }>({ mode: 'drawer', url: 'ledger' })

async function openDrilldown(target: GlDrilldownTarget) {
  await drillDetail.open('view')
  await drill.openFor(target)
}

const drillTitle = computed(() => {
  const target = drill.target.value
  if (!target) return t('drilldown.title')
  const name = [target.accountCode, target.accountName].filter(Boolean).join(' ')
  return name || t('drilldown.title')
})

const drillColumns = computed<DataTableColumns<GeneralLedgerLineDto>>(() => [
  { key: 'postingDate', title: t('columns.postingDate'), width: 110, render: (r: GeneralLedgerLineDto) => fmtDate(r.postingDate) },
  { key: 'entryNumber', title: t('generalLedger.entryNumber'), width: 120, render: (r: GeneralLedgerLineDto) => r.entryNumber ?? EMPTY_DASH },
  { key: 'memo', title: t('columns.memo'), minWidth: 160, render: (r: GeneralLedgerLineDto) => r.memo ?? EMPTY_DASH },
  // A drill-down lands on whatever account the reader clicked - revenue and
  // equity rows included - so this is the ledger, not one account's cash flow.
  ...moneyPairColumns<GeneralLedgerLineDto>({
    presentation: 'ledger',
    translate: t,
    debit: (r) => r.debit,
    credit: (r) => r.credit,
  }),
])

/** Balance column is dropped while a filter is active: see `isFiltered`. */
const drillColumnsWithBalance = computed<DataTableColumns<GeneralLedgerLineDto>>(() =>
  drill.balancesApply.value
    ? [...drillColumns.value, { key: 'runningBalance', title: t('generalLedger.balance'), width: 130, align: 'right', render: (r: GeneralLedgerLineDto) => h(TMoney, { value: r.runningBalance, strong: true }) }]
    : drillColumns.value,
)

function showError(error: unknown) {
  message.error(error instanceof Error ? error.message : String(error))
}

// ── Trial Balance ───────────────────────────────────────────────
const tb = ref<TrialBalanceReportDto | null>(null)
const tbLoading = ref(false)

const tbColumns: DataTableColumns<TrialBalanceRowDto> = [
  { key: 'code', title: t('columns.code'), width: 100 },
  { key: 'name', title: t('columns.name'), minWidth: 180 },
  { key: 'openingBalance', title: t('trialBalance.opening'), width: 130, render: (r) => amountCell(fmtAmount(r.openingBalance)) },
  { key: 'periodDebit', title: t('trialBalance.periodDebit'), width: 130, render: (r) => amountCell(fmtAmount(r.periodDebit)) },
  { key: 'periodCredit', title: t('trialBalance.periodCredit'), width: 130, render: (r) => amountCell(fmtAmount(r.periodCredit)) },
  { key: 'closingBalance', title: t('trialBalance.closing'), width: 130, render: (r) => amountCell(fmtAmount(r.closingBalance), true) },
]

async function runTrialBalance() {
  tbLoading.value = true
  try {
    tb.value = await bridge.reports.trialBalance(period.value.from, period.value.to)
  } catch (error) {
    showError(error)
  } finally {
    tbLoading.value = false
  }
}

// ── Balance Sheet ───────────────────────────────────────────────
const bs = ref<BalanceSheetReportDto | null>(null)
const bsLoading = ref(false)

const rowColumns: DataTableColumns<ReportAccountRowDto> = [
  { key: 'code', title: t('columns.code'), width: 100 },
  { key: 'name', title: t('columns.name'), minWidth: 160 },
  {
    key: 'balance',
    title: t('columns.balance'),
    width: 140,
    align: 'right',
    render: (r) =>
      h(TMoney, {
        value: r.balance,
        strong: true,
        drilldown: true,
        drilldownHint: t('drilldown.hint'),
        label: r.name,
        onDrilldown: () => void openDrilldown({ accountId: r.accountId, accountCode: r.code, accountName: r.name }),
      }),
  },
]

async function runBalanceSheet() {
  bsLoading.value = true
  try {
    bs.value = await bridge.reports.balanceSheet(period.value.to)
  } catch (error) {
    showError(error)
  } finally {
    bsLoading.value = false
  }
}

// ── Profit & Loss ───────────────────────────────────────────────
const pl = ref<ProfitAndLossReportDto | null>(null)
const plLoading = ref(false)

/** The comparison run, or null when comparison is off. */
const plPrior = ref<ProfitAndLossReportDto | null>(null)

async function runProfitAndLoss() {
  plLoading.value = true
  try {
    // Comparison is two ordinary runs joined in the client. The backend has no
    // `compareTo`, and adding one would buy a single round-trip at the cost of
    // a second reporting contract to keep honest.
    const prior = comparisonPeriod.value
    const [current, previous] = await Promise.all([
      bridge.reports.profitAndLoss(period.value.from, period.value.to),
      prior ? bridge.reports.profitAndLoss(prior.from, prior.to) : Promise.resolve(null),
    ])
    pl.value = current
    plPrior.value = previous
  } catch (error) {
    showError(error)
  } finally {
    plLoading.value = false
  }
}

const comparing = computed(() => comparison.value !== 'none' && plPrior.value !== null)

const priorLabel = computed(() =>
  comparisonPeriod.value ? formatAccountingDateRange(comparisonPeriod.value.from, comparisonPeriod.value.to) : '',
)

/** Prior-period balance by account, so rows line up even when the sets differ. */
const priorByAccount = computed(() => {
  const map = new Map<string, number>()
  for (const row of [...(plPrior.value?.income ?? []), ...(plPrior.value?.expenses ?? [])]) {
    map.set(row.accountId, row.balance)
  }
  return map
})

/** P&L columns gain prior + variance columns while a comparison is active. */
const plColumns = computed<DataTableColumns<ReportAccountRowDto>>(() => {
  if (!comparing.value) return rowColumns
  return [
    ...rowColumns,
    {
      key: 'prior',
      title: priorLabel.value,
      width: 140,
      align: 'right',
      render: (r: ReportAccountRowDto) => h(TMoney, { value: priorByAccount.value.get(r.accountId) ?? 0 }),
    },
    {
      key: 'variance',
      title: t('profitAndLoss.variance'),
      width: 150,
      align: 'right',
      render: (r: ReportAccountRowDto) => renderVariance(r.balance, priorByAccount.value.get(r.accountId) ?? 0),
    },
  ]
})

/**
 * Delta + percent. The percent is omitted against a zero base rather than
 * printed as an infinity, which is the classic comparison-table lie.
 */
function renderVariance(current: number, prior: number) {
  const v = variance(current, prior)
  return h('span', { class: 'fin-reports__variance', 'aria-label': srMoney(v.delta) }, [
    h(TMoney, { value: v.delta, signed: true, tone: 'sign' }),
    v.percent === null ? null : h('span', { class: 'fin-reports__variance-pct' }, formatPercent(v.percent)),
  ])
}

// ── Cash Flow ───────────────────────────────────────────────────
const cf = ref<CashFlowReportDto | null>(null)
const cfLoading = ref(false)

async function runCashFlow() {
  cfLoading.value = true
  try {
    cf.value = await bridge.reports.cashFlow(period.value.from, period.value.to)
  } catch (error) {
    showError(error)
  } finally {
    cfLoading.value = false
  }
}

// ── General Ledger ──────────────────────────────────────────────
const glAccountId = ref<string | null>(null)
const gl = ref<GeneralLedgerReportDto | null>(null)
const glLoading = ref(false)
const accountOptions = ref<Array<{ label: string; value: string }>>([])

const glColumns: DataTableColumns<GeneralLedgerLineDto> = [
  { key: 'postingDate', title: t('columns.postingDate'), width: 120, render: (r) => fmtDate(r.postingDate) },
  { key: 'entryNumber', title: t('generalLedger.entryNumber'), width: 130, render: (r) => r.entryNumber ?? EMPTY_DASH },
  { key: 'memo', title: t('columns.memo'), minWidth: 180, render: (r) => r.memo ?? EMPTY_DASH },
  { key: 'source', title: t('generalLedger.source'), width: 130, render: (r) => financeSourceTypeLabel(r.sourceType) },
  // The account picker offers every postable leaf, so this table renders
  // revenue, equity and liability accounts as readily as bank accounts -
  // "money out" of a revenue account would be nonsense. It is the ledger.
  ...moneyPairColumns<GeneralLedgerLineDto>({
    presentation: 'ledger',
    translate: t,
    debit: (r) => r.debit,
    credit: (r) => r.credit,
  }),
  // Right-aligned with the two amount columns above it: a money block that
  // mixes alignments reads as two unrelated tables glued together.
  { key: 'runningBalance', title: t('generalLedger.balance'), width: 130, align: 'right', render: (r) => amountCell(fmtAmount(r.runningBalance), true) },
]

function flattenLeaves(nodes: AccountTreeDto[], into: Array<{ label: string; value: string }>) {
  for (const node of nodes) {
    if (!node.isGroup) {
      into.push({ label: `${node.code} ${node.name}`, value: node.id })
    }
    flattenLeaves(node.children ?? [], into)
  }
}

async function runGeneralLedger(page: number) {
  if (!glAccountId.value) return
  glLoading.value = true
  try {
    gl.value = await bridge.reports.generalLedger(
      glAccountId.value,
      period.value.from,
      period.value.to,
      page,
      20,
    )
  } catch (error) {
    showError(error)
  } finally {
    glLoading.value = false
  }
}

// ── AR / AP Aging ───────────────────────────────────────────────
const arAging = ref<AgingReportDto | null>(null)
const apAging = ref<AgingReportDto | null>(null)
const arLoading = ref(false)
const apLoading = ref(false)

const agingColumns: DataTableColumns<AgingRowDto> = [
  { key: 'partyName', title: t('aging.party'), minWidth: 160 },
  { key: 'current', title: t('aging.current'), width: 110, render: (r) => amountCell(fmtAmount(r.current)) },
  { key: 'days1To30', title: t('aging.d1to30'), width: 100, render: (r) => amountCell(fmtAmount(r.days1To30)) },
  { key: 'days31To60', title: t('aging.d31to60'), width: 100, render: (r) => amountCell(fmtAmount(r.days31To60)) },
  { key: 'days61To90', title: t('aging.d61to90'), width: 100, render: (r) => amountCell(fmtAmount(r.days61To90)) },
  { key: 'over90', title: t('aging.over90'), width: 100, render: (r) => amountCell(fmtAmount(r.over90)) },
  { key: 'total', title: t('aging.total'), width: 120, render: (r) => amountCell(fmtAmount(r.total), true) },
]

async function runAging(side: 'ar' | 'ap') {
  const loading = side === 'ar' ? arLoading : apLoading
  loading.value = true
  try {
    if (side === 'ar') arAging.value = await bridge.reports.arAging(period.value.to)
    else apAging.value = await bridge.reports.apAging(period.value.to)
  } catch (error) {
    showError(error)
  } finally {
    loading.value = false
  }
}

// ── Tax Summary ─────────────────────────────────────────────────
const tax = ref<TaxSummaryReportDto | null>(null)
const taxLoading = ref(false)

const taxColumns: DataTableColumns<TaxSummaryRowDto> = [
  { key: 'agencyName', title: t('taxSummary.agency'), minWidth: 140, render: (r) => r.agencyName ?? EMPTY_DASH },
  { key: 'rateName', title: t('taxSummary.rateName'), minWidth: 140, render: (r) => r.rateName ?? EMPTY_DASH },
  { key: 'rate', title: t('taxSummary.rate'), width: 90, render: (r) => (r.rate != null ? `${r.rate}%` : EMPTY_DASH) },
  { key: 'outputTax', title: t('taxSummary.outputTax'), width: 130, render: (r) => amountCell(fmtAmount(r.outputTax)) },
  { key: 'inputTax', title: t('taxSummary.inputTax'), width: 130, render: (r) => amountCell(fmtAmount(r.inputTax)) },
  { key: 'netTax', title: t('taxSummary.netTax'), width: 130, render: (r) => amountCell(fmtAmount(r.netTax), true) },
]

async function runTaxSummary() {
  taxLoading.value = true
  try {
    tax.value = await bridge.reports.taxSummary(period.value.from, period.value.to)
  } catch (error) {
    showError(error)
  } finally {
    taxLoading.value = false
  }
}

// ── CSV export (per active tab) ─────────────────────────────────
// 表驱动：每 tab 一条 { ready, run }，就绪条件与导出调用同处声明——
// 新增报表 tab 时漏登记即按钮禁用（可见失败），而非双 switch 各漏一半的静默失败
const exporting = ref(false)

const exporters: Record<string, { ready: () => boolean; run: () => Promise<Blob> }> = {
  'trial-balance': {
    ready: () => true,
    run: () => bridge.reports.exportTrialBalanceCsv(period.value.from, period.value.to),
  },
  'balance-sheet': {
    ready: () => true,
    run: () => bridge.reports.exportBalanceSheetCsv(period.value.to),
  },
  'profit-and-loss': {
    ready: () => true,
    run: () => bridge.reports.exportProfitAndLossCsv(period.value.from, period.value.to),
  },
  'cash-flow': {
    ready: () => true,
    run: () => bridge.reports.exportCashFlowCsv(period.value.from, period.value.to),
  },
  'general-ledger': {
    ready: () => !!glAccountId.value,
    run: () => bridge.reports.exportGeneralLedgerCsv(glAccountId.value!, period.value.from, period.value.to),
  },
  'ar-aging': {
    ready: () => true,
    run: () => bridge.reports.exportArAgingCsv(period.value.to),
  },
  'ap-aging': {
    ready: () => true,
    run: () => bridge.reports.exportApAgingCsv(period.value.to),
  },
  'tax-summary': {
    ready: () => true,
    run: () => bridge.reports.exportTaxSummaryCsv(period.value.from, period.value.to),
  },
}

function canExport(active: string): boolean {
  return exporters[active]?.ready() ?? false
}

async function exportCsv(active: string) {
  const exporter = exporters[active]
  if (!exporter) return
  exporting.value = true
  try {
    const blob = await exporter.run()
    downloadBlob(blob, `${active.replaceAll('-', '_')}_${new Date().toISOString().slice(0, 10)}.csv`)
  } catch (error) {
    showError(error)
  } finally {
    exporting.value = false
  }
}

// ── Balance-summary maintenance (Batch F) ──────────────────────
// verify = read-only diagnosis (finance.balanceSummary.view);
// rebuild = full recompute of the current tenant's summary buckets
// (finance.balanceSummary.execute). The whole dropdown is hidden when the
// user holds neither code (fail-open for super-admin / not-yet-loaded).
const { can } = usePermissionGuard()
const canVerifyBalance = computed(() => can('finance.balanceSummary.view'))
const canRebuildBalance = computed(() => can('finance.balanceSummary.execute'))
const showMaintenance = computed(() => canVerifyBalance.value || canRebuildBalance.value)

const maintenanceOptions = computed(() => {
  const opts: Array<{ label: string; key: string }> = []
  if (canVerifyBalance.value) opts.push({ label: t('maintenance.verify'), key: 'verify' })
  if (canRebuildBalance.value) opts.push({ label: t('maintenance.rebuild'), key: 'rebuild' })
  return opts
})

const maintenanceLoading = ref(false)
const verifyShow = ref(false)
const verifyResult = ref<BalanceSummaryVerifyDto | null>(null)

const dialog = safeDialog()
function safeDialog() {
  try {
    return useDialog()
  } catch {
    return null
  }
}

function formatPeriod(period: number): string {
  const year = Math.floor(period / 100)
  const month = period % 100
  return `${year}-${String(month).padStart(2, '0')}`
}

function diffKindLabel(kind: BalanceSummaryDifferenceKind): string {
  if (kind === BalanceSummaryDifferenceKind.Missing) return t('maintenance.kind.missing')
  if (kind === BalanceSummaryDifferenceKind.Extra) return t('maintenance.kind.extra')
  if (kind === BalanceSummaryDifferenceKind.Mismatch) return t('maintenance.kind.mismatch')
  return String(kind)
}

const diffColumns: DataTableColumns<BalanceSummaryDifferenceDto> = [
  { key: 'kind', title: t('maintenance.diff.kind'), width: 110, render: (r) => diffKindLabel(r.kind) },
  { key: 'accountId', title: t('maintenance.diff.account'), minWidth: 180, ellipsis: { tooltip: true } },
  { key: 'period', title: t('maintenance.diff.period'), width: 100, render: (r) => formatPeriod(r.period) },
  { key: 'currency', title: t('maintenance.diff.currency'), width: 90 },
  { key: 'expected', title: t('maintenance.diff.expected'), minWidth: 170, render: (r) => amountCell(`${t('maintenance.diff.dr')} ${fmtAmount(r.expectedDebit)} / ${t('maintenance.diff.cr')} ${fmtAmount(r.expectedCredit)}`) },
  { key: 'stored', title: t('maintenance.diff.actual'), minWidth: 170, render: (r) => amountCell(`${t('maintenance.diff.dr')} ${fmtAmount(r.storedDebit)} / ${t('maintenance.diff.cr')} ${fmtAmount(r.storedCredit)}`) },
]

function onMaintenanceSelect(key: string) {
  if (key === 'verify') void runVerify()
  else if (key === 'rebuild') confirmRebuild()
}

async function runVerify() {
  if (maintenanceLoading.value) return
  maintenanceLoading.value = true
  try {
    verifyResult.value = await bridge.balanceSummary.verify()
    verifyShow.value = true
  } catch (error) {
    showError(error)
  } finally {
    maintenanceLoading.value = false
  }
}

function confirmRebuild() {
  const run = async () => {
    if (maintenanceLoading.value) return
    maintenanceLoading.value = true
    try {
      const result = await bridge.balanceSummary.rebuild()
      message.success(t('maintenance.rebuildSuccess', {
        buckets: result.buckets,
        lines: result.lines,
        durationMs: result.durationMs,
      }))
    } catch (error) {
      showError(error)
    } finally {
      maintenanceLoading.value = false
    }
  }
  if (dialog) {
    dialog.warning({
      title: t('maintenance.rebuildConfirmTitle'),
      content: t('maintenance.rebuildConfirmContent'),
      positiveText: t('maintenance.rebuildConfirmOk'),
      negativeText: t('maintenance.cancel'),
      positiveButtonProps: { type: 'primary' },
      onPositiveClick: () => void run(),
    })
  } else {
    void run()
  }
}

onMounted(async () => {
  try {
    const tree = await bridge.accounts.tree(true)
    const options: Array<{ label: string; value: string }> = []
    flattenLeaves(tree, options)
    accountOptions.value = options
  } catch {
    accountOptions.value = []
  }
})
</script>

<style scoped>
.fin-reports__drill {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.fin-reports__drill-head {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.fin-reports__drill-search {
  width: 240px;
  max-width: 100%;
}

.fin-reports__drill-period {
  margin-left: auto;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  font-variant-numeric: tabular-nums;
}

.fin-reports__drill-alert {
  margin: 0;
}

.fin-reports__controls {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.fin-reports__variance {
  margin-left: 8px;
  display: inline-flex;
  align-items: baseline;
  gap: 6px;
  justify-content: flex-end;
}

.fin-reports__variance-pct {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  font-variant-numeric: tabular-nums;
}

.fin-reports__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}

.fin-reports__account-select {
  width: 280px;
  max-width: 100%;
}

.fin-reports__totals {
  display: flex;
  gap: 16px;
  margin-top: 12px;
  font-variant-numeric: tabular-nums;
  font-size: 13px;
  flex-wrap: wrap;
}

.fin-reports__totals--top {
  margin-top: 0;
  margin-bottom: 12px;
}

.fin-reports__bs-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.fin-reports__section h4 {
  margin: 0 0 8px;
  font-size: 13px;
  font-weight: 600;
}

.fin-reports__section-total {
  margin: 8px 0 12px;
  font-size: 13px;
  font-variant-numeric: tabular-nums;
}

.fin-reports__check--ok {
  color: var(--tnzi-success, #18a058);
}

.fin-reports__check--bad {
  color: var(--tnzi-error, #d03050);
}

.fin-reports__truncation {
  margin: 12px 0 0;
  font-size: 12px;
  color: var(--tnzi-text-3, #909399);
}

.fin-reports__pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 12px;
}

@media (max-width: 767px) {
  .fin-reports__bs-grid {
    grid-template-columns: 1fr;
  }
}
</style>
