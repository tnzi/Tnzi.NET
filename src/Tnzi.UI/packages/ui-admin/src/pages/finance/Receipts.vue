<template>
  <TCrudPage :state="crud" :all-columns="columns"
    :search-fields="searchFields" :title="title" :row-actions="rowActions" :translate="t">
    <template #primary>
      <NButton v-if="canCreate" size="small" type="primary" :loading="uploading" @click="triggerUpload">
        <template #icon><TSvgIcon icon="mdi:upload-outline" :size="16" /></template>
        {{ t('upload') }}
      </NButton>
      <input ref="fileInput" type="file" accept="image/*,application/pdf" class="fin-receipts__file" @change="onFileChange" />
    </template>
  </TCrudPage>

  <!-- Receipt detail (preview + extracted fields + extract / convert). -->
  <TDetailHost :state="detail" :title="t('detail.title')" :width="620" :footer="false" :translate="t">
    <div v-if="detail.data.value" class="fin-receipts__detail">
      <div class="fin-receipts__preview">
        <NImage :src="previewSrc" object-fit="contain" class="fin-receipts__preview-img" :alt="detail.data.value.originalFileName ?? ''" />
        <a :href="downloadHref" target="_blank" rel="noopener" class="fin-receipts__file-link">
          <TSvgIcon icon="mdi:file-outline" :size="14" />
          {{ detail.data.value.originalFileName ?? t('detail.openFile') }}
        </a>
      </div>

      <div class="fin-receipts__status-row">
        <TStatusBadge
          :value="String(detail.data.value.status)"
          :type="statusMeta(detail.data.value.status).type"
          :label="t(statusMeta(detail.data.value.status).label)"
        />
        <span v-if="detail.data.value.confidence != null" class="fin-receipts__confidence">
          {{ t('detail.confidence', { pct: String(Math.round((detail.data.value.confidence ?? 0) * 100)) }) }}
        </span>
      </div>

      <NAlert v-if="detail.data.value.failReason" type="error" :bordered="false" class="fin-receipts__fail">
        {{ detail.data.value.failReason }}
      </NAlert>

      <TFormSchemaRenderer
        :schema="receiptExtractionFormSchema"
        :model="editModel"
        :columns="2"
        :readonly="isConverted"
        :translate="t"
      />

      <div v-if="!isConverted" class="fin-receipts__detail-actions">
        <NButton size="small" :loading="extracting" @click="runExtract">
          <template #icon><TSvgIcon icon="mdi:auto-fix" :size="16" /></template>
          {{ t('detail.extract') }}
        </NButton>
        <NButton v-if="canUpdate" size="small" @click="saveExtraction">{{ t('detail.save') }}</NButton>
        <NButton v-if="canConvert" size="small" type="primary" @click="openConvert">
          {{ t('detail.convert') }}
        </NButton>
      </div>
      <p v-else class="fin-receipts__hint">{{ t('detail.convertedHint') }}</p>
    </div>
  </TDetailHost>

  <!-- Convert to expense / bill draft. -->
  <TDetailHost :state="convertDetail" :title="t('convert.title')" :width="460" :footer="false" :translate="t">
    <NForm label-placement="top" size="small" class="fin-receipts__form">
      <NFormItem :label="t('convert.docType')" :show-feedback="false">
        <NRadioGroup v-model:value="convertForm.docType" size="small">
          <NRadioButton :value="ReceiptDocType.Expense">{{ t('convert.expense') }}</NRadioButton>
          <NRadioButton :value="ReceiptDocType.Bill">{{ t('convert.bill') }}</NRadioButton>
        </NRadioGroup>
      </NFormItem>
      <NFormItem :label="t('convert.vendor')" :show-feedback="false">
        <NSelect v-model:value="convertForm.vendorId" :options="sources.vendorOptions.value" :placeholder="t('convert.vendor')" filterable clearable />
      </NFormItem>
      <NFormItem :label="t('convert.account')" :show-feedback="false">
        <NSelect v-model:value="convertForm.accountId" :options="sources.leafAccountOptions.value" :placeholder="t('convert.account')" filterable clearable />
      </NFormItem>
      <NFormItem
        v-if="convertForm.docType === ReceiptDocType.Expense"
        :label="t('convert.paidFrom')"
        :show-feedback="false"
      >
        <NSelect
          v-model:value="convertForm.paidFromAccountId"
          :options="sources.fundsAccountOptions.value"
          :placeholder="t('convert.paidFrom')"
          filterable
          clearable
        />
      </NFormItem>
      <div class="fin-receipts__form-actions">
        <NButton size="small" @click="convertDetail.close()">{{ t('common.cancel') }}</NButton>
        <NButton size="small" type="primary" :loading="converting" :disabled="converting" @click="submitConvert">
          {{ t('convert.submit') }}
        </NButton>
      </div>
    </NForm>
  </TDetailHost>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NSelect, NRadioGroup, NRadioButton, NImage, NAlert, NForm, NFormItem } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { deleteAction, type RowAction } from '../../headless/rowActions'
import {
  createFinanceBridge,
  ReceiptStatus,
  ReceiptDocType,
  type ReceiptDto,
} from '../../services/bridges/finance-bridge'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { createFinanceOptionSources } from './options'
import { tsToIsoDate } from './money'
import { buildReceiptSearchFields, buildReceiptColumns, receiptExtractionFormSchema, RECEIPT_STATUS_META, type ReceiptRow } from './receipt-config'

const client = useAdminClient()
const bridge = createFinanceBridge({ client })
const storage = createStorageBridge({ client })
const t = makePageTranslator('finance.receipts')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const title = 'tnzi.admin.modules.finance.receipts.title'
const columns = buildReceiptColumns(t)

// 真实筛选（标准 1）：只声明后端 QueryDto 真的支持的字段。
const searchFields = buildReceiptSearchFields(t)

const canCreate = computed(() => can('finance.receipt.create'))
const canUpdate = computed(() => can('finance.receipt.update'))
const canConvert = computed(() => can('finance.document.create'))

function statusMeta(status?: ReceiptStatus | null) {
  return RECEIPT_STATUS_META[String(status ?? '')] ?? { type: 'default' as const, label: 'status.uploaded' }
}

const crud = useCrudPage<ReceiptRow>({
  pageId: 'finance.receipts',
  permission: 'finance.receipt',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.receipts.fetch(q),
  loadDetailById: (id) => bridge.receipts.getById(String(id)),
  deleteData: (ids) => Promise.all(ids.map((id) => bridge.receipts.delete(String(id)))).then(() => undefined),
})

void sources.ensureVendors()
void sources.ensureLeafAccounts()
void sources.ensureFundsAccounts()

// ── Upload → register ───────────────────────────────────────────
const fileInput = ref<HTMLInputElement | null>(null)
const uploading = ref(false)

function triggerUpload() {
  fileInput.value?.click()
}

async function onFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  uploading.value = true
  try {
    const uploaded = await storage.files.upload(file)
    await bridge.receipts.create({ fileId: uploaded.id, fileName: file.name })
    message.success(t('uploadSuccess'))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    uploading.value = false
    if (fileInput.value) fileInput.value.value = ''
  }
}

// ── Detail drawer ───────────────────────────────────────────────
const detail = useDetail<ReceiptDto>({ mode: 'drawer', url: 'detail', loadData: (id) => bridge.receipts.getById(String(id)) })

const editModel = reactive<Record<string, unknown>>({})
const isConverted = computed(() => detail.data.value?.status === ReceiptStatus.Converted)
const previewSrc = computed(() => (detail.data.value ? storage.files.previewUrl(detail.data.value.fileId) : ''))
const downloadHref = computed(() => (detail.data.value ? storage.files.downloadUrl(detail.data.value.fileId) : '#'))

watch(
  () => detail.data.value?.id,
  () => {
    const r = detail.data.value
    if (!r) return
    editModel.vendorName = r.vendorName ?? null
    editModel.docDate = r.docDate ?? null
    editModel.currency = r.currency ?? null
    editModel.subtotal = r.subtotal ?? null
    editModel.taxAmount = r.taxAmount ?? null
    editModel.total = r.total ?? null
    editModel.reference = r.reference ?? null
  },
)

function openReceipt(row: ReceiptRow) {
  void detail.open('view', String(row.id ?? ''))
}

const extracting = ref(false)

async function runExtract() {
  const id = detail.data.value?.id
  if (!id) return
  extracting.value = true
  try {
    const updated = await bridge.receipts.extract(id)
    detail.data.value = updated
    message.success(t('detail.extractSuccess'))
    await crud.refresh()
  } catch (error) {
    // A 501 signals no IReceiptExtractor is registered; the backend message
    // guides the operator to load Tnzi.Finance.Ai (default) or register one.
    const msg = error instanceof Error ? error.message : String(error)
    message.error(msg)
  } finally {
    extracting.value = false
  }
}

/** Normalise a date-field model value (timestamp after edit / ISO on load) to an ISO date. */
function docDateIso(v: unknown): string | null {
  if (v == null || v === '') return null
  if (typeof v === 'number') return tsToIsoDate(v)
  return String(v)
}

async function saveExtraction() {
  const id = detail.data.value?.id
  if (!id) return
  try {
    const updated = await bridge.receipts.update(id, {
      vendorName: (editModel.vendorName as string | null) ?? null,
      docDate: docDateIso(editModel.docDate),
      currency: (editModel.currency as string | null) ?? null,
      subtotal: editModel.subtotal != null ? Number(editModel.subtotal) : null,
      taxAmount: editModel.taxAmount != null ? Number(editModel.taxAmount) : null,
      total: editModel.total != null ? Number(editModel.total) : null,
      reference: (editModel.reference as string | null) ?? null,
    })
    detail.data.value = updated
    message.success(t('detail.saveSuccess'))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

// ── Convert ─────────────────────────────────────────────────────
const convertDetail = useDetail<{ id: string }>({ mode: 'modal', url: 'convert' })
const convertForm = reactive<{
  docType: ReceiptDocType
  vendorId: string | null
  accountId: string | null
  paidFromAccountId: string | null
}>({ docType: ReceiptDocType.Expense, vendorId: null, accountId: null, paidFromAccountId: null })
const converting = ref(false)

function openConvert() {
  const r = detail.data.value
  if (!r) return
  convertForm.docType = ReceiptDocType.Expense
  convertForm.vendorId = r.matchedVendorId ?? null
  convertForm.accountId = null
  convertForm.paidFromAccountId = null
  void convertDetail.open('create')
}

async function submitConvert() {
  const id = detail.data.value?.id
  if (!id) return
  converting.value = true
  try {
    await bridge.receipts.convert(id, {
      docType: convertForm.docType,
      vendorId: convertForm.vendorId,
      accountId: convertForm.accountId,
      paidFromAccountId: convertForm.docType === ReceiptDocType.Expense ? convertForm.paidFromAccountId : null,
    })
    message.success(t('convert.success'))
    convertDetail.close()
    detail.close()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    converting.value = false
  }
}

const rowActions: RowAction<ReceiptRow>[] = [
  {
    key: 'view',
    label: 'actions.view',
    onClick: (r) => openReceipt(r),
  },
  deleteAction(crud, { confirm: 'confirmDelete', show: (r: ReceiptRow) => r.status !== ReceiptStatus.Converted }),
]
</script>

<style scoped>
.fin-receipts__file {
  display: none;
}

.fin-receipts__detail,
.fin-receipts__form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-receipts__preview {
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-items: flex-start;
}

.fin-receipts__preview-img {
  max-width: 100%;
  max-height: 280px;
  border: 1px solid var(--tnzi-border, #eee);
  border-radius: var(--tnzi-admin-radius-md, 6px);
  background: var(--tnzi-layout-bg, #fafafa);
}

.fin-receipts__file-link {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 13px;
  color: var(--tnzi-primary, #18a058);
  text-decoration: none;
}

.fin-receipts__status-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.fin-receipts__confidence {
  font-size: 13px;
  color: var(--tnzi-text-3, #999);
}

.fin-receipts__fail {
  font-size: 13px;
}

.fin-receipts__detail-actions,
.fin-receipts__form-actions {
  display: flex;
  gap: 12px;
}

.fin-receipts__form-actions {
  justify-content: flex-end;
}

.fin-receipts__hint {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-text-3, #999);
}
</style>
