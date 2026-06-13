<template>
  <!--
    Quotas — budget cost dashboard + per-user AI token quota CRUD.

    Top:  a budget dashboard strip fed by `quota.getBudgetSummary()`
          (GET /admin/quotas/budget/summary — defaults to the current month
          when no time range is supplied). KPI cards (spend / limit / usage %
          with a progress bar / status badge) + a per-agent spend breakdown.
    Body: the quota rules table (TCrudPage). Create/edit upsert via
          quota.setQuota (idempotent, keyed on userId). Delete is not
          supported by the bridge — `deleteData` is omitted so the delete
          affordance is hidden automatically.
  -->
  <!--
    Tabbed layout: a normal screen height can't show the per-agent spend
    breakdown table AND the quota-rules table stacked. Two tabs give each list
    the full residual height (t-table-tabs); the rules tab is the default.
    The page-header bar (TContentPage) owns the single title + the user-ID
    search; the tabs sit right below it.
  -->
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <!-- User-ID search in the page-header bar (C2). The quota query endpoint
         filters on `userId` (free-text searchText is ignored by the bridge),
         so the input drives `filters.userId` directly. Rules tab only. -->
    <template #actions>
      <div v-if="activeTab === 'rules'" class="ai-quota-page__search">
        <NInput
          v-model:value="userIdQuery"
          size="small"
          clearable
          :placeholder="t('search.placeholder')"
          class="ai-quota-page__search-input"
          @keydown.enter="onUserIdSearch"
          @clear="onUserIdClear"
        >
          <template #prefix><TSvgIcon icon="mdi:magnify" :size="16" /></template>
        </NInput>
        <NButton size="small" type="primary" @click="onUserIdSearch">
          {{ t('admin.crud.search') }}
        </NButton>
      </div>
    </template>

    <NTabs v-model:value="activeTab" type="line" animated class="t-table-tabs">
      <!-- ── Quota rules ───────────────────────────────────────────── -->
      <NTabPane name="rules" :tab="t('tabs.rules')" display-directive="show">
        <div class="t-table-tabs__pane">
          <!-- show-header=false suppresses the shell's header bar — title +
               search live on the outer TContentPage (otherwise the shell
               falls back to the route meta title and renders a duplicate
               bar). -->
          <TCrudPage
            :state="crud"
            :all-columns="quotaColumns"
            :form-modal-width="640"
            :show-header="false"
            :translate="t"
            :row-actions="rowActions"
          >
            <template #form="{ formData, mode }">
              <TFormSchemaRenderer
                :schema="quotaFormSchema"
                :model="(formData ?? {}) as Record<string, unknown>"
                :readonly="mode === 'view'"
                :translate="t"
                :columns="2"
              />
            </template>
          </TCrudPage>
        </div>
      </NTabPane>

      <!-- ── Budget (KPIs + per-agent spend breakdown) ─────────────── -->
      <NTabPane name="budget" :tab="t('tabs.budget')" display-directive="show">
        <div class="t-table-tabs__pane">
          <div class="ai-quota-budget__kpis">
            <NCard size="small" :bordered="false">
              <NStatistic :label="t('budget.spend')">
                <span class="text-success font-600">${{ formatUsd(budget.currentSpendUsd) }}</span>
              </NStatistic>
            </NCard>
            <NCard size="small" :bordered="false">
              <NStatistic :label="t('budget.limit')">
                <span class="font-600">{{ budget.budgetLimitUsd > 0 ? `$${formatUsd(budget.budgetLimitUsd)}` : t('budget.noLimit') }}</span>
              </NStatistic>
            </NCard>
            <NCard size="small" :bordered="false">
              <NStatistic :label="t('budget.usage')">
                <div class="flex flex-col gap-6px">
                  <span class="font-600">{{ formatPercent(budget.usagePercentage) }}</span>
                  <NProgress
                    type="line"
                    :percentage="usagePercent"
                    :status="progressStatus"
                    :show-indicator="false"
                    :height="6"
                  />
                </div>
              </NStatistic>
            </NCard>
            <NCard size="small" :bordered="false">
              <NStatistic :label="t('budget.status')">
                <NTag size="small" :type="statusTagType" :bordered="false">
                  {{ t(`budget.statusValue.${budget.status}`) }}
                </NTag>
              </NStatistic>
            </NCard>
          </div>

          <!-- Per-agent spend breakdown — fills the residual height + scrolls. -->
          <NCard size="small" :bordered="false" class="ai-quota-budget__breakdown t-table-card" :title="t('budget.byAgent')">
            <template #header-extra>
              <NButton size="small" tertiary :loading="budgetLoading" @click="refreshBudget">
                <template #icon><TSvgIcon icon="mdi:refresh" :size="16" /></template>
                {{ t('budget.refresh') }}
              </NButton>
            </template>
            <TResponsiveTable
              :columns="agentSpendColumns"
              :data="budget.byAgent ?? []"
              :loading="budgetLoading"
              :row-key="(r: AgentSpendDto) => r.agentId ?? r.agentName"
              :pagination="false"
              :flex-height="true"
              size="small"
              :empty-text="t('budget.noSpend')"
            />
          </NCard>
        </div>
      </NTabPane>
    </NTabs>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import { NButton, NCard, NInput, NProgress, NStatistic, NTabPane, NTabs, NTag, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TContentPage from '../../../components/layout/TContentPage.vue'
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { editAction, type RowAction } from '../../../headless/rowActions'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { translatePageKey } from '../../_shared/translate'
import { quotaColumns, quotaFormSchema } from './quota-config'
import type {
  UserQuotaDto,
  SetQuotaDto,
  BudgetSummaryDto,
  AgentSpendDto,
} from '@tnzi/core/services/ai'

const t = (key: string) => translatePageKey('ai.quota', key)

const bridge = createAiBridge({ client: useAdminClient() })

const activeTab = ref<'rules' | 'budget'>('rules')

const crud = useCrudPage<UserQuotaDto>({
  pageId: 'ai.quota',
  columns: quotaColumns,
  rowKey: (q) => q.id,
  fetchData: (query) => bridge.quota.fetch(query),
  createData: (data) => bridge.quota.create(data as SetQuotaDto),
  updateData: (_id, data) => bridge.quota.update(String(_id), data as SetQuotaDto),
  // No deleteData — quota.delete is not supported by the bridge (the delete
  // affordance is hidden automatically when deleteData is omitted).
})

// Edit only — quotas are upserted by userId; there is no per-row delete.
const rowActions: RowAction<UserQuotaDto>[] = [editAction(crud)]

// Header user-ID search — drives the query's `filters.userId` (the backend
// quota query has no free-text keyword; userId is the supported filter).
const userIdQuery = ref('')
function onUserIdSearch(): void {
  crud.setFilters({ userId: userIdQuery.value.trim() || undefined })
  void crud.refresh()
}
function onUserIdClear(): void {
  userIdQuery.value = ''
  crud.setFilters({ userId: undefined })
  void crud.refresh()
}

// --- budget dashboard -------------------------------------------------------
const emptyBudget: BudgetSummaryDto = {
  periodStart: '',
  periodEnd: '',
  currentSpendUsd: 0,
  budgetLimitUsd: 0,
  usagePercentage: 0,
  status: 0,
  byAgent: [],
}

const budget = ref<BudgetSummaryDto>({ ...emptyBudget })
const budgetLoading = ref(false)

const usagePercent = computed(() => {
  const pct = Math.round((budget.value.usagePercentage ?? 0) * 1000) / 10
  return Math.max(0, Math.min(100, pct))
})

const progressStatus = computed<'success' | 'warning' | 'error'>(() =>
  budget.value.status === 2 ? 'error' : budget.value.status === 1 ? 'warning' : 'success',
)

const statusTagType = computed<'success' | 'warning' | 'error'>(() => progressStatus.value)

function formatUsd(value: number | null | undefined): string {
  const n = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(n)) return '0.00'
  return n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function formatPercent(ratio: number | null | undefined): string {
  const n = typeof ratio === 'number' ? ratio : Number(ratio)
  if (!Number.isFinite(n)) return '—'
  return `${Math.round(n * 1000) / 10}%`
}

// Per-agent spend breakdown columns (share fee + count + % of total).
const agentSpendColumns = computed<DataTableColumns<AgentSpendDto>>(() => {
  const total = budget.value.byAgent.reduce((sum, a) => sum + (a.spendUsd ?? 0), 0)
  return [
    {
      key: 'agentName',
      title: t('budget.agentName'),
      render: (row) => row.agentName || row.agentId || '—',
    },
    {
      key: 'spendUsd',
      title: t('budget.agentSpend'),
      align: 'right',
      render: (row) => `$${formatUsd(row.spendUsd)}`,
    },
    {
      key: 'requestCount',
      title: t('budget.requestCount'),
      align: 'right',
      render: (row) => (row.requestCount ?? 0).toLocaleString('en-US'),
    },
    {
      key: 'share',
      title: t('budget.share'),
      align: 'right',
      render: (row) =>
        total > 0
          ? h(
              NTag,
              { size: 'small', bordered: false },
              { default: () => `${Math.round(((row.spendUsd ?? 0) / total) * 1000) / 10}%` },
            )
          : '—',
    },
  ]
})

async function refreshBudget(): Promise<void> {
  budgetLoading.value = true
  try {
    // No time range → backend defaults to the current calendar month.
    const result = await bridge.quota.getBudgetSummary()
    budget.value = result ? { ...emptyBudget, ...result, byAgent: result.byAgent ?? [] } : { ...emptyBudget }
  } catch {
    // Budget fetch failure must not block the quota table.
    budget.value = { ...emptyBudget }
  } finally {
    budgetLoading.value = false
  }
}

onMounted(() => {
  crud.refresh().catch(() => undefined)
  refreshBudget().catch(() => undefined)
})
</script>

<style scoped>
/* Page-header user-ID search (rules tab only). */
.ai-quota-page__search {
  display: flex;
  align-items: center;
  gap: 8px;
}
.ai-quota-page__search-input {
  width: 240px;
  max-width: 100%;
}
@media (max-width: 640px) {
  .ai-quota-page__search { flex-wrap: wrap; }
  .ai-quota-page__search-input { width: 100%; }
}
/* KPI strip: responsive grid (4 → 2 → 1) so it stays readable at 375px. Sits
   above the breakdown table inside the Budget tab pane — fixed height, the
   table below claims the rest. */
.ai-quota-budget__kpis {
  flex-shrink: 0;
  margin-bottom: 12px;
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}
@media (max-width: 1023px) {
  .ai-quota-budget__kpis {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
@media (max-width: 575px) {
  .ai-quota-budget__kpis {
    grid-template-columns: 1fr;
  }
}
</style>
