<template>
  <TCrudPage :state="crud" :all-columns="fiscalYearColumns" :title="title" :row-actions="rowActions" :translate="t">
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
import TCrudPage from '../../components/crud/TCrudPage.vue'
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
  // The backend returns the full (small) list — wrap it as a single page.
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
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

async function reopenYear(row: FiscalYearRow) {
  try {
    await bridge.fiscalYears.reopen(String(row.id ?? ''))
    message.success(t('reopenSuccess'))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const rowActions: RowAction<FiscalYearRow>[] = [
  { key: 'close', label: 'actions.close', type: 'warning', show: (row) => can('finance.fiscalYear.update') && !row.isClosed, confirm: 'confirmClose', onClick: closeYear },
  { key: 'reopen', label: 'actions.reopen', show: (row) => can('finance.fiscalYear.update') && row.isClosed === true, confirm: 'confirmReopen', onClick: reopenYear },
  deleteAction(crud),
]
</script>
