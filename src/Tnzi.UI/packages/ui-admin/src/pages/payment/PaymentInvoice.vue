<template>
  <!--
    PaymentInvoice — list view backed by /admin/invoices.
    Read-mostly: list + lifecycle actions (send / mark-paid / cancel).
    Manual invoice creation lives outside this page because line-items
    editing is rich-form territory.
  -->
  <div class="t-invoice-page t-stack-page">
    <div class="t-invoice-page__header">
      <div class="t-invoice-page__title">
        <TSvgIcon icon="mdi:file-document-outline" :size="20" />
        <h2>{{ t('title') }}</h2>
      </div>
      <div class="t-invoice-page__toolbar">
        <NInput
          v-model:value="searchText"
          :placeholder="t('filter.search')"
          clearable
          size="medium"
          style="width: 240px"
          @keyup.enter="refresh"
        >
          <template #prefix><TSvgIcon icon="mdi:magnify" :size="14" /></template>
        </NInput>
        <NSelect
          v-model:value="statusFilter"
          :options="statusOptions"
          :placeholder="t('filter.status')"
          clearable
          size="medium"
          style="width: 180px"
          @update:value="refresh"
        />
        <NButton size="small" :loading="loading" @click="refresh">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('actions.refresh') }}
        </NButton>
      </div>
    </div>

    <NCard size="small" :bordered="false" class="t-table-card">
      <NDataTable
        :columns="columns"
        :data="rows"
        :loading="loading"
        :pagination="{
          page: pageIndex,
          pageSize,
          itemCount: totalCount,
          onUpdatePage: (p: number) => { pageIndex = p; refresh() },
        }"
        :bordered="false"
        remote
        size="small"
        :flex-height="true"
      />
    </NCard>

    <NModal v-model:show="markPaidVisible" preset="card" :title="t('modal.markPaid')" style="max-width: 540px">
      <NForm label-placement="top" :show-feedback="false">
        <NFormItem :label="t('form.paidAmount')" required>
          <NInputNumber v-model:value="markPaidForm.paidAmount" :min="0.01" :precision="2" style="width: 100%" />
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

    <NModal v-model:show="cancelVisible" preset="card" :title="t('modal.cancel')" style="max-width: 480px">
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
  </div>
</template>

<script setup lang="ts">
import { h, onMounted, reactive, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
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
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateOnly as formatDate } from '@tnzi/core'
import { useAdminClient } from '../../plugin/client'
import {
  createInvoiceBridge,
  type InvoiceDto,
  type MarkInvoicePaidDto,
} from '../../services/bridges/invoice-bridge'
import { interpolate, translatePageKey } from '../_shared/translate'

const bridge = createInvoiceBridge({ client: useAdminClient() })
const message = useMessage()
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('payment.invoices', key), params)

const loading = ref(false)
const actionLoading = ref(false)
const rows = ref<InvoiceDto[]>([])
const totalCount = ref(0)
const pageIndex = ref(1)
const pageSize = ref(20)
const searchText = ref('')
const statusFilter = ref<number | null>(null)

// InvoiceStatus enum (mirrors backend Tnzi.Payment.Metadata.InvoiceStatus):
// 0=Draft, 1=Pending, 2=Sent, 3=Paid, 4=Overdue, 5=Cancelled
const statusOptions = [
  { value: 0, label: t('status.draft') },
  { value: 1, label: t('status.pending') },
  { value: 2, label: t('status.sent') },
  { value: 3, label: t('status.paid') },
  { value: 4, label: t('status.overdue') },
  { value: 5, label: t('status.cancelled') },
]
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
const columns: DataTableColumns<InvoiceDto> = [
  {
    title: () => t('cols.status'),
    key: 'status',
    width: 120,
    render: (row) => h(NTag, { size: 'small', bordered: false, type: statusTone(row.status) }, () => statusLabel(row.status)),
  },
  {
    title: () => t('cols.invoiceNo'),
    key: 'invoiceNo',
    width: 180,
    render: (row) => h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 12px;' }, row.invoiceNo),
  },
  { title: () => t('cols.customer'), key: 'customerName', ellipsis: { tooltip: true } },
  { title: () => t('cols.customerEmail'), key: 'customerEmail', width: 220, ellipsis: { tooltip: true } },
  {
    title: () => t('cols.amount'),
    key: 'amount',
    width: 130,
    align: 'right',
    render: (row) => formatMoney(row.amount, row.currency),
  },
  {
    title: () => t('cols.paidAmount'),
    key: 'paidAmount',
    width: 130,
    align: 'right',
    render: (row) => formatMoney(row.paidAmount, row.currency),
  },
  { title: () => t('cols.invoiceDate'), key: 'invoiceDate', width: 130, render: (row) => formatDate(row.invoiceDate) },
  { title: () => t('cols.dueDate'), key: 'dueDate', width: 130, render: (row) => formatDate(row.dueDate) },
  {
    title: () => t('cols.actions'),
    key: 'actions',
    width: 240,
    align: 'right',
    fixed: 'right',
    render: (row) => {
      const buttons = []
      // Send: only when not yet sent/paid/cancelled
      if (row.status === 0 || row.status === 1 || row.status === 2) {
        buttons.push(
          h(
            NPopconfirm,
            { onPositiveClick: () => sendInvoice(row) },
            {
              trigger: () => h(NButton, { size: 'tiny', tertiary: true }, {
                icon: () => h(TSvgIcon, { icon: 'mdi:send', size: 12 }),
                default: () => t('actions.send'),
              }),
              default: () => t('sendConfirm', { no: row.invoiceNo }),
            },
          ),
        )
      }
      // Mark-paid: only when unpaid (not Paid / Cancelled)
      if (row.status !== 3 && row.status !== 5) {
        buttons.push(
          h(NButton, { size: 'tiny', type: 'primary', tertiary: true, onClick: () => openMarkPaid(row) }, {
            icon: () => h(TSvgIcon, { icon: 'mdi:cash-check', size: 12 }),
            default: () => t('actions.markPaid'),
          }),
        )
      }
      // Cancel: only when not already cancelled / paid
      if (row.status !== 3 && row.status !== 5) {
        buttons.push(
          h(NButton, { size: 'tiny', type: 'warning', tertiary: true, onClick: () => openCancel(row) }, {
            icon: () => h(TSvgIcon, { icon: 'mdi:close-circle-outline', size: 12 }),
            default: () => t('actions.cancel'),
          }),
        )
      }
      return h('div', { style: 'display: flex; justify-content: flex-end; gap: 4px;' }, buttons)
    },
  },
]

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
    await refresh()
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
    await refresh()
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
    await refresh()
  } catch (e) {
    message.error(t('toast.failed', { error: e instanceof Error ? e.message : String(e) }))
  }
}

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const result = await bridge.getList({
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      status: statusFilter.value,
      searchText: searchText.value || null,
    })
    rows.value = result.items
    totalCount.value = result.totalCount
  } catch {
    rows.value = []
    totalCount.value = 0
  } finally {
    loading.value = false
  }
}

onMounted(() => { void refresh() })
</script>

<style scoped>
/* Layout shell from shared `.t-stack-page` + `.t-table-card` utilities. */
.t-invoice-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  flex-wrap: wrap;
  padding: 16px 20px;
  background: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  border-radius: 8px;
}
.t-invoice-page__title {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-invoice-page__title h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}
.t-invoice-page__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
</style>
