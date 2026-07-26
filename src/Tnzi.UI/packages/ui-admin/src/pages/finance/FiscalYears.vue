<template>
  <TCrudPage :state="crud" :all-columns="fiscalYearColumns" :title="title" :row-actions="rowActions" :translate="t">
    <!-- The rolling closing date sits above the year list: same operator
         question ("can this period still be touched?"), two orthogonal locks. -->
    <template #kpis>
      <ClosingDatePanel ref="closingPanel" :bridge="bridge" :can-edit="canEditLock" :t="t" @changed="crud.refresh()" />
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="fiscalYearFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import ClosingDatePanel from './components/ClosingDatePanel.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { deleteAction, type RowAction } from '../../headless/rowActions'
import { createFinanceBridge } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { pagedResult } from '../../services/_mappers'
import { buildFiscalYearColumns, fiscalYearFormSchema, type FiscalYearRow } from './fiscal-year-config'
import { tsToIsoDate } from './money'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.fiscalYears')
const message = useSafeMessage()
const { can } = usePermissionGuard()

// Changing the closing date is authorized separately from opening/closing a
// fiscal year: it decides whether already-filed figures can still move.
const canEditLock = computed(() => can('finance.ledgerLock.update'))
const closingPanel = ref<{ reload: () => Promise<void> } | null>(null)

const fiscalYearColumns = buildFiscalYearColumns(t)

function toPayload(d: Record<string, unknown>) {
  const toIso = (v: unknown) => (typeof v === 'number' ? tsToIsoDate(v) : String(v ?? ''))
  return {
    name: String(d.name ?? '').trim(),
    startDate: toIso(d.startDate),
    endDate: toIso(d.endDate),
  }
}

const crud = useCrudPage<FiscalYearRow>({
  pageId: 'finance.fiscalYears',
  permission: 'finance.fiscalYear',
  columns: fiscalYearColumns,
  rowKey: (r) => String(r.id ?? ''),
  // The backend returns the full (small) list - wrap it as a single page.
  fetchData: async (q) => {
    const items = await bridge.fiscalYears.list()
    return pagedResult({ items, totalCount: items.length, pageIndex: q.pageIndex, pageSize: Math.max(q.pageSize, items.length || 1) })
  },
  createData: (d) => bridge.fiscalYears.create(toPayload(d)),
  deleteData: (ids) => bridge.fiscalYears.delete(ids.map(String)),
})

const title = 'tnzi.admin.modules.finance.fiscalYears.title'

async function closeYear(row: FiscalYearRow) {
  try {
    await bridge.fiscalYears.close(String(row.id ?? ''))
    message.success(t('closeSuccess'))
    await Promise.all([crud.refresh(), closingPanel.value?.reload()])
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

async function reopenYear(row: FiscalYearRow) {
  try {
    await bridge.fiscalYears.reopen(String(row.id ?? ''))
    message.success(t('reopenSuccess'))
    await Promise.all([crud.refresh(), closingPanel.value?.reload()])
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const rowActions: RowAction<FiscalYearRow>[] = [
  { key: 'close', label: 'actions.close', type: 'warning', show: (row) => can('finance.fiscalYear.update') && !row.isClosed, confirm: 'confirmClose', onClick: closeYear },
  { key: 'reopen', label: 'actions.reopen', show: (row) => can('finance.fiscalYear.update') && row.isClosed === true, confirm: 'confirmReopen', onClick: reopenYear },
  // 已关闭的年度不提供删除:后端会 409 拒绝(删掉它等于把期间锁悄悄拆了),
  // 摆一个必然失败的按钮只会让人以为是系统出错。解锁的路径是上面的 Reopen。
  deleteAction(crud, { show: (row) => row.isClosed !== true }),
]
</script>
