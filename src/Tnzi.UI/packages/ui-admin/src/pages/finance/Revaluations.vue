<template>
  <TCrudPage :state="crud" :all-columns="columns" :title="title" :translate="t">
    <template #primary>
      <NButton v-if="can('finance.revaluation.execute')" type="primary" tertiary size="small" @click="openRun">
        <template #icon>
          <TSvgIcon icon="mdi:calculator-variant-outline" :size="16" />
        </template>
        {{ t('run.title') }}
      </NButton>
    </template>
  </TCrudPage>

  <!-- Run revaluation: pick as-of date, preview per-account increments, then execute. -->
  <TDetailHost :state="runDetail" :title="t('run.title')" :width="900" :footer="false" :translate="t">
    <div class="fin-reval">
      <div class="fin-reval__header">
        <div class="fin-reval__field">
          <span class="fin-reval__label">{{ t('run.asOf') }}</span>
          <NDatePicker v-model:value="asOfTs" type="date" size="small" style="width: 100%" />
        </div>
        <div class="fin-reval__field fin-reval__field--memo">
          <span class="fin-reval__label">{{ t('run.memo') }}</span>
          <NInput v-model:value="memo" size="small" :placeholder="t('run.memoPlaceholder')" />
        </div>
        <div class="fin-reval__field fin-reval__field--action">
          <NButton size="small" :loading="previewing" :disabled="!asOfTs" @click="doPreview">
            <template #icon>
              <TSvgIcon icon="mdi:magnify" :size="16" />
            </template>
            {{ t('run.preview') }}
          </NButton>
        </div>
      </div>

      <p class="fin-reval__hint">{{ t('run.hint') }}</p>

      <template v-if="preview">
        <div v-if="preview.rows.length === 0" class="fin-reval__empty">{{ t('run.noAccounts') }}</div>
        <template v-else>
          <TResponsiveTable
            :columns="previewColumns"
            :data="preview.rows"
            :row-key="(r: RevaluationRowDto) => r.accountId"
            :checked-row-keys="selectedAccountIds"
            size="small"
            mobile="scroll"
            :pagination="false"
            :bordered="false"
            @update:checked-row-keys="onChecked"
          />
          <div class="fin-reval__footer">
            <span class="fin-reval__total">
              {{ t('run.totalAdjustment') }}:
              <strong :class="displayedTotal === 0 ? '' : displayedTotal > 0 ? 'fin-reval__pos' : 'fin-reval__neg'">
                {{ fmtAmount(displayedTotal) }} {{ preview.baseCurrency }}
              </strong>
            </span>
            <div class="fin-reval__actions">
              <NButton size="small" @click="runDetail.close()">{{ t('run.cancel') }}</NButton>
              <NButton size="small" type="primary" :loading="running" :disabled="running || !hasPostable" @click="execute">
                {{ t('run.execute') }}
              </NButton>
            </div>
          </div>
        </template>
      </template>
    </div>
  </TDetailHost>
</template>

<script setup lang="ts">
import { computed, h, ref, watch } from 'vue'
import { NButton, NDatePicker, NInput, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import {
  createFinanceBridge,
  type RevaluationPreviewDto,
  type RevaluationRowDto,
} from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { amountCell, fmtAmount, tsToIsoDate } from './money'
import { buildRevaluationHistoryColumns, type RevaluationHistoryRow } from './revaluation-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.revaluations')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const columns = buildRevaluationHistoryColumns(t)

// Read-only history = the summary vouchers each run posts (sourceType filter).
const crud = useCrudPage<RevaluationHistoryRow>({
  pageId: 'finance.revaluations',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.journals.fetch({ ...q, filters: { ...(q.filters ?? {}), sourceType: 'Revaluation' } }),
})

const title = 'tnzi.admin.modules.finance.revaluations.title'

// ── Run modal ───────────────────────────────────────────────────
const runDetail = useDetail<Record<string, never>>({ mode: 'modal', url: 'run' })

const asOfTs = ref<number | null>(Date.now())
const memo = ref('')
const preview = ref<RevaluationPreviewDto | null>(null)
const selectedAccountIds = ref<string[]>([])
const previewing = ref(false)
const running = ref(false)

// Any postable increment (non-skipped, non-zero) enables Execute — even when the
// net is zero, offsetting per-account revaluations still post.
const hasPostable = computed(() => (preview.value?.rows ?? []).some((r) => !r.skipReason && r.adjustment !== 0))

// The footer total must match what Execute actually posts: the sum over the
// CHECKED accounts (an empty selection means "all", mirroring execute's
// `accountIds ?? null`), not the whole-preview total which would overstate a
// partial selection.
const displayedTotal = computed(() => {
  const p = preview.value
  if (!p) return 0
  if (selectedAccountIds.value.length === 0) return p.totalAdjustment
  const set = new Set(selectedAccountIds.value)
  return p.rows.filter((r) => set.has(r.accountId)).reduce((sum, r) => sum + (r.adjustment ?? 0), 0)
})

function openRun() {
  void runDetail.open('create')
}

watch(
  () => runDetail.visible.value,
  (open) => {
    if (!open) return
    asOfTs.value = Date.now()
    memo.value = ''
    preview.value = null
    selectedAccountIds.value = []
  },
)

function onChecked(keys: Array<string | number>) {
  selectedAccountIds.value = keys.map(String)
}

async function doPreview() {
  if (!asOfTs.value) return
  previewing.value = true
  try {
    preview.value = await bridge.revaluations.preview({ asOf: tsToIsoDate(asOfTs.value) })
    // Pre-select every postable account; the user can narrow the subset.
    selectedAccountIds.value = preview.value.rows.filter((r) => !r.skipReason && r.adjustment !== 0).map((r) => r.accountId)
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    previewing.value = false
  }
}

async function execute() {
  if (running.value || !asOfTs.value) return
  running.value = true
  try {
    const result = await bridge.revaluations.run({
      asOf: tsToIsoDate(asOfTs.value),
      // Empty selection = revalue every eligible account; a subset restricts it.
      accountIds: selectedAccountIds.value.length ? selectedAccountIds.value : null,
      memo: memo.value.trim() || null,
    })
    if (result.journalEntryId) message.success(t('run.success'))
    else message.info(t('run.noAdjustment'))
    runDetail.close()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    running.value = false
  }
}

function rateLabel(rate: number): string {
  return rate.toLocaleString(undefined, { maximumFractionDigits: 8 })
}

const previewColumns: DataTableColumns<RevaluationRowDto> = [
  { type: 'selection', disabled: (row) => !!row.skipReason },
  { key: 'account', title: t('run.account'), minWidth: 200, render: (r) => `${r.code} · ${r.name}` },
  { key: 'currency', title: t('run.currency'), width: 80 },
  { key: 'txnBalance', title: t('run.txnBalance'), width: 120, render: (r) => amountCell(fmtAmount(r.txnBalance)) },
  { key: 'rate', title: t('run.rate'), width: 100, render: (r) => amountCell(rateLabel(r.rate)) },
  { key: 'targetBase', title: t('run.targetBase'), width: 120, render: (r) => amountCell(fmtAmount(r.targetBase)) },
  { key: 'bookBase', title: t('run.bookBase'), width: 120, render: (r) => amountCell(fmtAmount(r.bookBase)) },
  {
    key: 'adjustment',
    title: t('run.adjustment'),
    width: 130,
    render: (r) =>
      r.skipReason
        ? h('span', { class: 'fin-reval__skip' }, r.skipReason)
        : amountCell(fmtAmount(r.adjustment), r.adjustment !== 0),
  },
]
</script>

<style scoped>
.fin-reval {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-reval__header {
  display: grid;
  grid-template-columns: 200px 1fr auto;
  gap: 12px;
  align-items: end;
}

.fin-reval__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.fin-reval__label {
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

.fin-reval__hint {
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

.fin-reval__empty {
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
  font-size: 13px;
  padding: 24px 0;
  text-align: center;
}

.fin-reval__footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-top: 4px;
  flex-wrap: wrap;
}

.fin-reval__total {
  font-size: 13px;
  font-variant-numeric: tabular-nums;
}

.fin-reval__pos strong,
.fin-reval__pos {
  color: var(--tnzi-success, #18a058);
}

.fin-reval__neg {
  color: var(--tnzi-error, #d03050);
}

.fin-reval__skip {
  font-size: 12px;
  color: var(--tnzi-text-tertiary, rgba(0, 0, 0, 0.4));
}

.fin-reval__actions {
  display: flex;
  gap: 8px;
}

@media (max-width: 767px) {
  .fin-reval__header {
    grid-template-columns: 1fr;
  }
}
</style>
