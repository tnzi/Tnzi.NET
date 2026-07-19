<template>
  <TCrudPage :state="crud" :all-columns="columns" :title="title" :row-actions="rowActions" :translate="t">
    <!-- Workspace controls: account picker + status filter. -->
    <template #toolbarLeft>
      <NSelect
        v-model:value="accountId"
        :options="sources.fundsAccountOptions.value"
        :placeholder="t('workspace.selectAccount')"
        size="small"
        filterable
        clearable
        class="fin-feed__account"
      />
      <NRadioGroup v-model:value="statusFilter" size="small" class="fin-feed__status">
        <NRadioButton :value="BankTransactionStatus.Pending">{{ t('status.pending') }}</NRadioButton>
        <NRadioButton :value="BankTransactionStatus.Matched">{{ t('status.matched') }}</NRadioButton>
        <NRadioButton :value="BankTransactionStatus.Excluded">{{ t('status.excluded') }}</NRadioButton>
      </NRadioGroup>
    </template>

    <!-- Workspace actions. -->
    <template #primary>
      <NButton v-if="canImport" size="small" type="primary" :disabled="!accountId" @click="openImport">
        <template #icon><TSvgIcon icon="mdi:file-upload-outline" :size="16" /></template>
        {{ t('workspace.import') }}
      </NButton>
      <NButton v-if="canImport" size="small" :disabled="!accountId || pulling" :loading="pulling" @click="runPull">
        <template #icon><TSvgIcon icon="mdi:cloud-download-outline" :size="16" /></template>
        {{ t('workspace.pull') }}
      </NButton>
      <NButton v-if="canMatch" size="small" :disabled="!accountId" :loading="suggesting" @click="runSuggest">
        <template #icon><TSvgIcon icon="mdi:auto-fix" :size="16" /></template>
        {{ t('workspace.suggest') }}
      </NButton>
      <NButton size="small" quaternary :disabled="!accountId" @click="openBatches">
        <template #icon><TSvgIcon icon="mdi:format-list-bulleted" :size="16" /></template>
        {{ t('workspace.batches') }}
      </NButton>
    </template>
  </TCrudPage>

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

  <!-- Candidate picker (Pending rows with multiple matches). -->
  <TDetailHost :state="candidateDetail" :title="t('candidates.title')" :width="640" :footer="false" :translate="t">
    <div class="fin-feed__candidates">
      <TResponsiveTable
        :columns="candidateColumns"
        :data="candidates"
        :row-key="(r: BankMatchCandidateDto) => r.journalLineId"
        size="small"
        mobile="scroll"
        :pagination="false"
        :bordered="false"
      />
      <p v-if="candidates.length === 0" class="fin-feed__empty">{{ t('candidates.empty') }}</p>
    </div>
  </TDetailHost>

  <!-- Create document from a transaction. -->
  <TDetailHost :state="createDocDetail" :title="t('createDoc.title')" :width="480" :footer="false" :translate="t">
    <NForm label-placement="top" size="small" class="fin-feed__create-doc">
      <NFormItem :label="t('createDoc.docType')" :show-feedback="false">
        <NRadioGroup v-model:value="docType" size="small">
          <NRadioButton :value="BankFeedDocType.Expense">{{ t('createDoc.expense') }}</NRadioButton>
          <NRadioButton :value="BankFeedDocType.PaymentEntry">{{ t('createDoc.payment') }}</NRadioButton>
          <NRadioButton :value="BankFeedDocType.Transfer">{{ t('createDoc.transfer') }}</NRadioButton>
        </NRadioGroup>
      </NFormItem>
      <NFormItem
        v-if="docType === BankFeedDocType.Expense || docType === BankFeedDocType.Transfer"
        :label="t('createDoc.counterAccount')"
        :show-feedback="false"
      >
        <NSelect
          v-model:value="counterAccountId"
          :options="docType === BankFeedDocType.Transfer ? sources.fundsAccountOptions.value : sources.leafAccountOptions.value"
          :placeholder="t('createDoc.counterAccount')"
          filterable
          clearable
        />
      </NFormItem>
      <NFormItem v-if="docType === BankFeedDocType.PaymentEntry" :label="t('createDoc.party')" :show-feedback="false">
        <NSelect
          v-model:value="partyId"
          :options="partyOptions"
          :placeholder="t('createDoc.party')"
          filterable
          clearable
        />
      </NFormItem>
      <NFormItem
        v-if="docType === BankFeedDocType.Expense || docType === BankFeedDocType.PaymentEntry"
        :label="t('createDoc.method')"
        :show-feedback="false"
      >
        <NSelect
          v-model:value="paymentMethod"
          :options="methodOptions"
          :placeholder="t('createDoc.method')"
          clearable
          tag
          filterable
        />
      </NFormItem>
      <div class="fin-feed__create-doc-actions">
        <NButton size="small" @click="createDocDetail.close()">{{ t('createDoc.cancel') }}</NButton>
        <NButton size="small" type="primary" :loading="creatingDoc" :disabled="creatingDoc" @click="submitCreateDoc">
          {{ t('createDoc.submit') }}
        </NButton>
      </div>
    </NForm>
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
import { computed, ref, watch, h } from 'vue'
import { NButton, NSelect, NRadioGroup, NRadioButton, NForm, NFormItem, useDialog, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateOnly } from '@tnzi/core'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { type RowAction } from '../../headless/rowActions'
import {
  createFinanceBridge,
  BankTransactionStatus,
  BankTransactionSource,
  BankFeedDocType,
  PAYMENT_METHODS,
  type BankMatchCandidateDto,
  type BankImportBatchDto,
  type CsvMappingDto,
} from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { createFinanceOptionSources } from './options'
import { amountCell, fmtMoney, tsToIsoDate } from './money'
import { buildBankTransactionColumns, csvParseFormSchema, csvColumnFormSchema, type BankTransactionRow } from './bank-feed-config'
import { peekCsv, guessColumns, type CsvPeek } from './csv-preview'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.bankFeed')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

// Workspace write operations are custom (import / match), not CRUD callbacks,
// so gate them on the module's permission codes directly (fail-open for
// super-admin / unloaded; the backend [ApiAuthorize] is the real wall).
const canImport = computed(() => can('finance.bankFeed.create'))
const canMatch = computed(() => can('finance.bankFeed.update'))
// Create-document delegates to the document workflow (backend gate = finance.document.create).
const canCreateDoc = computed(() => can('finance.document.create'))

const columns = buildBankTransactionColumns(t)
const title = 'tnzi.admin.modules.finance.bankFeed.title'

const accountId = ref<string | null>(null)
const statusFilter = ref<BankTransactionStatus>(BankTransactionStatus.Pending)

const crud = useCrudPage<BankTransactionRow>({
  pageId: 'finance.bankFeed',
  permission: 'finance.bankFeed',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  // Wait for an account before the first fetch (workspace flow).
  autoLoad: false,
  fetchData: (q) => bridge.bankFeed.transactions(q),
})

// Account / status changes drive the transaction query (setFilters does not
// auto-refresh, so trigger it explicitly).
watch([accountId, statusFilter], () => {
  if (!accountId.value) return
  crud.setFilters({ accountId: accountId.value, status: statusFilter.value })
  void crud.refresh()
})

void sources.ensureFundsAccounts()
void sources.ensureLeafAccounts()
void sources.ensureCustomers()
void sources.ensureVendors()

async function refreshList() {
  if (accountId.value) await crud.refresh()
}

// ── Import ──────────────────────────────────────────────────────
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
/** A mapping restored from localStorage is the user's own prior correction - do not overwrite it with a guess. */
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

// ── Pull / Suggest ──────────────────────────────────────────────
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

// ── Confirm / candidates ────────────────────────────────────────
const dialog = safeDialog()
const candidateDetail = useDetail<{ id: string }>({ mode: 'modal', url: 'candidates' })
const candidates = ref<BankMatchCandidateDto[]>([])
const candidateTxnId = ref<string | null>(null)

function safeDialog() {
  try {
    return useDialog()
  } catch {
    return null
  }
}

async function openCandidates(row: BankTransactionRow) {
  candidateTxnId.value = String(row.id ?? '')
  candidates.value = []
  void candidateDetail.open('create')
  try {
    candidates.value = await bridge.bankFeed.candidates(String(row.id ?? ''))
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const candidateColumns: DataTableColumns<BankMatchCandidateDto> = [
  { key: 'postingDate', title: t('candidates.date'), width: 110, render: (r) => formatDateOnly(r.postingDate, { utc: true }) },
  { key: 'entryNumber', title: t('candidates.entry'), width: 120, render: (r) => r.entryNumber ?? '—' },
  { key: 'memo', title: t('candidates.memo'), minWidth: 160, render: (r) => r.memo ?? '—' },
  { key: 'amount', title: t('candidates.amount'), width: 120, render: (r) => amountCell(fmtMoney(r.amount)) },
  {
    key: 'pick',
    title: '',
    width: 90,
    render: (r) => h(NButton, { size: 'tiny', type: 'primary', onClick: () => void pickCandidate(r) }, { default: () => t('candidates.pick') }),
  },
]

async function pickCandidate(candidate: BankMatchCandidateDto) {
  if (!candidateTxnId.value) return
  await confirmMatch(candidateTxnId.value, candidate.journalLineId)
  candidateDetail.close()
}

/** Confirm a match; when the account has no draft reconciliation the backend
 *  400s - guide the user to create one and retry. */
async function confirmMatch(txnId: string, journalLineId?: string) {
  try {
    await bridge.bankFeed.confirm(txnId, { journalLineId: journalLineId ?? null })
    message.success(t('confirmSuccess'))
    await refreshList()
  } catch (error) {
    const msg = error instanceof Error ? error.message : String(error)
    if (/reconcil|draft/i.test(msg) && accountId.value) {
      promptCreateDraft(txnId, journalLineId)
      return
    }
    message.error(msg)
  }
}

function promptCreateDraft(txnId: string, journalLineId?: string) {
  const run = async () => {
    if (!accountId.value) return
    try {
      await bridge.reconciliations.create({
        accountId: accountId.value,
        statementDate: tsToIsoDate(Date.now()),
        statementEndingBalance: 0,
      })
      await bridge.bankFeed.confirm(txnId, { journalLineId: journalLineId ?? null })
      message.success(t('confirmSuccess'))
      await refreshList()
    } catch (err) {
      message.error(err instanceof Error ? err.message : String(err))
    }
  }
  if (dialog) {
    dialog.warning({
      title: t('noDraft.title'),
      content: t('noDraft.content'),
      positiveText: t('noDraft.create'),
      negativeText: t('noDraft.cancel'),
      positiveButtonProps: { type: 'primary' },
      onPositiveClick: () => void run(),
    })
  } else {
    void run()
  }
}

async function runRowAction(action: () => Promise<unknown>, successKey: string) {
  try {
    await action()
    message.success(t(successKey))
    await refreshList()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

// ── Create document ─────────────────────────────────────────────
const createDocDetail = useDetail<{ id: string }>({ mode: 'modal', url: 'createDoc' })
const docType = ref<BankFeedDocType>(BankFeedDocType.Expense)
const counterAccountId = ref<string | null>(null)
const partyId = ref<string | null>(null)
const paymentMethod = ref<string | null>(null)
const creatingDoc = ref(false)
const createDocTxn = ref<BankTransactionRow | null>(null)

const methodOptions = PAYMENT_METHODS.map((m) => ({ label: m, value: m }))

// Deposit (positive) → customer; withdrawal → vendor. The backend infers the
// party type from the transaction sign, so only the correct list is offered.
const partyOptions = computed(() =>
  (createDocTxn.value?.amount ?? 0) >= 0 ? sources.customerOptions.value : sources.vendorOptions.value,
)

function openCreateDoc(row: BankTransactionRow) {
  createDocTxn.value = row
  docType.value = (row.amount ?? 0) >= 0 ? BankFeedDocType.PaymentEntry : BankFeedDocType.Expense
  counterAccountId.value = null
  partyId.value = null
  paymentMethod.value = null
  void createDocDetail.open('create')
}

async function submitCreateDoc() {
  const id = createDocTxn.value?.id
  if (!id) return
  creatingDoc.value = true
  try {
    await bridge.bankFeed.createDocument(String(id), {
      docType: docType.value,
      counterAccountId: docType.value === BankFeedDocType.PaymentEntry ? null : counterAccountId.value,
      partyId: docType.value === BankFeedDocType.PaymentEntry ? partyId.value : null,
      paymentMethod: docType.value === BankFeedDocType.Transfer ? null : paymentMethod.value,
    })
    message.success(t('createDoc.success'))
    createDocDetail.close()
    await refreshList()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    creatingDoc.value = false
  }
}

// ── Batches ─────────────────────────────────────────────────────
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
  { key: 'creationTime', title: t('batches.imported'), width: 120, render: (r) => formatDateOnly(r.creationTime, { utc: true }) },
  { key: 'source', title: t('batches.source'), width: 90, render: (r) => String(r.source) },
  { key: 'fileName', title: t('batches.fileName'), minWidth: 160, render: (r) => r.fileName ?? '—' },
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

// ── Row actions (conditional on status) ─────────────────────────
const isPending = (r: BankTransactionRow) => r.status === BankTransactionStatus.Pending
const isMatched = (r: BankTransactionRow) => r.status === BankTransactionStatus.Matched
const isExcluded = (r: BankTransactionRow) => r.status === BankTransactionStatus.Excluded

const rowActions: RowAction<BankTransactionRow>[] = [
  {
    key: 'confirm',
    label: 'actions.confirm',
    type: 'primary',
    show: (r) => canMatch.value && isPending(r) && !!r.suggestedJournalLineId,
    onClick: (r) => void confirmMatch(String(r.id ?? '')),
  },
  {
    key: 'candidates',
    label: 'actions.candidates',
    show: (r) => canMatch.value && isPending(r),
    onClick: (r) => void openCandidates(r),
  },
  {
    key: 'createDoc',
    label: 'actions.createDoc',
    show: (r) => canCreateDoc.value && isPending(r),
    onClick: (r) => openCreateDoc(r),
  },
  {
    key: 'exclude',
    label: 'actions.exclude',
    type: 'warning',
    show: (r) => canMatch.value && isPending(r),
    confirm: 'confirmExclude',
    onClick: (r) => void runRowAction(() => bridge.bankFeed.exclude(String(r.id ?? '')), 'excludeSuccess'),
  },
  {
    key: 'unmatch',
    label: 'actions.unmatch',
    type: 'warning',
    show: (r) => canMatch.value && isMatched(r),
    confirm: 'confirmUnmatch',
    onClick: (r) => void runRowAction(() => bridge.bankFeed.unmatch(String(r.id ?? '')), 'unmatchSuccess'),
  },
  {
    key: 'restore',
    label: 'actions.restore',
    show: (r) => canMatch.value && isExcluded(r),
    onClick: (r) => void runRowAction(() => bridge.bankFeed.restore(String(r.id ?? '')), 'restoreSuccess'),
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
