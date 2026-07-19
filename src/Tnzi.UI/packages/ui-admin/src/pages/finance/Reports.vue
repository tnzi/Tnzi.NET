<template>
  <TTabsPage :title="title" icon="mdi:chart-box-outline" :translate="t" :sections="tabs" default-section="trial-balance">
      <!-- Per-tab CSV export (server-generated, UTF-8 BOM) + balance-summary maintenance -->
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
          <NDatePicker v-model:value="tbRange" type="daterange" size="small" clearable />
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
        <TEmpty v-else-if="!tbLoading" :description="t('runHint')" />
      </template>

      <!-- ── Balance Sheet ─────────────────────────────────── -->
      <template #balance-sheet>
        <div class="fin-reports__toolbar">
          <NDatePicker v-model:value="bsAsOf" type="date" size="small" />
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
        <TEmpty v-else-if="!bsLoading" :description="t('runHint')" />
      </template>

      <!-- ── Profit & Loss ─────────────────────────────────── -->
      <template #profit-and-loss>
        <div class="fin-reports__toolbar">
          <NDatePicker v-model:value="plRange" type="daterange" size="small" clearable />
          <NButton size="small" type="primary" :loading="plLoading" @click="runProfitAndLoss">{{ t('actions.run') }}</NButton>
        </div>
        <template v-if="pl">
          <div class="fin-reports__bs-grid">
            <div class="fin-reports__section">
              <h4>{{ t('profitAndLoss.income') }}</h4>
              <TResponsiveTable :columns="rowColumns" :data="pl.income" size="small" mobile="scroll" :pagination="false" :bordered="false" />
              <div class="fin-reports__section-total">{{ t('profitAndLoss.totalIncome') }}: <strong>{{ fmtAmount(pl.totalIncome) }}</strong></div>
            </div>
            <div class="fin-reports__section">
              <h4>{{ t('profitAndLoss.expenses') }}</h4>
              <TResponsiveTable :columns="rowColumns" :data="pl.expenses" size="small" mobile="scroll" :pagination="false" :bordered="false" />
              <div class="fin-reports__section-total">{{ t('profitAndLoss.totalExpenses') }}: <strong>{{ fmtAmount(pl.totalExpenses) }}</strong></div>
            </div>
          </div>
          <div class="fin-reports__totals">
            <span>{{ t('profitAndLoss.netProfit') }}: <strong :class="pl.netProfit >= 0 ? 'fin-reports__check--ok' : 'fin-reports__check--bad'">{{ fmtAmount(pl.netProfit) }}</strong></span>
          </div>
        </template>
        <TEmpty v-else-if="!plLoading" :description="t('runHint')" />
      </template>

      <!-- ── Cash Flow (indirect method) ───────────────────── -->
      <template #cash-flow>
        <div class="fin-reports__toolbar">
          <NDatePicker v-model:value="cfRange" type="daterange" size="small" clearable />
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
        <TEmpty v-else-if="!cfLoading" :description="t('runHint')" />
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
          <NDatePicker v-model:value="glRange" type="daterange" size="small" clearable />
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
        <TEmpty v-else-if="!glLoading" :description="t('runHint')" />
      </template>

      <!-- ── AR Aging ──────────────────────────────────────── -->
      <template #ar-aging>
        <div class="fin-reports__toolbar">
          <NDatePicker v-model:value="arAsOf" type="date" size="small" />
          <NButton size="small" type="primary" :loading="arLoading" @click="runAging('ar')">{{ t('actions.run') }}</NButton>
        </div>
        <template v-if="arAging">
          <TResponsiveTable :columns="agingColumns" :data="arAging.rows" size="small" mobile="scroll" :pagination="false" :bordered="false" />
          <div class="fin-reports__totals">
            <span>{{ t('aging.total') }}: <strong>{{ fmtAmount(arAging.totals.total) }}</strong></span>
            <span>{{ t('aging.over90') }}: <strong>{{ fmtAmount(arAging.totals.over90) }}</strong></span>
          </div>
        </template>
        <TEmpty v-else-if="!arLoading" :description="t('runHint')" />
      </template>

      <!-- ── AP Aging ──────────────────────────────────────── -->
      <template #ap-aging>
        <div class="fin-reports__toolbar">
          <NDatePicker v-model:value="apAsOf" type="date" size="small" />
          <NButton size="small" type="primary" :loading="apLoading" @click="runAging('ap')">{{ t('actions.run') }}</NButton>
        </div>
        <template v-if="apAging">
          <TResponsiveTable :columns="agingColumns" :data="apAging.rows" size="small" mobile="scroll" :pagination="false" :bordered="false" />
          <div class="fin-reports__totals">
            <span>{{ t('aging.total') }}: <strong>{{ fmtAmount(apAging.totals.total) }}</strong></span>
            <span>{{ t('aging.over90') }}: <strong>{{ fmtAmount(apAging.totals.over90) }}</strong></span>
          </div>
        </template>
        <TEmpty v-else-if="!apLoading" :description="t('runHint')" />
      </template>

      <!-- ── Tax Summary ───────────────────────────────────── -->
      <template #tax-summary>
        <div class="fin-reports__toolbar">
          <NDatePicker v-model:value="taxRange" type="daterange" size="small" clearable />
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
        <TEmpty v-else-if="!taxLoading" :description="t('runHint')" />
      </template>

      <!-- ── Balance-summary verify result (tab-agnostic overlay) ── -->
      <template #overlays>
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
            <TEmpty v-else-if="verifyResult.isConsistent" :description="t('maintenance.consistentHint')" />
          </template>
        </TModalShell>
      </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { NButton, NDatePicker, NDropdown, NPagination, NSelect, useDialog, type DataTableColumns } from 'naive-ui'
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
import { useAdminClient } from '../../plugin/client'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { downloadBlob, formatDateOnly } from '@tnzi/core'
import { amountCell, fmtAmount, tsToIsoDate } from './money'
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

const yearStart = new Date(new Date().getFullYear(), 0, 1).getTime()
const today = Date.now()

function showError(error: unknown) {
  message.error(error instanceof Error ? error.message : String(error))
}

// ── Trial Balance ───────────────────────────────────────────────
const tbRange = ref<[number, number]>([yearStart, today])
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
  if (!tbRange.value) return
  tbLoading.value = true
  try {
    tb.value = await bridge.reports.trialBalance(tsToIsoDate(tbRange.value[0]), tsToIsoDate(tbRange.value[1]))
  } catch (error) {
    showError(error)
  } finally {
    tbLoading.value = false
  }
}

// ── Balance Sheet ───────────────────────────────────────────────
const bsAsOf = ref<number>(today)
const bs = ref<BalanceSheetReportDto | null>(null)
const bsLoading = ref(false)

const rowColumns: DataTableColumns<ReportAccountRowDto> = [
  { key: 'code', title: t('columns.code'), width: 100 },
  { key: 'name', title: t('columns.name'), minWidth: 160 },
  { key: 'balance', title: t('columns.balance'), width: 140, render: (r) => amountCell(fmtAmount(r.balance), true) },
]

async function runBalanceSheet() {
  bsLoading.value = true
  try {
    bs.value = await bridge.reports.balanceSheet(tsToIsoDate(bsAsOf.value))
  } catch (error) {
    showError(error)
  } finally {
    bsLoading.value = false
  }
}

// ── Profit & Loss ───────────────────────────────────────────────
const plRange = ref<[number, number]>([yearStart, today])
const pl = ref<ProfitAndLossReportDto | null>(null)
const plLoading = ref(false)

async function runProfitAndLoss() {
  if (!plRange.value) return
  plLoading.value = true
  try {
    pl.value = await bridge.reports.profitAndLoss(tsToIsoDate(plRange.value[0]), tsToIsoDate(plRange.value[1]))
  } catch (error) {
    showError(error)
  } finally {
    plLoading.value = false
  }
}

// ── Cash Flow ───────────────────────────────────────────────────
const cfRange = ref<[number, number]>([yearStart, today])
const cf = ref<CashFlowReportDto | null>(null)
const cfLoading = ref(false)

async function runCashFlow() {
  if (!cfRange.value) return
  cfLoading.value = true
  try {
    cf.value = await bridge.reports.cashFlow(tsToIsoDate(cfRange.value[0]), tsToIsoDate(cfRange.value[1]))
  } catch (error) {
    showError(error)
  } finally {
    cfLoading.value = false
  }
}

// ── General Ledger ──────────────────────────────────────────────
const glAccountId = ref<string | null>(null)
const glRange = ref<[number, number]>([yearStart, today])
const gl = ref<GeneralLedgerReportDto | null>(null)
const glLoading = ref(false)
const accountOptions = ref<Array<{ label: string; value: string }>>([])

const glColumns: DataTableColumns<GeneralLedgerLineDto> = [
  { key: 'postingDate', title: t('columns.postingDate'), width: 120, render: (r) => formatDateOnly(r.postingDate, { utc: true }) },
  { key: 'entryNumber', title: t('generalLedger.entryNumber'), width: 130, render: (r) => r.entryNumber ?? '—' },
  { key: 'memo', title: t('columns.memo'), minWidth: 180, render: (r) => r.memo ?? '—' },
  { key: 'source', title: t('generalLedger.source'), width: 130, render: (r) => financeSourceTypeLabel(r.sourceType) },
  { key: 'debit', title: t('generalLedger.debit'), width: 120, render: (r) => amountCell(r.debit > 0 ? fmtAmount(r.debit) : '—') },
  { key: 'credit', title: t('generalLedger.credit'), width: 120, render: (r) => amountCell(r.credit > 0 ? fmtAmount(r.credit) : '—') },
  { key: 'runningBalance', title: t('generalLedger.balance'), width: 130, render: (r) => amountCell(fmtAmount(r.runningBalance), true) },
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
  if (!glAccountId.value || !glRange.value) return
  glLoading.value = true
  try {
    gl.value = await bridge.reports.generalLedger(
      glAccountId.value,
      tsToIsoDate(glRange.value[0]),
      tsToIsoDate(glRange.value[1]),
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
const arAsOf = ref<number>(today)
const apAsOf = ref<number>(today)
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
    if (side === 'ar') arAging.value = await bridge.reports.arAging(tsToIsoDate(arAsOf.value))
    else apAging.value = await bridge.reports.apAging(tsToIsoDate(apAsOf.value))
  } catch (error) {
    showError(error)
  } finally {
    loading.value = false
  }
}

// ── Tax Summary ─────────────────────────────────────────────────
const taxRange = ref<[number, number]>([yearStart, today])
const tax = ref<TaxSummaryReportDto | null>(null)
const taxLoading = ref(false)

const taxColumns: DataTableColumns<TaxSummaryRowDto> = [
  { key: 'agencyName', title: t('taxSummary.agency'), minWidth: 140, render: (r) => r.agencyName ?? '—' },
  { key: 'rateName', title: t('taxSummary.rateName'), minWidth: 140, render: (r) => r.rateName ?? '—' },
  { key: 'rate', title: t('taxSummary.rate'), width: 90, render: (r) => (r.rate != null ? `${r.rate}%` : '—') },
  { key: 'outputTax', title: t('taxSummary.outputTax'), width: 130, render: (r) => amountCell(fmtAmount(r.outputTax)) },
  { key: 'inputTax', title: t('taxSummary.inputTax'), width: 130, render: (r) => amountCell(fmtAmount(r.inputTax)) },
  { key: 'netTax', title: t('taxSummary.netTax'), width: 130, render: (r) => amountCell(fmtAmount(r.netTax), true) },
]

async function runTaxSummary() {
  if (!taxRange.value) return
  taxLoading.value = true
  try {
    tax.value = await bridge.reports.taxSummary(tsToIsoDate(taxRange.value[0]), tsToIsoDate(taxRange.value[1]))
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
    ready: () => !!tbRange.value,
    run: () => bridge.reports.exportTrialBalanceCsv(tsToIsoDate(tbRange.value[0]), tsToIsoDate(tbRange.value[1])),
  },
  'balance-sheet': {
    ready: () => true,
    run: () => bridge.reports.exportBalanceSheetCsv(tsToIsoDate(bsAsOf.value)),
  },
  'profit-and-loss': {
    ready: () => !!plRange.value,
    run: () => bridge.reports.exportProfitAndLossCsv(tsToIsoDate(plRange.value[0]), tsToIsoDate(plRange.value[1])),
  },
  'cash-flow': {
    ready: () => !!cfRange.value,
    run: () => bridge.reports.exportCashFlowCsv(tsToIsoDate(cfRange.value[0]), tsToIsoDate(cfRange.value[1])),
  },
  'general-ledger': {
    ready: () => !!glAccountId.value && !!glRange.value,
    run: () => bridge.reports.exportGeneralLedgerCsv(glAccountId.value!, tsToIsoDate(glRange.value[0]), tsToIsoDate(glRange.value[1])),
  },
  'ar-aging': {
    ready: () => true,
    run: () => bridge.reports.exportArAgingCsv(tsToIsoDate(arAsOf.value)),
  },
  'ap-aging': {
    ready: () => true,
    run: () => bridge.reports.exportApAgingCsv(tsToIsoDate(apAsOf.value)),
  },
  'tax-summary': {
    ready: () => !!taxRange.value,
    run: () => bridge.reports.exportTaxSummaryCsv(tsToIsoDate(taxRange.value[0]), tsToIsoDate(taxRange.value[1])),
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
