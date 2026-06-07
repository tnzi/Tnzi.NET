<template>
  <!--
    Invoices — list view backed by /admin/invoices.
    List chrome delegated to TListShell + TTableRenderer (useCrudPage fetch-only).
    Read-mostly: list + lifecycle actions (send / mark-paid / cancel). The
    mark-paid and cancel modals are kept as custom siblings because they are
    lifecycle confirmations, not a CRUD create/edit form. Manual invoice
    creation lives outside this page (line-items editing is rich-form territory).
  -->
  <TListShell
    :state="crud"
    :title="t('title')"
    :show-batch="false"
    :search-placeholder="t('filter.search')"
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
      <TTableRenderer :state="crud" :show-selection="false" :translate="t" />
    </template>
  </TListShell>

  <!-- Lifecycle modals — kept as custom siblings (not CRUD forms). -->
  <NModal v-model:show="markPaidVisible" preset="card" :title="t('modal.markPaid')" class="max-w-540px">
    <NForm label-placement="top" :show-feedback="false">
      <NFormItem :label="t('form.paidAmount')" required>
        <NInputNumber v-model:value="markPaidForm.paidAmount" :min="0.01" :precision="2" class="w-full" />
      </NFormItem>
      <NFormItem :label="t('form.remark')">
        <NInput v-model:value="markPaidForm.remark" type="textarea" :rows="2" :placeholder="t('form.remarkPlaceholder')" />
      </NFormItem>
    </NForm>
    <template #footer>
      <NSpace justify="end">
        <NButton @click="markPaidVisible = false">{{ t('actions.cancel') }}</NButton>
        <NButton type="primary" :loading="actionLoading" :disabled="!markPaidForm.paidAmount || markPaidForm.paidAmount <= 0" @click="confirmMarkPaid">
          {{ t('actions.confirm') }}
        </NButton>
      </NSpace>
    </template>
  </NModal>

  <NModal v-model:show="cancelVisible" preset="card" :title="t('modal.cancel')" class="max-w-480px">
    <NForm label-placement="top" :show-feedback="false">
      <NFormItem :label="t('form.cancelReason')" required>
        <NInput v-model:value="cancelReason" type="textarea" :rows="3" />
      </NFormItem>
    </NForm>
    <template #footer>
      <NSpace justify="end">
        <NButton @click="cancelVisible = false">{{ t('actions.cancel') }}</NButton>
        <NButton type="error" :loading="actionLoading" :disabled="!cancelReason" @click="confirmCancel">
          {{ t('actions.confirmCancel') }}
        </NButton>
      </NSpace>
    </template>
  </NModal>
</template>

<script setup lang="ts">
import { h, reactive, ref } from 'vue'
import {
  NButton,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NTag,
  useMessage,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateOnly as formatDate } from '@tnzi/core'
import { useAdminClient } from '../../plugin/client'
import {
  createInvoiceBridge,
  type InvoiceDto,
  type MarkInvoicePaidDto,
} from '../../services/bridges/invoice-bridge'
import { interpolate, translatePageKey } from '../_shared/translate'
import { useCrudPage } from '../../headless/useCrudPage'
import type { ColumnDef } from '../../headless/useColumnSettings'
import TListShell from '../../components/crud/TListShell.vue'
import TTableRenderer from '../../components/crud/renderers/TTableRenderer.vue'

const bridge = createInvoiceBridge({ client: useAdminClient() })
const message = useMessage()
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('payment.invoices', key), params)

const actionLoading = ref(false)

// ─── Status filter (drives crud.setFilters) ──────────────────────────
// InvoiceStatus enum (mirrors backend Tnzi.Payment.Metadata.InvoiceStatus):
// 0=Draft, 1=Pending, 2=Sent, 3=Paid, 4=Overdue, 5=Cancelled
const statusFilter = ref<number | null>(null)
const statusOptions = [
  { value: 0, label: t('status.draft') },
  { value: 1, label: t('status.pending') },
  { value: 2, label: t('status.sent') },
  { value: 3, label: t('status.paid') },
  { value: 4, label: t('status.overdue') },
  { value: 5, label: t('status.cancelled') },
]
function onStatusChange(): void {
  crud.setFilters({ status: statusFilter.value })
  void crud.refresh()
}

// ─── Helper label / format functions ─────────────────────────────────
function statusTone(s: number): 'success' | 'warning' | 'error' | 'info' | 'default' {
  switch (s) {
    case 3: return 'success'
    case 2: return 'info'
    case 4: return 'error'
    case 5: return 'default'
    default: return 'default'
  }
}
function statusLabel(s: number): string {
  return statusOptions.find((o) => o.value === s)?.label ?? String(s)
}
function formatMoney(n: number, currency: string): string {
  try {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency: currency || 'USD' }).format(n)
  } catch {
    return `${n.toFixed(2)} ${currency}`
  }
}

// ─── Column definitions (ColumnDef[]) ────────────────────────────────
// `title` is a plain string (ColumnDef takes no thunk). `render` receives
// `Record<string, unknown>`; cast to InvoiceDto inside each renderer.
const tableColumns: ColumnDef[] = [
  {
    key: 'status',
    title: t('cols.status'),
    width: 120,
    render: (row) => {
      const r = row as unknown as InvoiceDto
      return h(NTag, { size: 'small', bordered: false, type: statusTone(r.status) }, () => statusLabel(r.status))
    },
  },
  {
    key: 'invoiceNo',
    title: t('cols.invoiceNo'),
    width: 180,
    render: (row) => {
      const r = row as unknown as InvoiceDto
      return h('code', { class: 'font-[family-name:var(--tnzi-font-mono)] text-12px' }, r.invoiceNo)
    },
  },
  { key: 'customerName', title: t('cols.customer'), ellipsis: { tooltip: true } },
  { key: 'customerEmail', title: t('cols.customerEmail'), width: 220, ellipsis: { tooltip: true } },
  {
    key: 'amount',
    title: t('cols.amount'),
    width: 130,
    align: 'right',
    render: (row) => {
      const r = row as unknown as InvoiceDto
      return formatMoney(r.amount, r.currency)
    },
  },
  {
    key: 'paidAmount',
    title: t('cols.paidAmount'),
    width: 130,
    align: 'right',
    render: (row) => {
      const r = row as unknown as InvoiceDto
      return formatMoney(r.paidAmount, r.currency)
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
  {
    key: 'actions',
    title: t('cols.actions'),
    width: 240,
    fixed: 'right',
    align: 'right',
    render: (row) => {
      const r = row as unknown as InvoiceDto
      const buttons = []
      // Send: only when not yet sent/paid/cancelled
      if (r.status === 0 || r.status === 1 || r.status === 2) {
        buttons.push(
          h(
            NPopconfirm,
            { onPositiveClick: () => sendInvoice(r) },
            {
              trigger: () => h(NButton, { size: 'tiny', tertiary: true }, {
                icon: () => h(TSvgIcon, { icon: 'mdi:send', size: 12 }),
                default: () => t('actions.send'),
              }),
              default: () => t('sendConfirm', { no: r.invoiceNo }),
            },
          ),
        )
      }
      // Mark-paid: only when unpaid (not Paid / Cancelled)
      if (r.status !== 3 && r.status !== 5) {
        buttons.push(
          h(NButton, { size: 'tiny', type: 'primary', tertiary: true, onClick: () => openMarkPaid(r) }, {
            icon: () => h(TSvgIcon, { icon: 'mdi:cash-check', size: 12 }),
            default: () => t('actions.markPaid'),
          }),
        )
      }
      // Cancel: only when not already cancelled / paid
      if (r.status !== 3 && r.status !== 5) {
        buttons.push(
          h(NButton, { size: 'tiny', type: 'warning', tertiary: true, onClick: () => openCancel(r) }, {
            icon: () => h(TSvgIcon, { icon: 'mdi:close-circle-outline', size: 12 }),
            default: () => t('actions.cancel'),
          }),
        )
      }
      return h('div', { class: 'flex justify-end gap-4px' }, buttons)
    },
  },
]

// ─── Fetch-only useCrudPage (no create/update/delete) ────────────────
// Lifecycle actions (send / mark-paid / cancel) are handled by the page's
// own bridge calls + modals, then crud.refresh().
const crud = useCrudPage<InvoiceDto>({
  pageId: 'payment.invoices',
  columns: tableColumns,
  rowKey: (r) => r.id,
  fetchData: async (q) => {
    const status = (q.filters.status as number | null | undefined) ?? null
    const r = await bridge.getList({
      pageIndex: q.pageIndex,
      pageSize: q.pageSize,
      status,
      searchText: q.searchText || null,
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
crud.refresh().catch(() => undefined)

// ─── Mark paid modal ──────────────────────────────────────────────
const markPaidVisible = ref(false)
const activeRow = ref<InvoiceDto | null>(null)
const markPaidForm = reactive<MarkInvoicePaidDto>({
  paidAmount: 0,
  remark: '',
})

function openMarkPaid(row: InvoiceDto): void {
  activeRow.value = row
  markPaidForm.remark = ''
  // Default to the outstanding due amount; backend RangeAttribute requires > 0.
  markPaidForm.paidAmount = Number((row.dueAmount ?? row.amount).toFixed(2))
  markPaidVisible.value = true
}

async function confirmMarkPaid(): Promise<void> {
  if (!activeRow.value) return
  actionLoading.value = true
  try {
    await bridge.markAsPaid(activeRow.value.id, { ...markPaidForm })
    markPaidVisible.value = false
    message.success(t('toast.markPaid'))
    await crud.refresh()
  } catch (e) {
    message.error(t('toast.failed', { error: e instanceof Error ? e.message : String(e) }))
  } finally {
    actionLoading.value = false
  }
}

// ─── Cancel modal ─────────────────────────────────────────────────
const cancelVisible = ref(false)
const cancelReason = ref('')

function openCancel(row: InvoiceDto): void {
  activeRow.value = row
  cancelReason.value = ''
  cancelVisible.value = true
}

async function confirmCancel(): Promise<void> {
  if (!activeRow.value) return
  actionLoading.value = true
  try {
    await bridge.cancel(activeRow.value.id, cancelReason.value)
    cancelVisible.value = false
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
</script>
