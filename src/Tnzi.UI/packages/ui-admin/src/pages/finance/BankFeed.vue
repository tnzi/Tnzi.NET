<template>
  <TContentPage :title="title" icon="mdi:bank-transfer" :translate="t" scroll="fill">
    <template #actions>
      <NSelect
        v-model:value="accountId"
        :options="sources.fundsAccountOptions.value"
        :placeholder="t('workspace.selectAccount')"
        size="small"
        filterable
        clearable
        class="fin-feed__account"
      />
    </template>

    <TEmpty v-if="!accountId" :text="t('workspace.selectAccount')" />

    <TReconcileWorkspace
      v-else
      ref="workspace"
      :bridge="bridge"
      :account-id="accountId"
      :has-draft-reconciliation="hasDraft"
      :expense-account-options="sources.expenseAccountOptions.value"
      :funds-account-options="sources.fundsAccountOptions.value"
      :customer-options="sources.customerOptions.value"
      :vendor-options="sources.vendorOptions.value"
      :can-match="canMatch"
      :can-create-document="canCreateDoc"
      :t="t"
      @create-reconciliation="createDraftReconciliation"
    >
      <template #actions>
        <NButton v-if="canImport" size="small" type="primary" @click="openImport">
          <template #icon><TSvgIcon icon="mdi:file-upload-outline" :size="16" /></template>
          {{ t('workspace.import') }}
        </NButton>
        <NButton v-if="canImport" size="small" :loading="pulling" @click="runPull">
          <template #icon><TSvgIcon icon="mdi:cloud-download-outline" :size="16" /></template>
          {{ t('workspace.pull') }}
        </NButton>
        <NButton v-if="canMatch" size="small" :loading="suggesting" @click="runSuggest">
          <template #icon><TSvgIcon icon="mdi:auto-fix" :size="16" /></template>
          {{ t('workspace.suggest') }}
        </NButton>
        <NButton size="small" quaternary @click="openBatches">
          <template #icon><TSvgIcon icon="mdi:format-list-bulleted" :size="16" /></template>
          {{ t('workspace.batches') }}
        </NButton>
      </template>
    </TReconcileWorkspace>
  </TContentPage>

  <!-- Import statement (OFX / CSV). -->
  <TDetailHost :state="importDetail" :title="t('import.title')" :width="560" :footer="false" :translate="t">
    <div class="fin-feed__import">
      <NRadioGroup v-model:value="importSource" size="small">
        <NRadioButton :value="BankTransactionSource.Ofx">OFX</NRadioButton>
        <NRadioButton :value="BankTransactionSource.Csv">CSV</NRadioButton>
      </NRadioGroup>
      <input
        ref="fileInput"
        type="file"
        accept=".ofx,.csv,.txt"
        class="fin-feed__file"
        @change="onFileChange"
      />

      <template v-if="importSource === BankTransactionSource.Csv">
        <TFormSchemaRenderer :schema="csvParseFormSchema" :model="csvMapping" :columns="2" :translate="t" />

        <!-- Preview the file we are about to send: a wrong delimiter or skip-rows
             is obvious here, instead of after importing hundreds of junk rows. -->
        <div v-if="peek.headers.length > 0" class="fin-feed__preview">
          <div class="fin-feed__preview-head">
            <span class="fin-feed__preview-title">{{ t('import.previewTitle') }}</span>
            <NButton size="tiny" quaternary @click="applyGuess">{{ t('import.autoDetect') }}</NButton>
          </div>
          <div class="fin-feed__preview-scroll">
            <table class="fin-feed__preview-table">
              <thead>
                <tr>
                  <th v-for="(header, i) in peek.headers" :key="i">{{ header }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(row, r) in peek.rows" :key="r">
                  <td v-for="(_, c) in peek.headers" :key="c">{{ row[c] ?? '' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-if="peek.rows.length === 0" class="fin-feed__empty">{{ t('import.previewNoRows') }}</p>
        </div>
        <p v-else-if="importFile" class="fin-feed__empty">{{ t('import.previewUnreadable') }}</p>
        <p v-else class="fin-feed__empty">{{ t('import.previewHint') }}</p>

        <TFormSchemaRenderer
          v-if="peek.headers.length > 0"
          :schema="csvColumnFormSchema"
          :model="csvMapping"
          :columns="2"
          :field-renderers="fieldRenderers"
          :translate="t"
        />
      </template>

      <div class="fin-feed__import-actions">
        <NButton size="small" @click="importDetail.close()">{{ t('import.cancel') }}</NButton>
        <NButton size="small" type="primary" :loading="importing" :disabled="!canSubmitImport || importing" @click="submitImport">
          {{ t('import.submit') }}
        </NButton>
      </div>
    </div>
  </TDetailHost>


  <!-- Import batches. -->
  <TDetailHost :state="batchesDetail" :title="t('batches.title')" :width="720" :footer="false" :translate="t">
    <TResponsiveTable
      :columns="batchColumns"
      :data="batches"
      :row-key="(r: BankImportBatchDto) => r.id"
      size="small"
      mobile="scroll"
      :pagination="false"
      :bordered="false"
    />
  </TDetailHost>
</template>

<script setup lang="ts">
/**
 * Bank feed = the reconcile workspace, not a transaction grid.
 *
 * This page owns statement ingestion (import / provider pull / batch ledger)
 * and hands the actual review to `TReconcileWorkspace`, where the Xero
 * one-line-at-a-time flow lives. See the finance UX plan under
 * `docs/superpowers/specs/`.
 */
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, ref, watch, h } from 'vue'
import { NButton, NSelect, NRadioGroup, NRadioButton, useDialog, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'

import TContentPage from '../../components/layout/TContentPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TEmpty from '../../components/data/TEmpty.vue'
import TReconcileWorkspace from '../../components/finance/TReconcileWorkspace.vue'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import {
  createFinanceBridge,
  BankTransactionSource,
  ReconciliationStatus,
  type BankImportBatchDto,
  type CsvMappingDto,
} from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { createFinanceOptionSources } from './options'
import { tsToIsoDate, fmtDate } from './money'
import { csvParseFormSchema, csvColumnFormSchema } from './bank-feed-config'
import { peekCsv, guessColumns, type CsvPeek } from './csv-preview'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.bankFeed')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

// Workspace write operations are custom (import / match), not CRUD callbacks,
// so gate them on the module permission codes directly (fail-open for
// super-admin / unloaded; the backend [ApiAuthorize] is the real wall).
const canImport = computed(() => can('finance.bankFeed.create'))
const canMatch = computed(() => can('finance.bankFeed.update'))
// Create-document delegates to the document workflow (backend gate = finance.document.create).
const canCreateDoc = computed(() => can('finance.document.create'))

const title = 'tnzi.admin.modules.finance.bankFeed.title'

const accountId = ref<string | null>(null)
const workspace = ref<{ reload: () => Promise<void> } | null>(null)

void sources.ensureFundsAccounts()
void sources.ensureExpenseAccounts()
void sources.ensureCustomers()
void sources.ensureVendors()

async function refreshList() {
  await workspace.value?.reload()
}

// -- Draft reconciliation gate ----------------------------------
// Confirming a match writes a cleared line into the account open
// reconciliation, so the workspace has to know up front whether one exists.
// Otherwise every OK would 400 and the operator would learn the rule by
// failing at it.
const hasDraft = ref(false)

async function checkDraft() {
  if (!accountId.value) {
    hasDraft.value = false
    return
  }
  try {
    const page = await bridge.reconciliations.fetch({
      pageIndex: 1,
      pageSize: 1,
      filters: { accountId: accountId.value, status: ReconciliationStatus.Draft },
    })
    hasDraft.value = page.items.length > 0
  } catch {
    // Fail OPEN: a failed probe must not raise a banner telling the operator to
    // create a reconciliation that may well already exist. The backend 400 is
    // the real gate.
    hasDraft.value = true
  }
}

watch(accountId, () => void checkDraft(), { immediate: true })

async function createDraftReconciliation() {
  if (!accountId.value) return
  try {
    await bridge.reconciliations.create({
      accountId: accountId.value,
      statementDate: tsToIsoDate(Date.now()),
      statementEndingBalance: 0,
    })
    hasDraft.value = true
    message.success(t('noDraft.created'))
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

// -- Import -----------------------------------------------------
const importDetail = useDetail<{ id: string }>({ mode: 'modal', url: 'import' })
const importSource = ref<BankTransactionSource>(BankTransactionSource.Csv)
const importFile = ref<File | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
const importing = ref(false)
const csvMapping = ref<Record<string, unknown>>({})

function mappingStorageKey(id: string): string {
  return `tnzi-bankfeed-csv-mapping:${id}`
}

/** Head of the chosen file, cached so re-parsing on a delimiter change costs no re-read. */
const fileHead = ref('')
const peek = ref<CsvPeek>({ headers: [], rows: [] })
/** A mapping restored from localStorage is the user own prior correction - do not overwrite it with a guess. */
const hadSavedMapping = ref(false)

/** Only the first chunk is read: enough to show a preview, never the whole statement. */
const PEEK_BYTES = 64 * 1024

function openImport() {
  if (!accountId.value) return
  importFile.value = null
  if (fileInput.value) fileInput.value.value = ''
  fileHead.value = ''
  peek.value = { headers: [], rows: [] }
  // Restore the last mapping remembered for this account.
  const saved = typeof localStorage !== 'undefined' ? localStorage.getItem(mappingStorageKey(accountId.value)) : null
  hadSavedMapping.value = !!saved
  csvMapping.value = saved ? (JSON.parse(saved) as Record<string, unknown>) : { hasHeader: true, delimiter: ',' }
  void importDetail.open('create')
}

function onFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  importFile.value = input.files?.[0] ?? null
  void loadPeek()
}

/** Re-derive the preview from the cached head (no file re-read). */
function reparse() {
  if (!fileHead.value) {
    peek.value = { headers: [], rows: [] }
    return
  }
  const m = csvMapping.value
  peek.value = peekCsv(
    fileHead.value,
    (m.delimiter as string) || ',',
    m.hasHeader !== false,
    Number(m.skipRows ?? 0),
  )
}

async function loadPeek() {
  const file = importFile.value
  if (!file || importSource.value !== BankTransactionSource.Csv) {
    fileHead.value = ''
    peek.value = { headers: [], rows: [] }
    return
  }
  try {
    fileHead.value = await file.slice(0, PEEK_BYTES).text()
  } catch {
    // Unreadable head only costs the preview - the server still does the real parse.
    fileHead.value = ''
  }
  reparse()
  // Guess only when the user has no remembered mapping of their own; the
  // "Auto-detect" button re-runs it on demand when a saved mapping fits badly.
  if (!hadSavedMapping.value) applyGuess()
}

/** Columns picked by name, so the mapping never asks anyone to count commas. */
const csvColumnOptions = computed(() =>
  peek.value.headers.map((header, index) => ({ label: `${index + 1}. ${header}`, value: index })),
)

const fieldRenderers = {
  'finance-csv-column': selectRenderer(() => csvColumnOptions.value, { placeholder: t('import.columnPlaceholder') }),
}

/**
 * Fill the mapping from the header names. Clears the losing amount shape so a
 * previous guess cannot leave both a signed column and a debit/credit pair set.
 */
function applyGuess() {
  if (peek.value.headers.length === 0) return
  const guess = guessColumns(peek.value.headers)
  csvMapping.value = {
    ...csvMapping.value,
    dateColumn: guess.date ?? undefined,
    descriptionColumn: guess.description ?? undefined,
    referenceColumn: guess.reference ?? undefined,
    amountColumn: guess.amount ?? undefined,
    debitColumn: guess.debit ?? undefined,
    creditColumn: guess.credit ?? undefined,
  }
}

// Re-parse when the read settings change; re-peek when the file/source changes.
watch(() => [csvMapping.value.delimiter, csvMapping.value.hasHeader, csvMapping.value.skipRows], reparse)
watch(importSource, () => void loadPeek())

/**
 * A CSV import needs a date and at least one amount source; without them the
 * server is guaranteed to reject the file, so do not offer to send it.
 */
const canSubmitImport = computed(() => {
  if (!importFile.value) return false
  if (importSource.value !== BankTransactionSource.Csv) return true
  const m = csvMapping.value
  const has = (v: unknown) => v != null && v !== ''
  return has(m.dateColumn) && (has(m.amountColumn) || has(m.debitColumn) || has(m.creditColumn))
})

function buildMapping(): CsvMappingDto {
  const m = csvMapping.value
  const numOrUndef = (v: unknown) => (v == null || v === '' ? undefined : Number(v))
  return {
    hasHeader: m.hasHeader !== false,
    delimiter: (m.delimiter as string) || ',',
    dateColumn: Number(m.dateColumn ?? 0),
    dateFormat: (m.dateFormat as string) || undefined,
    amountColumn: numOrUndef(m.amountColumn),
    debitColumn: numOrUndef(m.debitColumn),
    creditColumn: numOrUndef(m.creditColumn),
    descriptionColumn: numOrUndef(m.descriptionColumn),
    referenceColumn: numOrUndef(m.referenceColumn),
    skipRows: Number(m.skipRows ?? 0),
    decimalSeparator: (m.decimalSeparator as string) || undefined,
  }
}

async function submitImport() {
  if (!accountId.value || !importFile.value) return
  importing.value = true
  try {
    const isCsv = importSource.value === BankTransactionSource.Csv
    const mapping = isCsv ? buildMapping() : undefined
    const result = await bridge.bankFeed.import(accountId.value, importSource.value, importFile.value, mapping)
    if (isCsv && typeof localStorage !== 'undefined') {
      localStorage.setItem(mappingStorageKey(accountId.value), JSON.stringify(csvMapping.value))
    }
    message.success(t('import.success', { imported: String(result.importedCount), skipped: String(result.skippedCount) }))
    importDetail.close()
    await refreshList()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    importing.value = false
  }
}

// -- Pull / Suggest ---------------------------------------------
const suggesting = ref(false)
const pulling = ref(false)

async function runPull() {
  if (!accountId.value) return
  pulling.value = true
  try {
    const result = await bridge.bankFeed.pull(accountId.value)
    message.success(t('import.success', { imported: String(result.importedCount), skipped: String(result.skippedCount) }))
    await refreshList()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    pulling.value = false
  }
}

async function runSuggest() {
  if (!accountId.value) return
  suggesting.value = true
  try {
    const result = await bridge.bankFeed.suggest(accountId.value)
    message.success(t('workspace.suggestResult', { suggested: String(result.suggested), auto: String(result.autoConfirmed) }))
    await refreshList()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    suggesting.value = false
  }
}

// -- Batches ----------------------------------------------------
function safeDialog() {
  try {
    return useDialog()
  } catch {
    return null
  }
}

const dialog = safeDialog()
const batchesDetail = useDetail<{ id: string }>({ mode: 'drawer', url: 'batches' })
const batches = ref<BankImportBatchDto[]>([])

function openBatches() {
  if (!accountId.value) return
  batches.value = []
  void batchesDetail.open('create')
  void loadBatches()
}

async function loadBatches() {
  if (!accountId.value) return
  try {
    const page = await bridge.bankFeed.batches({ pageIndex: 1, pageSize: 100, filters: { accountId: accountId.value } })
    batches.value = page.items
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

function deleteBatch(batch: BankImportBatchDto) {
  const run = async () => {
    try {
      await bridge.bankFeed.deleteBatch(batch.id)
      message.success(t('batches.deleteSuccess'))
      await loadBatches()
      await refreshList()
    } catch (error) {
      message.error(error instanceof Error ? error.message : String(error))
    }
  }
  // Deleting an import batch discards a whole imported statement (and its unmatched rows) - confirm first.
  if (!dialog) return void run()
  dialog.warning({
    title: t('batches.delete'),
    content: t('batches.deleteConfirm'),
    positiveText: t('batches.delete'),
    negativeText: t('batches.cancel'),
    positiveButtonProps: { type: 'error' },
    onPositiveClick: run,
  })
}

const batchColumns: DataTableColumns<BankImportBatchDto> = [
  { key: 'creationTime', title: t('batches.imported'), width: 120, render: (r) => fmtDate(r.creationTime) },
  { key: 'source', title: t('batches.source'), width: 90, render: (r) => String(r.source) },
  { key: 'fileName', title: t('batches.fileName'), minWidth: 160, render: (r) => r.fileName ?? EMPTY_DASH },
  { key: 'importedCount', title: t('batches.count'), width: 90, render: (r) => String(r.importedCount) },
  { key: 'matchedCount', title: t('batches.matched'), width: 90, render: (r) => String(r.matchedCount) },
  {
    key: 'actions',
    title: '',
    width: 90,
    render: (r) =>
      r.matchedCount > 0
        ? h('span', { class: 'fin-feed__locked' }, t('batches.locked'))
        : h(NButton, { size: 'tiny', type: 'error', quaternary: true, onClick: () => void deleteBatch(r) }, { default: () => t('batches.delete') }),
  },
]
</script>

<style scoped>
.fin-feed__account {
  width: 240px;
  max-width: 60vw;
}

.fin-feed__status {
  margin-left: 8px;
}

.fin-feed__import,
.fin-feed__create-doc,
.fin-feed__candidates {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-feed__file {
  font-size: 13px;
}

.fin-feed__preview {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.fin-feed__preview-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.fin-feed__preview-title {
  font-size: 12px;
  font-weight: 600;
  color: var(--tnzi-text-2, #666);
}

.fin-feed__preview-scroll {
  overflow-x: auto;
  border: 1px solid var(--tnzi-border, #efeff5);
  border-radius: var(--tnzi-admin-radius-md, 4px);
}

.fin-feed__preview-table {
  border-collapse: collapse;
  width: 100%;
  font-size: 12px;
  white-space: nowrap;
}

.fin-feed__preview-table th,
.fin-feed__preview-table td {
  padding: 5px 10px;
  text-align: left;
  border-bottom: 1px solid var(--tnzi-border, #efeff5);
}

.fin-feed__preview-table th {
  font-weight: 600;
  color: var(--tnzi-text-2, #666);
  background: var(--tnzi-layout-bg, #fafafc);
}

.fin-feed__preview-table tr:last-child td {
  border-bottom: none;
}

.fin-feed__import-actions,
.fin-feed__create-doc-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

.fin-feed__empty {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-text-3, #999);
  text-align: center;
}

.fin-feed__locked {
  font-size: 12px;
  color: var(--tnzi-text-3, #999);
}
</style>
