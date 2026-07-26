<template>
  <!--
    Invoices - list view backed by /admin/invoices (TListShell + TTableRenderer,
    useCrudPage fetch-only). Read-mostly: list + lifecycle actions
    (send / mark-paid / cancel). Mark-paid and cancel route through
    useDetail(modal) + TDetailHost (§5.5.2 - no hand-rolled NModal); send is a
    direct call gated by a RowAction confirm. Manual invoice creation lives
    outside this page (line-items editing is rich-form territory).

    `InvoiceQueryDto` has no free-text field server-side, so the default keyword
    box is disabled; `status` is exposed as a toolbar filter.
  -->
  <TListShell
    :state="crud"
    :title="t('title')"
    :show-batch="false"
    :show-default-search="false"
    :translate="t"
  >
    <template #toolbarLeft>
      <NSelect
        v-model:value="statusFilter"
        :options="statusOptions"
        :placeholder="t('filter.status')"
        clearable
        size="small"
        class="w-180px"
        @update:value="onStatusChange"
      />
    </template>
    <template #renderer>
      <TTableRenderer :state="crud" :show-selection="false" :row-actions="rowActions" :translate="t" />
    </template>
  </TListShell>

  <!-- Mark-paid overlay - paidAmount defaults to the outstanding due amount. -->
  <TDetailHost :state="markPaidDetail" :title="t('modal.markPaid')" :width="480" :translate="t">
    <template #default>
      <NForm label-placement="top" :show-feedback="false">
        <NFormItem :label="t('form.paidAmount')" required>
          <NInputNumber v-model:value="markPaidForm.paidAmount" :min="0.01" :precision="2" class="w-full" />
        </NFormItem>
        <NFormItem :label="t('form.remark')">
          <NInput v-model:value="markPaidForm.remark" type="textarea" :rows="2" :placeholder="t('form.remarkPlaceholder')" />
        </NFormItem>
      </NForm>
    </template>
    <template #footer="{ close }">
      <NButton @click="close">{{ t('actions.cancel') }}</NButton>
      <NButton
        type="primary"
        :loading="actionLoading"
        :disabled="!markPaidForm.paidAmount || markPaidForm.paidAmount <= 0"
        @click="confirmMarkPaid"
      >
        {{ t('actions.confirm') }}
      </NButton>
    </template>
  </TDetailHost>

  <!-- Cancel overlay - reason required. -->
  <TDetailHost :state="cancelDetail" :title="t('modal.cancel')" :width="460" :translate="t">
    <template #default>
      <NForm label-placement="top" :show-feedback="false">
        <NFormItem :label="t('form.cancelReason')" required>
          <NInput v-model:value="cancelReason" type="textarea" :rows="3" />
        </NFormItem>
      </NForm>
    </template>
    <template #footer="{ close }">
      <NButton @click="close">{{ t('actions.cancel') }}</NButton>
      <NButton type="error" :loading="actionLoading" :disabled="!cancelReason" @click="confirmCancel">
        {{ t('actions.confirmCancel') }}
      </NButton>
    </template>
  </TDetailHost>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { h, reactive, ref, watch } from 'vue'
import { NButton, NForm, NFormItem, NInput, NInputNumber, NSelect, useMessage } from 'naive-ui'
import { formatCurrency, formatDateOnly as formatDate } from '@tnzi/core'
import { useAdminClient } from '../../plugin/client'
import {
  createInvoiceBridge,
  type InvoiceDto,
  type MarkInvoicePaidDto,
} from '../../services/bridges/invoice-bridge'
import { makePageTranslator } from '../_shared/translate'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { type RowAction } from '../../headless/rowActions'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { StatusType } from '@tnzi/ui'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TListShell from '../../components/crud/TListShell.vue'
import TTableRenderer from '../../components/crud/renderers/TTableRenderer.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'

const bridge = createInvoiceBridge({ client: useAdminClient() })
const message = useMessage()
const t = makePageTranslator('payment.invoices')
const { can } = usePermissionGuard()

const actionLoading = ref(false)

// ─── Status filter (drives crud.setFilters) ──────────────────────────
// InvoiceStatus serialises as its member name (global JsonStringEnumConverter):
// Draft / Pending / Sent / Paid / Overdue / Cancelled.
const statusFilter = ref<string | null>(null)
const statusOptions = [
  { value: 'Draft', label: t('status.draft') },
  { value: 'Pending', label: t('status.pending') },
  { value: 'Sent', label: t('status.sent') },
  { value: 'Paid', label: t('status.paid') },
  { value: 'Overdue', label: t('status.overdue') },
  { value: 'Cancelled', label: t('status.cancelled') },
]
function onStatusChange(): void {
  crud.setFilters({ status: statusFilter.value })
  void crud.refresh()
}

// ─── Status badge mapping (tone + i18n label key per InvoiceStatus member) ─
const INVOICE_STATUS_MAP: Record<string, { type: StatusType; labelKey: string }> = {
  Draft: { type: 'default', labelKey: 'status.draft' },
  Pending: { type: 'default', labelKey: 'status.pending' },
  Sent: { type: 'info', labelKey: 'status.sent' },
  Paid: { type: 'success', labelKey: 'status.paid' },
  Overdue: { type: 'error', labelKey: 'status.overdue' },
  Cancelled: { type: 'default', labelKey: 'status.cancelled' },
}

// Terminal states - mark-paid / cancel no longer apply.
const TERMINAL = new Set(['Paid', 'Cancelled'])
// Sendable states - draft / pending / sent may (re)send the invoice.
const SENDABLE = new Set(['Draft', 'Pending', 'Sent'])

function money(n: number, currency: string): string {
  return formatCurrency(Number(n ?? 0), String(currency || 'USD'))
}

// ─── Column definitions (ColumnDef[]) ────────────────────────────────
const tableColumns: ColumnDef[] = [
  {
    key: 'status',
    title: t('cols.status'),
    width: 120,
    render: (row) => {
      const r = row as unknown as InvoiceDto
      const v = String(r.status ?? '')
      const meta = INVOICE_STATUS_MAP[v]
      return h(TStatusBadge, {
        value: v,
        type: meta?.type ?? 'default',
        label: meta ? t(meta.labelKey) : v || EMPTY_DASH,
      })
    },
  },
  {
    key: 'invoiceNo',
    title: t('cols.invoiceNo'),
    minWidth: 140,
    render: (row) => {
      const r = row as unknown as InvoiceDto
      return h('code', { class: 'tnzi-mono text-12px' }, r.invoiceNo)
    },
  },
  { key: 'customerName', title: t('cols.customer'), minWidth: 140, ellipsis: { tooltip: true } },
  { key: 'customerEmail', title: t('cols.customerEmail'), minWidth: 170, ellipsis: { tooltip: true } },
  {
    key: 'amount',
    title: t('cols.amount'),
    width: 130,
    align: 'right',
    render: (row) => {
      const r = row as unknown as InvoiceDto
      return money(r.amount, r.currency)
    },
  },
  {
    key: 'paidAmount',
    title: t('cols.paidAmount'),
    width: 130,
    align: 'right',
    render: (row) => {
      const r = row as unknown as InvoiceDto
      return money(r.paidAmount, r.currency)
    },
  },
  {
    key: 'invoiceDate',
    title: t('cols.invoiceDate'),
    width: 130,
    render: (row) => formatDate((row as unknown as InvoiceDto).invoiceDate),
  },
  {
    key: 'dueDate',
    title: t('cols.dueDate'),
    width: 130,
    render: (row) => formatDate((row as unknown as InvoiceDto).dueDate),
  },
]

// ─── Fetch-only useCrudPage (no create/update/delete) ────────────────
const crud = useCrudPage<InvoiceDto>({
  pageId: 'payment.invoices',
  columns: tableColumns,
  rowKey: (r) => r.id,
  fetchData: async (q) => {
    const status = (q.filters.status as string | null | undefined) ?? null
    const r = await bridge.getList({
      pageIndex: q.pageIndex,
      pageSize: q.pageSize,
      status,
    })
    const pageSize = q.pageSize
    return {
      items: r.items,
      totalCount: r.totalCount,
      pageIndex: q.pageIndex,
      pageSize,
      totalPages: Math.max(1, Math.ceil(r.totalCount / pageSize)),
      hasPreviousPage: q.pageIndex > 1,
      hasNextPage: q.pageIndex * pageSize < r.totalCount,
    }
  },
})

// ─── Mark-paid overlay (useDetail modal) ─────────────────────────────
const markPaidDetail = useDetail<InvoiceDto>({ mode: 'modal', url: 'mark-paid', source: crud })
const markPaidForm = reactive<MarkInvoicePaidDto>({ paidAmount: 0, remark: '' })
watch(() => markPaidDetail.data.value, (inv) => {
  if (!inv) return
  markPaidForm.remark = ''
  // Default to the outstanding due amount; backend RangeAttribute requires > 0.
  markPaidForm.paidAmount = Number((inv.dueAmount ?? inv.amount).toFixed(2))
})

async function confirmMarkPaid(): Promise<void> {
  const inv = markPaidDetail.data.value
  if (!inv) return
  actionLoading.value = true
  try {
    await bridge.markAsPaid(inv.id, { ...markPaidForm })
    markPaidDetail.close()
    message.success(t('toast.markPaid'))
    await crud.refresh()
  } catch (e) {
    message.error(t('toast.failed', { error: e instanceof Error ? e.message : String(e) }))
  } finally {
    actionLoading.value = false
  }
}

// ─── Cancel overlay (useDetail modal) ────────────────────────────────
const cancelDetail = useDetail<InvoiceDto>({ mode: 'modal', url: 'cancel-invoice', source: crud })
const cancelReason = ref('')
watch(() => cancelDetail.data.value, (inv) => {
  if (inv) cancelReason.value = ''
})

async function confirmCancel(): Promise<void> {
  const inv = cancelDetail.data.value
  if (!inv) return
  actionLoading.value = true
  try {
    await bridge.cancel(inv.id, cancelReason.value)
    cancelDetail.close()
    message.success(t('toast.cancelled'))
    await crud.refresh()
  } catch (e) {
    message.error(t('toast.failed', { error: e instanceof Error ? e.message : String(e) }))
  } finally {
    actionLoading.value = false
  }
}

async function sendInvoice(row: InvoiceDto): Promise<void> {
  try {
    await bridge.send(row.id)
    message.success(t('toast.sent'))
    await crud.refresh()
  } catch (e) {
    message.error(t('toast.failed', { error: e instanceof Error ? e.message : String(e) }))
  }
}

// ─── Declarative row actions (send / mark-paid / cancel) ─────────────
const rowActions: RowAction<InvoiceDto>[] = [
  {
    key: 'send',
    label: 'actions.send',
    icon: 'mdi:send',
    show: (r) => can('payment.invoice.update') && SENDABLE.has(String(r.status ?? '')),
    confirm: (r) => t('sendConfirm', { no: r.invoiceNo }),
    onClick: (r) => sendInvoice(r),
  },
  {
    key: 'markPaid',
    label: 'actions.markPaid',
    type: 'primary',
    icon: 'mdi:cash-check',
    show: (r) => can('payment.invoice.update') && !TERMINAL.has(String(r.status ?? '')),
    onClick: (r) => void markPaidDetail.open('edit', r),
  },
  {
    key: 'cancel',
    label: 'actions.cancel',
    type: 'warning',
    icon: 'mdi:close-circle-outline',
    show: (r) => can('payment.invoice.update') && !TERMINAL.has(String(r.status ?? '')),
    onClick: (r) => void cancelDetail.open('edit', r),
  },
]
</script>
