<template>
  <TDetailSection :title="t('sections.history')" icon="mdi:receipt-text-outline" :max-width="'none'">
    <BillingContactsBlock />
    <NAlert v-if="loadError" type="error" :show-icon="true" data-testid="billing-history-error">
      {{ t('history.loadFailed', { error: loadError }) }}
    </NAlert>
    <TResponsiveTable
      v-else
      :columns="columns"
      :data="invoices"
      :loading="loading"
      :row-key="(r: InvoiceDto) => r.id"
      :pagination="pagination"
      :empty-text="t('history.empty')"
      size="small"
      :bordered="false"
      remote
      data-testid="billing-history"
    />
  </TDetailSection>
</template>

<script setup lang="ts">
/**
 * Billing: the account's invoices and receipts, newest first, each with its
 * PDF. The download goes through the authenticated client (`client.download`
 * + `downloadBlob`), never a link: the endpoint authenticates by header.
 */
import { computed, h, onMounted, ref } from 'vue'
import { NAlert, NButton, type DataTableColumns } from 'naive-ui'
import { downloadBlob, formatCurrency, formatDateOnly } from '@tnzi/core'
import { STORAGE_OVERAGE_LINE_KIND, type InvoiceDto } from '@tnzi/core/services/payment'
import type { StatusType } from '@tnzi/ui'
import TDetailSection from '../../components/detail/TDetailSection.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { EMPTY_DASH } from '../../utils/placeholders'
import { useSafeMessage } from '../_shared/safe-message'
import { billingT as t, documentAmount, useBillingBridge } from './billing-context'
import BillingContactsBlock from './BillingContactsBlock.vue'

const PAGE_SIZE = 20

const STATUS_TONE: Record<string, StatusType> = {
  Draft: 'default',
  Pending: 'warning',
  Sent: 'info',
  Paid: 'success',
  PartiallyPaid: 'warning',
  Overdue: 'error',
  Cancelled: 'default',
  Refunded: 'info',
}

const bridge = useBillingBridge()
const message = useSafeMessage()

const invoices = ref<InvoiceDto[]>([])
const total = ref(0)
const page = ref(1)
const loading = ref(false)
const loadError = ref<string | null>(null)
const downloadingId = ref<string | null>(null)

function statusLabel(status: string): string {
  const leaf = status.charAt(0).toLowerCase() + status.slice(1)
  return t(`admin.modules.payment.invoices.status.${leaf}`)
}

async function download(row: InvoiceDto): Promise<void> {
  downloadingId.value = row.id
  try {
    const blob = await bridge.downloadInvoicePdf(row.id)
    downloadBlob(blob, `${row.invoiceNo || row.id}.pdf`)
  } catch (err) {
    message.error(t('history.downloadFailed', { error: err instanceof Error ? err.message : String(err) }))
  } finally {
    downloadingId.value = null
  }
}

/** The storage overage a renewal receipt carries (its own line), or null when it has none. */
function storageOverageOf(row: InvoiceDto): number | null {
  const lines = (row.lineItems ?? []).filter((l) => l.kind === STORAGE_OVERAGE_LINE_KIND)
  if (!lines.length) return null
  return lines.reduce((sum, l) => sum + (l.amount ?? 0), 0)
}

const columns = computed<DataTableColumns<InvoiceDto>>(() => [
  { key: 'invoiceNo', title: t('history.columns.invoiceNo'), minWidth: 140 },
  {
    key: 'invoiceDate',
    title: t('history.columns.date'),
    width: 140,
    render: (row) => formatDateOnly(row.invoiceDate, { fallback: EMPTY_DASH, dateStyle: 'medium' }),
  },
  {
    key: 'amount',
    title: t('history.columns.amount'),
    width: 140,
    align: 'right',
    render: (row) => {
      const amount = h('span', { class: 'font-500 tabular-nums' }, formatCurrency(documentAmount(row), row.currency))
      const overage = storageOverageOf(row)
      if (overage === null) return amount
      return h('div', { class: 'flex flex-col items-end' }, [
        amount,
        h(
          'span',
          { class: 'text-12px text-muted', 'data-testid': `billing-history-storage-${row.id}` },
          t('history.storageOverage', { amount: formatCurrency(overage, row.currency) }),
        ),
      ])
    },
  },
  {
    key: 'status',
    title: t('history.columns.status'),
    width: 140,
    render: (row) => {
      const v = String(row.status ?? '')
      return h(TStatusBadge, { value: v, type: STATUS_TONE[v] ?? 'default', label: v ? statusLabel(v) : EMPTY_DASH })
    },
  },
  {
    key: 'actions',
    title: '',
    width: 150,
    render: (row) =>
      h(
        NButton,
        {
          size: 'small',
          quaternary: true,
          loading: downloadingId.value === row.id,
          'data-testid': `billing-download-${row.id}`,
          onClick: () => void download(row),
        },
        { default: () => t('actions.download') },
      ),
  },
])

const pagination = computed(() => ({
  page: page.value,
  pageSize: PAGE_SIZE,
  itemCount: total.value,
  onUpdatePage: (next: number) => {
    page.value = next
    void load()
  },
}))

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    const result = await bridge.listInvoices(page.value, PAGE_SIZE)
    invoices.value = result?.items ?? []
    total.value = result?.totalCount ?? invoices.value.length
  } catch (err) {
    invoices.value = []
    total.value = 0
    loadError.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  void load()
})

defineExpose({ reload: load })
</script>
