<template>
  <TTabsPage
    :sections="sections"
    :title="title"
    icon="mdi:checkbook"
    :help="t('help')"
    :translate="t"
    default-section="queue"
  >
    <!-- 队列概览：这一屏的钱现在是什么状态（数据源是全量队列，非当前页）。 -->
    <template #kpis>
      <TKpiRow cols="1 s:2">
        <TKpiCard :label="t('queue.kpiCount')" :value="checkQueueKpis.count" icon="mdi:printer-check" />
        <TKpiCard
          :label="t('queue.kpiTotal')"
          :value="fmtMoney(checkQueueKpis.total, checkQueueKpis.currency)"
          :animated="false"
          icon="mdi:cash-multiple"
        />
      </TKpiRow>
    </template>

    <!-- ── Print queue ─────────────────────────────────────────── -->
    <template #queue>
      <div class="fin-checks__queue">
        <div class="fin-checks__queue-bar">
          <NSelect
            v-model:value="queueAccountId"
            :options="bankAccountOptions"
            :placeholder="t('queue.selectAccount')"
            size="small"
            filterable
            clearable
            class="fin-checks__account"
          />
          <div class="fin-checks__queue-actions">
            <NButton
              v-if="canCreate"
              size="small"
              type="primary"
              :disabled="!queueAccountId || checkedQueueKeys.length === 0"
              @click="openPrint"
            >
              <template #icon><TSvgIcon icon="mdi:printer-outline" :size="16" /></template>
              {{ t('queue.print') }}
            </NButton>
            <NButton size="small" quaternary :disabled="!queueAccountId" :loading="calibrating" @click="downloadCalibration">
              <template #icon><TSvgIcon icon="mdi:ruler-square" :size="16" /></template>
              {{ t('queue.calibration') }}
            </NButton>
          </div>
        </div>
        <TResponsiveTable
          :columns="queueColumns"
          :data="queueRows"
          :row-key="(r: CheckQueueItemDto) => r.paymentEntryId"
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

    <!-- ── Register book ───────────────────────────────────────── -->
    <template #register>
      <TCrudPage
        :state="crud"
        :all-columns="columns"
        :title="registerTitle"
        :search-fields="searchFields"
    :row-actions="rowActions"
        :translate="t"
        :show-header="false"
      >
        <template #primary>
          <NButton v-if="canCreate" size="small" type="primary" @click="openRegister">
            <template #icon><TSvgIcon icon="mdi:pencil-plus-outline" :size="16" /></template>
            {{ t('register.action') }}
          </NButton>
        </template>
        <template #toolbarRight>
          <NButton size="small" quaternary @click="openPositivePay">
            <template #icon><TSvgIcon icon="mdi:bank-check" :size="16" /></template>
            {{ t('positivePay.action') }}
          </NButton>
          <NButton v-if="canUpdate" size="small" quaternary @click="openSpoil">
            <template #icon><TSvgIcon icon="mdi:file-cancel-outline" :size="16" /></template>
            {{ t('spoil.action') }}
          </NButton>
        </template>
      </TCrudPage>
    </template>

    <template #overlays>
      <!-- Print modal (issue date). -->
      <TDetailHost :state="printDetail" :title="t('printModal.title')" :width="420" :footer="false" :translate="t">
        <NForm label-placement="top" size="small" class="fin-checks__form">
          <p class="fin-checks__hint">{{ t('printModal.hint', { count: String(checkedQueueKeys.length) }) }}</p>
          <NFormItem :label="t('printModal.issueDateLabel')" :show-feedback="false">
            <NDatePicker v-model:value="printIssueDate" type="date" clearable :placeholder="t('printModal.issueDate')" class="fin-checks__full" />
          </NFormItem>
          <div class="fin-checks__form-actions">
            <NButton size="small" @click="printDetail.close()">{{ t('common.cancel') }}</NButton>
            <NButton size="small" type="primary" :loading="printing" :disabled="printing" @click="submitPrint">
              {{ t('printModal.submit') }}
            </NButton>
          </div>
        </NForm>
      </TDetailHost>

      <!-- Register manual check. -->
      <TDetailHost :state="registerDetail" :title="t('register.title')" :width="460" :footer="false" :translate="t">
        <NForm label-placement="top" size="small" class="fin-checks__form">
          <NFormItem :label="t('register.account')" :show-feedback="false">
            <NSelect v-model:value="regForm.bankAccountId" :options="bankAccountOptions" :placeholder="t('queue.selectAccount')" filterable />
          </NFormItem>
          <NFormItem :label="t('register.checkNumber')" :show-feedback="false">
            <NInputNumber v-model:value="regForm.checkNumber" :min="1" :placeholder="t('register.checkNumber')" class="fin-checks__full" />
          </NFormItem>
          <NFormItem :label="t('register.payee')" :show-feedback="false">
            <NInput v-model:value="regForm.payeeName" :placeholder="t('register.payee')" />
          </NFormItem>
          <NFormItem :label="t('register.amount')" :show-feedback="false">
            <NInputNumber v-model:value="regForm.amount" :min="0" :placeholder="t('register.amount')" class="fin-checks__full" />
          </NFormItem>
          <NFormItem :label="t('register.currency')" :show-feedback="false">
            <NInput v-model:value="regForm.currency" :placeholder="t('register.currency')" />
          </NFormItem>
          <NFormItem :label="t('register.issueDate')" :show-feedback="false">
            <NDatePicker v-model:value="regForm.issueDate" type="date" :placeholder="t('register.issueDate')" class="fin-checks__full" />
          </NFormItem>
          <div class="fin-checks__form-actions">
            <NButton size="small" @click="registerDetail.close()">{{ t('common.cancel') }}</NButton>
            <NButton size="small" type="primary" :loading="savingReg" :disabled="savingReg || !regForm.bankAccountId || regForm.checkNumber == null" @click="submitRegister">
              {{ t('register.submit') }}
            </NButton>
          </div>
        </NForm>
      </TDetailHost>

      <!-- Spoil check (reserve a damaged number). -->
      <TDetailHost :state="spoilDetail" :title="t('spoil.title')" :width="440" :footer="false" :translate="t">
        <NForm label-placement="top" size="small" class="fin-checks__form">
          <p class="fin-checks__hint">{{ t('spoil.hint') }}</p>
          <NFormItem :label="t('spoil.account')" :show-feedback="false">
            <NSelect v-model:value="spoilForm.bankAccountId" :options="bankAccountOptions" :placeholder="t('queue.selectAccount')" filterable />
          </NFormItem>
          <NFormItem :label="t('spoil.checkNumber')" :show-feedback="false">
            <NInputNumber v-model:value="spoilForm.checkNumber" :min="1" :placeholder="t('spoil.checkNumber')" class="fin-checks__full" />
          </NFormItem>
          <NFormItem :label="t('spoil.reason')" :show-feedback="false">
            <NInput v-model:value="spoilForm.reason" type="textarea" :rows="2" :placeholder="t('spoil.reason')" />
          </NFormItem>
          <div class="fin-checks__form-actions">
            <NButton size="small" @click="spoilDetail.close()">{{ t('common.cancel') }}</NButton>
            <NButton size="small" type="warning" :loading="savingSpoil" :disabled="savingSpoil || !spoilForm.bankAccountId || spoilForm.checkNumber == null" @click="submitSpoil">
              {{ t('spoil.submit') }}
            </NButton>
          </div>
        </NForm>
      </TDetailHost>

      <!-- Void reason. -->
      <TDetailHost :state="voidDetail" :title="t('voidModal.title')" :width="420" :footer="false" :translate="t">
        <NForm label-placement="top" size="small" class="fin-checks__form">
          <NFormItem :label="t('voidModal.reason')" :show-feedback="false">
            <NInput v-model:value="voidReason" type="textarea" :rows="2" :placeholder="t('voidModal.reason')" />
          </NFormItem>
          <div class="fin-checks__form-actions">
            <NButton size="small" @click="voidDetail.close()">{{ t('common.cancel') }}</NButton>
            <NButton size="small" type="warning" :loading="voiding" :disabled="voiding" @click="submitVoid">
              {{ t('voidModal.submit') }}
            </NButton>
          </div>
        </NForm>
      </TDetailHost>

      <!-- Positive-pay CSV export (bank account + issue-date window). -->
      <TModalShell v-model:show="ppShow" :title="t('positivePay.title')" :width="440">
        <NForm label-placement="top" size="small" class="fin-checks__form">
          <p class="fin-checks__hint">{{ t('positivePay.hint') }}</p>
          <NFormItem :label="t('positivePay.account')" :show-feedback="false">
            <NSelect v-model:value="ppForm.bankAccountId" :options="bankAccountOptions" :placeholder="t('queue.selectAccount')" filterable />
          </NFormItem>
          <NFormItem :label="t('positivePay.from')" :show-feedback="false">
            <NDatePicker v-model:value="ppForm.from" type="date" clearable :placeholder="t('positivePay.from')" class="fin-checks__full" />
          </NFormItem>
          <NFormItem :label="t('positivePay.to')" :show-feedback="false">
            <NDatePicker v-model:value="ppForm.to" type="date" clearable :placeholder="t('positivePay.to')" class="fin-checks__full" />
          </NFormItem>
        </NForm>
        <template #footer>
          <NButton size="small" @click="ppShow = false">{{ t('common.cancel') }}</NButton>
          <NButton size="small" type="primary" :loading="exportingPp" :disabled="exportingPp || !ppForm.bankAccountId || ppForm.from == null || ppForm.to == null" @click="submitPositivePay">
            {{ t('positivePay.submit') }}
          </NButton>
        </template>
      </TModalShell>
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NSelect, NInput, NInputNumber, NDatePicker, NForm, NFormItem } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { downloadBlob } from '@tnzi/core'
import TTabsPage from '../../components/layout/TTabsPage.vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TModalShell from '../../components/overlay/TModalShell.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { type RowAction } from '../../headless/rowActions'
import {
  createFinanceBridge,
  CheckStatus,
  type BankAccountDto,
  type CheckQueueItemDto,
} from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { fmtMoney, tsToIsoDate } from './money'
import { buildCheckSearchFields, buildCheckColumns, buildCheckQueueColumns, type BankCheckRow } from './check-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.checks')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const title = 'tnzi.admin.modules.finance.checks.title'
const registerTitle = 'tnzi.admin.modules.finance.checks.register.title'
const columns = buildCheckColumns(t)

// 真实筛选（标准 1）：只声明后端 QueryDto 真的支持的字段。
const searchFields = buildCheckSearchFields(t)
const queueColumns = buildCheckQueueColumns(t)

const sections = [
  // Mixed blocks (filter bar + a plain table), not a single flex-filling
  // table, so the pane must own its scroll - otherwise the fill-height chain
  // shrinks it and the queue is unreachable on a short viewport.
  { name: 'queue', label: t('tabs.queue'), scroll: true },
  { name: 'register', label: t('tabs.register') },
]

// Write operations are custom (print / register / void / spoil), not CRUD
// callbacks, so gate them on the module's permission codes directly
// (fail-open for super-admin / unloaded; the backend [ApiAuthorize] is the wall).
const canCreate = computed(() => can('finance.check.create'))
const canUpdate = computed(() => can('finance.check.update'))

// ── Bank account profile options (shared by queue + modals) ─────
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

// ── Register book (read-only CRUD list + custom row actions) ────
const crud = useCrudPage<BankCheckRow>({
  pageId: 'finance.checks',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.checks.fetch(q),
})

// ── Print queue ─────────────────────────────────────────────────
const queueAccountId = ref<string | null>(null)
const queueRows = ref<CheckQueueItemDto[]>([])
const queueLoading = ref(false)
const checkedQueueKeys = ref<string[]>([])

watch(queueAccountId, () => {
  checkedQueueKeys.value = []
  void loadQueue()
})

/**
 * 队列概览（标准 2「概览区必须回答现在的状态」）。
 *
 * 数据源就是队列本身——它是**全量不分页**的，所以这里的合计是真数字，
 * 不是"当前页加总"那种会被信任然后被发现是错的东西。
 */
const checkQueueKpis = computed(() => ({
  count: queueRows.value.length,
  total: queueRows.value.reduce((sum, r) => sum + (r.amount ?? 0), 0),
  currency: queueRows.value[0]?.currency,
}))

async function loadQueue() {
  if (!queueAccountId.value) {
    queueRows.value = []
    return
  }
  queueLoading.value = true
  try {
    queueRows.value = await bridge.checks.queue(queueAccountId.value)
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    queueLoading.value = false
  }
}

function onCheckedChange(keys: Array<string | number>) {
  checkedQueueKeys.value = keys.map(String)
}

// ── Print ───────────────────────────────────────────────────────
const printDetail = useDetail<{ id: string }>({ mode: 'modal', url: 'print' })
const printIssueDate = ref<number | null>(null)
const printing = ref(false)

function openPrint() {
  if (!queueAccountId.value || checkedQueueKeys.value.length === 0) return
  printIssueDate.value = null
  void printDetail.open('create')
}

async function submitPrint() {
  if (checkedQueueKeys.value.length === 0) return
  printing.value = true
  try {
    const blob = await bridge.checks.print({
      paymentEntryIds: [...checkedQueueKeys.value],
      issueDate: printIssueDate.value != null ? tsToIsoDate(printIssueDate.value) : null,
    })
    downloadBlob(blob, `checks-${Date.now()}.pdf`)
    message.success(t('printModal.success'))
    printDetail.close()
    checkedQueueKeys.value = []
    await loadQueue()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    printing.value = false
  }
}

// ── Calibration ─────────────────────────────────────────────────
const calibrating = ref(false)

async function downloadCalibration() {
  if (!queueAccountId.value) return
  calibrating.value = true
  try {
    const blob = await bridge.checks.calibration(queueAccountId.value)
    downloadBlob(blob, `check-calibration-${queueAccountId.value}.pdf`)
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    calibrating.value = false
  }
}

// ── Positive-pay CSV export (issued-check file for the bank's fraud-control service) ──
const ppShow = ref(false)
const ppForm = reactive<{ bankAccountId: string | null; from: number | null; to: number | null }>({ bankAccountId: null, from: null, to: null })
const exportingPp = ref(false)

function openPositivePay() {
  ppForm.bankAccountId = queueAccountId.value
  ppForm.from = null
  ppForm.to = null
  ppShow.value = true
}

async function submitPositivePay() {
  if (!ppForm.bankAccountId || ppForm.from == null || ppForm.to == null) return
  exportingPp.value = true
  try {
    const blob = await bridge.checks.exportPositivePay(ppForm.bankAccountId, tsToIsoDate(ppForm.from), tsToIsoDate(ppForm.to))
    downloadBlob(blob, `positive-pay-${ppForm.bankAccountId}.csv`)
    ppShow.value = false
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    exportingPp.value = false
  }
}

// ── Register manual check ───────────────────────────────────────
const registerDetail = useDetail<{ id: string }>({ mode: 'modal', url: 'register' })
const regForm = reactive<{
  bankAccountId: string | null
  checkNumber: number | null
  payeeName: string | null
  amount: number | null
  currency: string | null
  issueDate: number | null
}>({ bankAccountId: null, checkNumber: null, payeeName: null, amount: null, currency: null, issueDate: Date.now() })
const savingReg = ref(false)

function openRegister() {
  regForm.bankAccountId = null
  regForm.checkNumber = null
  regForm.payeeName = null
  regForm.amount = null
  regForm.currency = null
  regForm.issueDate = Date.now()
  void registerDetail.open('create')
}

async function submitRegister() {
  if (!regForm.bankAccountId || regForm.checkNumber == null) return
  savingReg.value = true
  try {
    await bridge.checks.register({
      bankAccountId: regForm.bankAccountId,
      checkNumber: Number(regForm.checkNumber),
      payeeName: regForm.payeeName?.trim() || null,
      amount: regForm.amount ?? null,
      currency: regForm.currency?.trim().toUpperCase() || null,
      issueDate: tsToIsoDate(regForm.issueDate ?? Date.now()),
    })
    message.success(t('register.success'))
    registerDetail.close()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    savingReg.value = false
  }
}

// ── Spoil check ─────────────────────────────────────────────────
const spoilDetail = useDetail<{ id: string }>({ mode: 'modal', url: 'spoil' })
const spoilForm = reactive<{ bankAccountId: string | null; checkNumber: number | null; reason: string | null }>({
  bankAccountId: null,
  checkNumber: null,
  reason: null,
})
const savingSpoil = ref(false)

function openSpoil() {
  spoilForm.bankAccountId = null
  spoilForm.checkNumber = null
  spoilForm.reason = null
  void spoilDetail.open('create')
}

async function submitSpoil() {
  if (!spoilForm.bankAccountId || spoilForm.checkNumber == null) return
  savingSpoil.value = true
  try {
    await bridge.checks.spoil({
      bankAccountId: spoilForm.bankAccountId,
      checkNumber: Number(spoilForm.checkNumber),
      reason: spoilForm.reason?.trim() || null,
    })
    message.success(t('spoil.success'))
    spoilDetail.close()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    savingSpoil.value = false
  }
}

// ── Void ────────────────────────────────────────────────────────
const voidDetail = useDetail<{ id: string }>({ mode: 'modal', url: 'void' })
const voidReason = ref<string | null>(null)
const voidTargetId = ref<string | null>(null)
const voiding = ref(false)

function openVoid(row: BankCheckRow) {
  voidTargetId.value = String(row.id ?? '')
  voidReason.value = null
  void voidDetail.open('create')
}

async function submitVoid() {
  if (!voidTargetId.value) return
  voiding.value = true
  try {
    await bridge.checks.voidCheck(voidTargetId.value, { reason: voidReason.value?.trim() || null })
    message.success(t('voidModal.success'))
    voidDetail.close()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    voiding.value = false
  }
}

// ── Reprint (void the original + new check → PDF) ───────────────
async function reprint(row: BankCheckRow) {
  const id = String(row.id ?? '')
  if (!id) return
  try {
    const blob = await bridge.checks.reprint(id)
    downloadBlob(blob, `check-reprint-${id}.pdf`)
    message.success(t('reprintSuccess'))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const isIssued = (r: BankCheckRow) => r.status === CheckStatus.Issued

const rowActions: RowAction<BankCheckRow>[] = [
  {
    key: 'reprint',
    label: 'actions.reprint',
    type: 'primary',
    // Reprint voids the original cheque + consumes a new cheque number (irreversible) - confirm first.
    confirm: 'confirmReprint',
    show: (r) => canCreate.value && isIssued(r) && !!r.paymentEntryId,
    onClick: (r) => void reprint(r),
  },
  {
    key: 'void',
    label: 'actions.void',
    type: 'warning',
    show: (r) => canUpdate.value && isIssued(r),
    onClick: (r) => openVoid(r),
  },
]
</script>

<style scoped>
.fin-checks__queue {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-checks__queue-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.fin-checks__account {
  width: 240px;
  max-width: 60vw;
}

.fin-checks__queue-actions {
  display: flex;
  gap: 8px;
  margin-left: auto;
}

.fin-checks__form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-checks__hint {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-text-3, #999);
}

.fin-checks__full {
  width: 100%;
}

.fin-checks__form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}
</style>
