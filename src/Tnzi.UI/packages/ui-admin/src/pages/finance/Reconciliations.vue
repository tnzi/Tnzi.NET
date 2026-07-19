<template>
  <TCrudPage :state="crud" :all-columns="columns" :title="title" :row-actions="rowActions" :translate="t">
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="reconciliationFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :field-renderers="fieldRenderers"
        :translate="t"
      />
    </template>
  </TCrudPage>

  <!-- Worksheet: select cleared lines against the bank statement. -->
  <TDetailHost :state="worksheetDetail" :title="t('worksheet.title')" :width="720" :footer="false" :translate="t">
    <div class="fin-recon__worksheet">
      <div class="fin-recon__totals">
        <span v-if="worksheet?.currency" class="fin-recon__currency">{{ worksheet.currency }}</span>
        <span>{{ t('worksheet.statement') }}: <strong>{{ fmtAmount(worksheet?.statementEndingBalance ?? 0) }}</strong></span>
        <span>{{ t('worksheet.cleared') }}: <strong>{{ fmtAmount(liveCleared) }}</strong></span>
        <span :class="liveDifference === 0 ? 'fin-recon__check--ok' : 'fin-recon__check--bad'">
          {{ t('worksheet.difference') }}: <strong>{{ fmtAmount(liveDifference) }}</strong>
        </span>
      </div>
      <TResponsiveTable
        :columns="lineColumns"
        :data="worksheet?.lines ?? []"
        :row-key="(r: ReconciliationCandidateLineDto) => r.journalLineId"
        :checked-row-keys="selectedIds"
        size="small"
        mobile="scroll"
        :pagination="false"
        :bordered="false"
        @update:checked-row-keys="onChecked"
      />
      <div class="fin-recon__actions">
        <NButton size="small" :disabled="!isDraftOpen || saving" :loading="saving" type="primary" @click="saveLines">
          {{ t('worksheet.save') }}
        </NButton>
        <NButton size="small" type="success" :disabled="!isDraftOpen || liveDifference !== 0 || saving" @click="completeFromWorksheet">
          {{ t('actions.complete') }}
        </NButton>
      </div>
    </div>
  </TDetailHost>
</template>

<script setup lang="ts">
import { computed, h, ref, watch } from 'vue'
import { NButton, type DataTableColumns } from 'naive-ui'
import { formatDateOnly } from '@tnzi/core'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { deleteAction, editAction, type RowAction } from '../../headless/rowActions'
import {
  createFinanceBridge,
  ReconciliationStatus,
  type ReconciliationCandidateLineDto,
  type ReconciliationDto,
  type ReconciliationWorksheetDto,
} from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { createFinanceOptionSources } from './options'
import { amountCell, fmtAmount, tsToIsoDate } from './money'
import { buildReconciliationColumns, reconciliationFormSchema, type ReconciliationRow } from './reconciliation-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.reconciliations')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const columns = buildReconciliationColumns(t)

function toPayload(d: Record<string, unknown>) {
  return {
    accountId: String(d.accountId ?? ''),
    statementDate: typeof d.statementDate === 'number' ? tsToIsoDate(d.statementDate) : String(d.statementDate ?? ''),
    statementEndingBalance: Number(d.statementEndingBalance ?? 0),
    note: (d.note as string | null) || null,
  }
}

const crud = useCrudPage<ReconciliationRow>({
  pageId: 'finance.reconciliations',
  permission: 'finance.reconciliation',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.reconciliations.fetch(q),
  loadDetailById: (id) => bridge.reconciliations.getById(String(id)),
  createData: (d) => bridge.reconciliations.create(toPayload(d)),
  updateData: (id, d) => bridge.reconciliations.update(String(id), toPayload(d)),
  deleteData: async (ids) => {
    for (const id of ids) await bridge.reconciliations.delete(String(id))
  },
})

const title = 'tnzi.admin.modules.finance.reconciliations.title'

const fieldRenderers = {
  'finance-account': selectRenderer(() => sources.leafAccountOptions.value, { placeholder: t('form.accountPlaceholder'), clearable: false }),
}

watch(
  () => crud.formModal.visible.value,
  (open) => {
    if (open) void sources.ensureLeafAccounts()
  },
  { immediate: true },
)

// ── Worksheet drawer ────────────────────────────────────────────
const worksheetDetail = useDetail<ReconciliationDto>({
  mode: 'drawer',
  url: 'worksheet',
  loadData: (id) => bridge.reconciliations.getById(String(id)),
})

const worksheet = ref<ReconciliationWorksheetDto | null>(null)
const selectedIds = ref<string[]>([])
const saving = ref(false)

const isDraftOpen = computed(() => worksheetDetail.data.value?.status === ReconciliationStatus.Draft)

/** Live cleared balance = server-side cleared minus the reconciliation's saved
 *  selection, plus the current (unsaved) selection - keeps the difference bar
 *  honest while the user toggles checkboxes before saving. */
const liveCleared = computed(() => {
  const ws = worksheet.value
  if (!ws) return 0
  const savedSelected = ws.lines.filter((l) => l.isSelected)
  const savedNet = savedSelected.reduce((sum, l) => sum + l.debit - l.credit, 0)
  const selectedSet = new Set(selectedIds.value)
  const currentNet = ws.lines.filter((l) => selectedSet.has(l.journalLineId)).reduce((sum, l) => sum + l.debit - l.credit, 0)
  return ws.clearedBalance - savedNet + currentNet
})

// IEEE 浮点累加会产生 1e-13 级残差：按分位舍入后再比较，
// 否则精确 === 0 的完成门会被永久卡死（服务端 decimal 差额为 0 也点不了）
const liveDifference = computed(() => {
  if (!worksheet.value) return 0
  return Math.round((worksheet.value.statementEndingBalance - liveCleared.value) * 100) / 100
})

watch(
  () => worksheetDetail.data.value?.id,
  async (id) => {
    worksheet.value = null
    selectedIds.value = []
    if (!id) return
    try {
      worksheet.value = await bridge.reconciliations.worksheet(String(id))
      selectedIds.value = worksheet.value.lines.filter((l) => l.isSelected).map((l) => l.journalLineId)
    } catch (error) {
      message.error(error instanceof Error ? error.message : String(error))
    }
  },
)

/** Lines an imported bank transaction is holding — the server refuses to drop them. */
const statementMatchedIds = computed(
  () => new Set((worksheet.value?.lines ?? []).filter((l) => l.isStatementMatched).map((l) => l.journalLineId)),
)

function onChecked(keys: Array<string | number>) {
  if (!isDraftOpen.value) return
  // Keep statement-matched lines selected whatever the table emits: dropping one
  // would orphan the bank transaction that points at its clearing row, so the
  // backend 409s the whole save. Release is unmatch on the bank feed screen.
  selectedIds.value = [...new Set([...keys.map(String), ...statementMatchedIds.value])]
}

const lineColumns: DataTableColumns<ReconciliationCandidateLineDto> = [
  { type: 'selection', disabled: (r) => r.isStatementMatched },
  { key: 'postingDate', title: t('worksheet.date'), width: 110, render: (r) => formatDateOnly(r.postingDate, { utc: true }) },
  { key: 'entryNumber', title: t('worksheet.entry'), width: 120, render: (r) => r.entryNumber ?? '—' },
  {
    key: 'memo',
    title: t('worksheet.memo'),
    minWidth: 160,
    render: (r) =>
      r.isStatementMatched
        ? h('div', { class: 'fin-recon__memo' }, [
            h('span', r.memo ?? '—'),
            h(TStatusBadge, { value: 'statement', type: 'info', label: t('worksheet.statementMatched') }),
          ])
        : (r.memo ?? '—'),
  },
  { key: 'debit', title: t('worksheet.debit'), width: 110, render: (r) => amountCell(r.debit > 0 ? fmtAmount(r.debit) : '—') },
  { key: 'credit', title: t('worksheet.credit'), width: 110, render: (r) => amountCell(r.credit > 0 ? fmtAmount(r.credit) : '—') },
]

async function saveLines() {
  const id = worksheetDetail.data.value?.id
  if (!id) return
  saving.value = true
  try {
    worksheet.value = await bridge.reconciliations.setLines(String(id), selectedIds.value)
    selectedIds.value = worksheet.value.lines.filter((l) => l.isSelected).map((l) => l.journalLineId)
    message.success(t('worksheet.saved'))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    saving.value = false
  }
}

async function completeFromWorksheet() {
  const id = worksheetDetail.data.value?.id
  if (!id) return
  saving.value = true
  try {
    // Persist the current selection first so the server-side gate sees it.
    worksheet.value = await bridge.reconciliations.setLines(String(id), selectedIds.value)
    await bridge.reconciliations.complete(String(id))
    message.success(t('completeSuccess'))
    worksheetDetail.close()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    saving.value = false
  }
}

async function run(action: () => Promise<unknown>, successKey: string) {
  try {
    await action()
    message.success(t(successKey))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const isDraft = (row: ReconciliationRow) => row.status === ReconciliationStatus.Draft

// edit/delete 走内置工厂（权限门 = crud.canUpdate/canDelete）；worksheet/complete 为自定义操作
const rowActions: RowAction<ReconciliationRow>[] = [
  { key: 'worksheet', label: 'actions.worksheet', type: 'primary', onClick: (row) => worksheetDetail.open('view', String(row.id ?? '')) },
  editAction(crud, { show: isDraft }),
  { key: 'complete', label: 'actions.complete', type: 'success', show: (row) => can('finance.reconciliation.update') && isDraft(row), confirm: 'confirmComplete', onClick: (row) => void run(() => bridge.reconciliations.complete(String(row.id ?? '')), 'completeSuccess') },
  deleteAction(crud, { show: isDraft, confirm: 'confirmDelete' }),
]
</script>

<style scoped>
.fin-recon__worksheet {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-recon__memo {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.fin-recon__totals {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
  font-size: 13px;
}

.fin-recon__currency {
  padding: 1px 8px;
  border-radius: 4px;
  font-weight: 600;
  font-size: 12px;
  color: var(--tnzi-primary, #2080f0);
  background: var(--tnzi-primary-suppl, rgba(32, 128, 240, 0.12));
}

.fin-recon__check--ok strong {
  color: var(--tnzi-success, #18a058);
}

.fin-recon__check--bad strong {
  color: var(--tnzi-error, #d03050);
}

.fin-recon__actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}
</style>
