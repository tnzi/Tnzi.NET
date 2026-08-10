<template>
  <TTabsPage
    :sections="sections"
    :title="title"
    icon="mdi:bank-transfer-out"
    :help="t('help')"
    :translate="t"
    default-section="queue"
  >
    <!-- ── Payable queue ───────────────────────────────────────── -->
    <!-- 队列概览（标准 2）：数据源是全量队列，不是当前页合计。 -->
    <template #kpis>
      <TKpiRow cols="1 s:2">
        <TKpiCard :label="t('queue.kpiCount')" :value="eftQueueKpis.count" icon="mdi:bank-transfer" />
        <TKpiCard
          :label="t('queue.kpiTotal')"
          :value="fmtMoney(eftQueueKpis.total, eftQueueKpis.currency)"
          :animated="false"
          icon="mdi:cash-multiple"
        />
      </TKpiRow>
    </template>

    <template #queue>
      <div class="fin-eft__queue">
        <div class="fin-eft__queue-bar">
          <span class="fin-eft__hint">{{ t('queue.hint') }}</span>
          <NButton
            v-if="canCreate"
            size="small"
            type="primary"
            :disabled="checkedQueueKeys.length === 0"
            class="fin-eft__queue-create"
            @click="openCreate"
          >
            <template #icon><TSvgIcon icon="mdi:playlist-plus" :size="16" /></template>
            {{ t('queue.create', { count: String(checkedQueueKeys.length) }) }}
          </NButton>
        </div>
        <TResponsiveTable
          :columns="queueColumns"
          :data="queueRows"
          :row-key="(r: EftQueueItemDto) => r.paymentEntryId"
          :checked-row-keys="checkedQueueKeys"
          :loading="queueLoading"
          size="small"
          mobile="scroll"
          :pagination="false"
          :bordered="false"
          :empty-text="t('queue.empty')"
          @update:checked-row-keys="onCheckedChange"
        />
      </div>
    </template>

    <!-- ── Batches ─────────────────────────────────────────────── -->
    <template #batches>
      <TCrudPage
        :state="crud"
        :all-columns="columns"
        :title="batchesTitle"
        :search-fields="searchFields"
    :row-actions="rowActions"
        :translate="t"
        :show-header="false"
      />
    </template>

    <template #overlays>
      <!-- Create batch. -->
      <TDetailHost :state="createDetail" :title="t('create.title')" :width="460" :footer="false" :translate="t">
        <NForm label-placement="top" size="small" class="fin-eft__form">
          <p class="fin-eft__hint">{{ t('create.hint', { count: String(checkedQueueKeys.length) }) }}</p>
          <NFormItem :label="t('create.account')" :show-feedback="false">
            <NSelect v-model:value="createForm.bankAccountId" :options="bankAccountOptions" :placeholder="t('create.account')" filterable />
          </NFormItem>
          <NFormItem :label="t('create.format')" :show-feedback="false">
            <NSelect v-model:value="createForm.format" :options="formatOptions" :placeholder="t('create.format')" />
          </NFormItem>
          <NFormItem :label="t('create.effectiveDate')" :show-feedback="false">
            <NDatePicker v-model:value="createForm.effectiveDate" type="date" :placeholder="t('create.effectiveDate')" class="fin-eft__full" />
          </NFormItem>
          <div class="fin-eft__form-actions">
            <NButton size="small" @click="createDetail.close()">{{ t('common.cancel') }}</NButton>
            <NButton size="small" type="primary" :loading="creating" :disabled="creating || !createForm.bankAccountId || createForm.format == null" @click="submitCreate">
              {{ t('create.submit') }}
            </NButton>
          </div>
        </NForm>
      </TDetailHost>

      <!-- Batch detail (header + lines). -->
      <TDetailHost :state="detail" :title="t('detail.title')" :width="720" :footer="false" :translate="t">
        <div v-if="detail.data.value" class="fin-eft__detail">
          <NDescriptions :column="2" size="small" label-placement="left" bordered>
            <NDescriptionsItem :label="t('detail.number')">{{ detail.data.value.number ?? t('draftLabel') }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.account')">{{ detail.data.value.bankAccountName ?? EMPTY_DASH }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.format')">{{ t(formatLabel(detail.data.value.format)) }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.currency')">{{ detail.data.value.currency }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.effectiveDate')">{{ fmtDate(detail.data.value.effectiveDate) }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.fileCreationNumber')">{{ detail.data.value.fileCreationNumber ?? EMPTY_DASH }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.count')">{{ detail.data.value.totalCount }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.amount')">{{ fmtMoney(detail.data.value.totalAmount, detail.data.value.currency) }}</NDescriptionsItem>
          </NDescriptions>
          <TResponsiveTable
            :columns="lineColumns"
            :data="detail.data.value.lines"
            :row-key="(r: EftBatchLineDto) => r.id"
            size="small"
            mobile="scroll"
            :pagination="false"
            :bordered="false"
            :empty-text="t('detail.noLines')"
          />
        </div>
      </TDetailHost>

      <!-- Void reason. -->
      <TDetailHost :state="voidDetail" :title="t('voidModal.title')" :width="420" :footer="false" :translate="t">
        <NForm label-placement="top" size="small" class="fin-eft__form">
          <!--
            文件交出去过就必须显式确认才能作废：作废会把批内付款放回待付队列，
            已提交给银行的批次这么一放就会被付第二次。后端同样拦（409），这里只是
            让操作者在点下去之前就看见后果，而不是先吃一个错误再猜怎么办。
          -->
          <NAlert v-if="voidHandedOutAt" type="warning" :bordered="false" class="fin-eft__void-warning">
            {{ t('voidModal.handedOutWarning', { time: voidHandedOutAt, count: voidDownloadCount }) }}
          </NAlert>
          <NFormItem :label="t('voidModal.reason')" :show-feedback="false">
            <NInput v-model:value="voidReason" type="textarea" :rows="2" :placeholder="t('voidModal.reason')" />
          </NFormItem>
          <NFormItem v-if="voidHandedOutAt" :show-label="false" :show-feedback="false">
            <NCheckbox v-model:checked="voidAcknowledged">{{ t('voidModal.acknowledge') }}</NCheckbox>
          </NFormItem>
          <div class="fin-eft__form-actions">
            <NButton size="small" @click="voidDetail.close()">{{ t('common.cancel') }}</NButton>
            <NButton
              size="small"
              type="warning"
              :loading="voiding"
              :disabled="voiding || (!!voidHandedOutAt && !voidAcknowledged)"
              @click="submitVoid"
            >
              {{ t('voidModal.submit') }}
            </NButton>
          </div>
        </NForm>
      </TDetailHost>
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, reactive, ref } from 'vue'
import { NAlert, NButton, NCheckbox, NSelect, NInput, NDatePicker, NDescriptions, NDescriptionsItem, NForm, NFormItem } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { downloadBlob, formatDateTime } from '@tnzi/core'
import TTabsPage from '../../components/layout/TTabsPage.vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { type RowAction } from '../../headless/row-actions'
import {
  createFinanceBridge,
  EftBatchStatus,
  EftFileFormat,
  type BankAccountDto,
  type EftBatchDto,
  type EftBatchLineDto,
  type EftQueueItemDto,
} from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { fmtMoney, tsToIsoDate, fmtDate } from './money'
import { buildEftSearchFields, buildEftBatchColumns, buildEftQueueColumns, buildEftLineColumns, EFT_FORMAT_LABEL, type EftBatchRow } from './eft-batch-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.eftBatches')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const title = 'tnzi.admin.modules.finance.eftBatches.title'
const batchesTitle = 'tnzi.admin.modules.finance.eftBatches.batchesTitle'
const columns = buildEftBatchColumns(t)

// 真实筛选（标准 1）：只声明后端 QueryDto 真的支持的字段。
const searchFields = buildEftSearchFields(t)
const queueColumns = buildEftQueueColumns(t)
const lineColumns = buildEftLineColumns(t)

const sections = [
  // Mixed blocks (filter bar + a plain table): the pane owns its scroll.
  { name: 'queue', label: t('tabs.queue'), scroll: true },
  { name: 'batches', label: t('tabs.batches') },
]

const formatOptions = [
  { label: t('format.nacha'), value: EftFileFormat.Nacha },
  { label: t('format.cpa005'), value: EftFileFormat.Cpa005 },
]

const canCreate = computed(() => can('finance.eft.create'))
const canUpdate = computed(() => can('finance.eft.update'))
const canDownload = computed(() => can('finance.eft.download'))

function formatLabel(fmt?: EftFileFormat | null): string {
  return fmt ? (EFT_FORMAT_LABEL[String(fmt)] ?? '') : ''
}
// ── Bank account profile options (originating account) ──────────
const bankAccountOptions = ref<{ label: string; value: string }[]>([])

async function loadBankAccounts() {
  try {
    const page = await bridge.bankAccounts.fetch({ pageIndex: 1, pageSize: 100 })
    bankAccountOptions.value = page.items.map((a: BankAccountDto) => ({ label: a.name, value: a.id }))
  } catch {
    bankAccountOptions.value = []
  }
}
void loadBankAccounts()

// ── Batches list ────────────────────────────────────────────────
const crud = useCrudPage<EftBatchRow>({
  pageId: 'finance.eftBatches',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.eftBatches.fetch(q),
})

// ── Payable queue ───────────────────────────────────────────────
const queueRows = ref<EftQueueItemDto[]>([])
const queueLoading = ref(false)
const checkedQueueKeys = ref<string[]>([])

/**
 * 队列概览（标准 2）：数据源是**全量队列**（不分页），所以合计是真数字。
 */
const eftQueueKpis = computed(() => ({
  count: queueRows.value.length,
  total: queueRows.value.reduce((sum, r) => sum + (r.amount ?? 0), 0),
  currency: queueRows.value[0]?.currency,
}))

async function loadQueue() {
  queueLoading.value = true
  try {
    queueRows.value = await bridge.eftBatches.queue()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    queueLoading.value = false
  }
}
void loadQueue()

function onCheckedChange(keys: Array<string | number>) {
  checkedQueueKeys.value = keys.map(String)
}

// ── Create batch ────────────────────────────────────────────────
const createDetail = useDetail<{ id: string }>({ mode: 'modal', url: 'create' })
const createForm = reactive<{ bankAccountId: string | null; format: EftFileFormat | null; effectiveDate: number | null }>({
  bankAccountId: null,
  format: EftFileFormat.Nacha,
  effectiveDate: Date.now(),
})
const creating = ref(false)

function openCreate() {
  if (checkedQueueKeys.value.length === 0) return
  createForm.bankAccountId = null
  createForm.format = EftFileFormat.Nacha
  createForm.effectiveDate = Date.now()
  void createDetail.open('create')
}

async function submitCreate() {
  if (!createForm.bankAccountId || createForm.format == null) return
  creating.value = true
  try {
    await bridge.eftBatches.create({
      bankAccountId: createForm.bankAccountId,
      format: createForm.format,
      effectiveDate: tsToIsoDate(createForm.effectiveDate ?? Date.now()),
      paymentEntryIds: [...checkedQueueKeys.value],
    })
    message.success(t('create.success'))
    createDetail.close()
    checkedQueueKeys.value = []
    await loadQueue()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    creating.value = false
  }
}

// ── Detail drawer ───────────────────────────────────────────────
const detail = useDetail<EftBatchDto>({ mode: 'drawer', url: 'detail', loadData: (id) => bridge.eftBatches.getById(String(id)) })

// ── Generate / download / void ──────────────────────────────────
async function generate(row: EftBatchRow) {
  const id = String(row.id ?? '')
  if (!id) return
  try {
    await bridge.eftBatches.generate(id)
    message.success(t('generateSuccess'))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

async function download(row: EftBatchRow) {
  const id = String(row.id ?? '')
  if (!id) return
  try {
    const blob = await bridge.eftBatches.download(id)
    downloadBlob(blob, `${row.number ?? 'eft-batch'}.txt`)
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const voidDetail = useDetail<{ id: string }>({ mode: 'modal', url: 'void' })
const voidReason = ref<string | null>(null)
const voidTargetId = ref<string | null>(null)
const voiding = ref(false)
/** 该批次文件首次被交出去的时间，非空 = 作废前必须显式确认。 */
const voidHandedOutAt = ref<string | null>(null)
const voidDownloadCount = ref(0)
const voidAcknowledged = ref(false)

function openVoid(row: EftBatchRow) {
  voidTargetId.value = String(row.id ?? '')
  voidReason.value = null
  voidHandedOutAt.value = row.firstDownloadedTime ? formatDateTime(row.firstDownloadedTime) : null
  voidDownloadCount.value = row.downloadCount ?? 0
  voidAcknowledged.value = false
  void voidDetail.open('create')
}

async function submitVoid() {
  if (!voidTargetId.value) return
  voiding.value = true
  try {
    await bridge.eftBatches.voidBatch(voidTargetId.value, {
      reason: voidReason.value?.trim() || null,
      acknowledgeFileNotSubmitted: voidAcknowledged.value,
    })
    message.success(t('voidModal.success'))
    voidDetail.close()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    voiding.value = false
  }
}

const isDraft = (r: EftBatchRow) => r.status === EftBatchStatus.Draft
const isGenerated = (r: EftBatchRow) => r.status === EftBatchStatus.Generated

const rowActions: RowAction<EftBatchRow>[] = [
  {
    key: 'detail',
    label: 'actions.detail',
    onClick: (r) => void detail.open('view', String(r.id ?? '')),
  },
  {
    key: 'generate',
    label: 'actions.generate',
    type: 'primary',
    show: (r) => canUpdate.value && isDraft(r),
    onClick: (r) => void generate(r),
  },
  {
    key: 'download',
    label: 'actions.download',
    show: (r) => canDownload.value && isGenerated(r),
    onClick: (r) => void download(r),
  },
  {
    key: 'void',
    label: 'actions.void',
    type: 'warning',
    show: (r) => canUpdate.value && (isDraft(r) || isGenerated(r)),
    onClick: (r) => openVoid(r),
  },
]
</script>

<style scoped>
.fin-eft__queue {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-eft__queue-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.fin-eft__queue-create {
  margin-left: auto;
}

.fin-eft__detail {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.fin-eft__form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-eft__hint {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-text-3, #999);
}

.fin-eft__full {
  width: 100%;
}

.fin-eft__form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}
</style>
