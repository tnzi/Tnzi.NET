<template>
  <TCrudPage
    :state="crud"
    :all-columns="journalEntryColumns"
    :title="title"
    :row-actions="rowActions"
    :translate="t"
    :detail-width="720"
    :detail-title="detailTitle"
  >
    <template #primary>
      <NButton type="primary" tertiary size="small" @click="openCreate">
        <template #icon>
          <TSvgIcon icon="mdi:plus" :size="16" />
        </template>
        {{ t('actions.create') }}
      </NButton>
    </template>

    <!-- Read-only detail — header meta + lines. Same `view` open-state as the
         form modal (deep-linkable ?detail=view:<id>, Back-closeable); the
         engine renders it as a right drawer because this `#detail` slot
         exists. `onView` lazy-loads the full entry (list rows carry no lines). -->
    <template #detail>
      <template v-if="viewed">
        <NDescriptions :column="2" size="small" bordered class="je-detail__meta">
          <NDescriptionsItem :label="t('columns.status')">{{ statusLabel(viewed.status) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.postingDate')">{{ formatDateOnly(viewed.postingDate, { utc: true }) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.currency')">
            {{ viewed.currency }} <template v-if="viewed.exchangeRate !== 1"> @ {{ viewed.exchangeRate }}</template>
          </NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.sourceType')">
            {{ viewed.sourceType ?? '—' }}<template v-if="viewed.sourceId"> / {{ viewed.sourceId }}</template>
          </NDescriptionsItem>
          <NDescriptionsItem :label="`${t('columns.totalDebit')} ${t('baseCurrency')}`">{{ fmtAmount(viewed.totalDebit) }}</NDescriptionsItem>
          <NDescriptionsItem :label="`${t('columns.totalCredit')} ${t('baseCurrency')}`">{{ fmtAmount(viewed.totalCredit) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.memo')" :span="2">{{ viewed.memo ?? '—' }}</NDescriptionsItem>
        </NDescriptions>
        <TResponsiveTable
          class="je-detail__lines"
          :columns="lineColumns"
          :data="viewed.lines"
          :bordered="false"
          size="small"
          mobile="scroll"
          :pagination="false"
        />
      </template>
    </template>
  </TCrudPage>

  <!-- Line editor (create / edit draft) — driven by `useDetail` so it is
       deep-linkable (?entry=new / ?entry=edit:<id>) and rendered by the single
       TDetailHost renderer; the editor supplies its own action row, hence
       :footer="false". Keyed by open sequence so each open starts fresh. -->
  <TDetailHost :state="entryDetail" :title="editorTitle" :width="880" :footer="false" :translate="t">
    <JournalEntryEditor
      :key="editorSeq"
      :entry="editingEntry"
      :account-options="accountOptions"
      :bridge="bridge"
      @saved="onSaved"
      @cancel="entryDetail.close()"
    />
  </TDetailHost>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NButton, NDescriptions, NDescriptionsItem, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateOnly } from '@tnzi/core'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { viewAction, type RowAction } from '../../headless/rowActions'
import { createFinanceBridge, JournalEntryStatus, type AccountTreeDto, type JournalEntryDto, type JournalLineDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { buildJournalEntryColumns, ENTRY_STATUS_META, type JournalRow } from './journal-entry-config'
import { amountCell, fmtAmount } from './money'
import JournalEntryEditor from './components/JournalEntryEditor.vue'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.journals')
const message = useSafeMessage()

const journalEntryColumns = buildJournalEntryColumns(t)

const crud = useCrudPage<JournalRow>({
  pageId: 'finance.journals',
  columns: journalEntryColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.journals.fetch(q),
  // Deep-link cold restore (?detail=view:<id>) hydrates even off-page rows.
  loadDetailById: (id) => bridge.journals.getById(id),
  // Read-only list — create/edit go through the dedicated line-editor overlay.
  onView: (row) => void loadViewed(row),
})

const title = 'tnzi.admin.modules.finance.journals.title'
const detailTitle = (d: JournalRow) => d.number ?? t('detail.draftTitle')

function statusLabel(status?: JournalEntryStatus): string {
  const meta = ENTRY_STATUS_META[status ?? '']
  return meta ? t(meta.label) : String(status ?? '')
}

// ── Read-only detail (full entry with lines) ────────────────────
const viewed = ref<JournalEntryDto | null>(null)

async function loadViewed(row: JournalRow) {
  viewed.value = null
  try {
    viewed.value = await bridge.journals.getById(String(row.id ?? ''))
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const lineColumns: DataTableColumns<JournalLineDto> = [
  { key: 'lineNumber', title: '#', width: 48 },
  {
    key: 'account',
    title: t('form.account'),
    minWidth: 200,
    render: (row) => `${row.accountCode ?? ''} ${row.accountName ?? row.accountId}`.trim(),
  },
  { key: 'memo', title: t('form.lineMemo'), minWidth: 140, render: (row) => row.memo ?? '—' },
  {
    key: 'debit',
    title: `${t('form.debit')} ${t('baseCurrency')}`,
    width: 140,
    render: (row) => amountCell(row.debit > 0 ? fmtAmount(row.debit) : '—'),
  },
  {
    key: 'credit',
    title: `${t('form.credit')} ${t('baseCurrency')}`,
    width: 140,
    render: (row) => amountCell(row.credit > 0 ? fmtAmount(row.credit) : '—'),
  },
]

// ── Line-editor overlay (create / edit draft) ───────────────────
const entryDetail = useDetail<JournalEntryDto>({
  mode: 'modal',
  url: 'entry',
  loadData: (id) => bridge.journals.getById(String(id)),
})

// 'create' opens with an empty object — treat anything without an id as create.
const editingEntry = computed(() => {
  const d = entryDetail.data.value
  return d?.id ? d : null
})
const editorTitle = computed(() => (editingEntry.value ? t('dialog.editTitle') : t('dialog.createTitle')))

// Fresh editor per open (also covers deep-link cold restores).
const editorSeq = ref(0)
watch(
  () => entryDetail.visible.value,
  (open) => {
    if (open) {
      editorSeq.value++
      void ensureAccountOptions()
    }
  },
  { immediate: true },
)

function openCreate() {
  void entryDetail.open('create')
}

function openEdit(row: JournalRow) {
  void entryDetail.open('edit', String(row.id ?? ''))
}

function onSaved() {
  entryDetail.close()
  void crud.refresh()
}

// ── Account options (leaf accounts, loaded on first editor open) ─
const accountOptions = ref<Array<{ label: string; value: string }>>([])
let accountOptionsLoaded = false

function flattenLeaves(nodes: AccountTreeDto[], into: Array<{ label: string; value: string }>) {
  for (const node of nodes) {
    if (!node.isGroup && node.isActive) {
      into.push({ label: `${node.code} ${node.name}`, value: node.id })
    }
    flattenLeaves(node.children ?? [], into)
  }
}

async function ensureAccountOptions() {
  if (accountOptionsLoaded) return
  try {
    const tree = await bridge.accounts.tree(false)
    const options: Array<{ label: string; value: string }> = []
    flattenLeaves(tree, options)
    accountOptions.value = options
    accountOptionsLoaded = true
  } catch {
    accountOptions.value = []
  }
}

// ── Lifecycle actions ───────────────────────────────────────────
async function postEntry(row: JournalRow) {
  try {
    await bridge.journals.post(String(row.id ?? ''))
    message.success(t('postSuccess'))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

async function reverseEntry(row: JournalRow) {
  try {
    await bridge.journals.reverse(String(row.id ?? ''))
    message.success(t('reverseSuccess'))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

async function deleteEntry(row: JournalRow) {
  try {
    await bridge.journals.deleteDraft(String(row.id ?? ''))
    message.success(t('deleteSuccess'))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const isDraft = (row: JournalRow) => row.status === JournalEntryStatus.Draft
const isPosted = (row: JournalRow) => row.status === JournalEntryStatus.Posted

const rowActions: RowAction<JournalRow>[] = [
  viewAction(crud),
  { key: 'edit', type: 'primary', show: isDraft, onClick: openEdit },
  { key: 'post', label: 'actions.post', type: 'primary', show: isDraft, confirm: 'confirmPost', onClick: postEntry },
  { key: 'reverse', label: 'actions.reverse', type: 'warning', show: isPosted, confirm: 'confirmReverse', onClick: reverseEntry },
  { key: 'delete', label: 'actions.delete', type: 'error', show: isDraft, confirm: 'confirmDelete', onClick: deleteEntry },
]
</script>

<style scoped>
.je-detail__meta {
  margin-bottom: 12px;
}

.je-detail__lines {
  margin-top: 4px;
}
</style>
