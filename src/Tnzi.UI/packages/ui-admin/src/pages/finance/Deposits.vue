<template>
  <TTabsPage
    :sections="sections"
    :title="title"
    icon="mdi:bank-plus"
    :help="t('help')"
    :translate="t"
    default-section="queue"
  >
    <!-- ── Undeposited receipts ────────────────────────────────── -->
    <!-- 队列概览（标准 2）：数据源是当前币种的全量候选清单，不是当前页合计。 -->
    <template #kpis>
      <TKpiRow cols="1 s:2">
        <TKpiCard :label="t('queue.kpiCount')" :value="queueKpis.count" icon="mdi:receipt-text-check" />
        <TKpiCard
          :label="t('queue.kpiTotal')"
          :value="fmtMoney(queueKpis.total, queueKpis.currency)"
          :animated="false"
          icon="mdi:cash-multiple"
        />
      </TKpiRow>
    </template>

    <template #queue>
      <div class="fin-dep__queue">
        <div class="fin-dep__queue-bar">
          <NSelect
            v-model:value="sourceAccountId"
            :options="sources.fundsAccountOptions.value"
            :placeholder="t('queue.sourceAccount')"
            class="fin-dep__account-select"
            filterable
            size="small"
            @update:value="onSourceAccountChange"
          />
          <!-- 一张存款单只能是一种币，所以候选清单也按币种分开看：混币合计是一个
               贴着某个币种标签的假数字，而混币勾选到过账那一刻才会被后端拒绝。 -->
          <NSelect
            v-if="queueCurrencies.length > 1"
            v-model:value="queueCurrency"
            :options="currencyOptions"
            :placeholder="t('queue.currency')"
            class="fin-dep__currency-select"
            size="small"
            @update:value="onCurrencyChange"
          />
          <span class="fin-dep__hint">{{ t('queue.hint') }}</span>
          <NButton
            v-if="canCreate"
            size="small"
            type="primary"
            :disabled="!sourceAccountId"
            class="fin-dep__queue-create"
            @click="openCreate"
          >
            <template #icon><TSvgIcon icon="mdi:playlist-plus" :size="16" /></template>
            {{ checkedQueueKeys.length > 0 ? t('queue.create', { count: String(checkedQueueKeys.length) }) : t('queue.createEmpty') }}
          </NButton>
        </div>
        <TResponsiveTable
          :columns="queueColumns"
          :data="queueRows"
          :row-key="(r: UndepositedReceiptDto) => r.paymentEntryId"
          :checked-row-keys="checkedQueueKeys"
          :loading="queueLoading"
          size="small"
          mobile="scroll"
          :pagination="false"
          :bordered="false"
          :empty-text="sourceAccountId ? t('queue.empty') : t('queue.pickAccount')"
          @update:checked-row-keys="onCheckedChange"
        />
      </div>
    </template>

    <!-- ── Deposits ────────────────────────────────────────────── -->
    <template #deposits>
      <TCrudPage
        :state="crud"
        :all-columns="columns"
        :title="depositsTitle"
        :search-fields="searchFields"
        :row-actions="rowActions"
        :translate="t"
        :show-header="false"
      />
    </template>

    <template #overlays>
      <!-- Record deposit: destination + date; the selected receipts and the
           other-funds lines below are what the deposit is made of. -->
      <TDetailHost :state="createDetail" :title="t('create.title')" :width="620" :footer="false" :translate="t">
        <NForm label-placement="top" size="small" class="fin-dep__form">
          <p class="fin-dep__hint">
            {{ createHint }}
          </p>
          <div class="fin-dep__form-grid">
            <NFormItem :label="t('create.toAccount')" :show-feedback="false">
              <NSelect
                v-model:value="createForm.toAccountId"
                :options="destinationOptions"
                :placeholder="t('create.toAccount')"
                filterable
              />
            </NFormItem>
            <NFormItem :label="t('create.date')" :show-feedback="false">
              <NDatePicker v-model:value="createForm.depositDate" type="date" :placeholder="t('create.date')" class="fin-dep__full" />
            </NFormItem>
          </div>
          <NFormItem :label="t('create.reference')" :show-feedback="false">
            <NInput v-model:value="createForm.reference" :placeholder="t('create.reference')" />
          </NFormItem>
          <NFormItem :label="t('create.memo')" :show-feedback="false">
            <NInput v-model:value="createForm.memo" type="textarea" :rows="2" :placeholder="t('create.memo')" />
          </NFormItem>

          <!-- 「其它款项」行：不来自任何收款单的钱（银行利息、供应商退款、股东投入）。
               后端允许一张只由这类行组成的存款单，所以这里不要求先勾收款。 -->
          <div class="fin-dep__funds">
            <div class="fin-dep__funds-head">
              <span class="fin-dep__funds-title">{{ t('create.otherFunds') }}</span>
              <NButton size="tiny" @click="addFundsLine">
                <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
                {{ t('create.addFunds') }}
              </NButton>
            </div>
            <p class="fin-dep__hint">{{ t('create.otherFundsHint') }}</p>
            <p v-if="incompleteFundsLines.length > 0" class="fin-dep__warn">{{ t('create.fundsIncomplete') }}</p>
            <!-- 行重复且无可见表头，所以每个控件自带可访问名；
                 删除键只有图标，`title` 给鼠标提示、`aria-label` 给读屏。 -->
            <div v-for="line in otherFunds" :key="line.key" class="fin-dep__funds-row">
              <NSelect
                v-model:value="line.accountId"
                :options="sources.leafAccountOptions.value"
                :placeholder="t('create.fundsAccount')"
                :aria-label="t('create.fundsAccount')"
                class="fin-dep__funds-account"
                filterable
                size="small"
              />
              <NInputNumber
                v-model:value="line.amount"
                :placeholder="t('create.fundsAmount')"
                :aria-label="t('create.fundsAmount')"
                :min="0"
                :show-button="false"
                class="fin-dep__funds-amount"
                size="small"
              />
              <NInput
                v-model:value="line.description"
                :placeholder="t('create.fundsDescription')"
                :aria-label="t('create.fundsDescription')"
                class="fin-dep__funds-desc"
                size="small"
              />
              <NButton
                size="small"
                quaternary
                :title="t('create.removeFunds')"
                :aria-label="t('create.removeFunds')"
                @click="removeFundsLine(line.key)"
              >
                <template #icon><TSvgIcon icon="mdi:close" :size="14" /></template>
              </NButton>
            </div>
          </div>

          <div class="fin-dep__form-actions">
            <NButton size="small" @click="createDetail.close()">{{ t('common.cancel') }}</NButton>
            <NButton size="small" type="primary" :loading="creating" :disabled="creating || !canSubmit" @click="submitCreate">
              {{ t('create.submit') }}
            </NButton>
          </div>
        </NForm>
      </TDetailHost>

      <!-- Deposit detail (header + lines). -->
      <TDetailHost :state="detail" :title="t('detail.title')" :width="720" :footer="false" :translate="t">
        <div v-if="detail.data.value" class="fin-dep__detail">
          <NDescriptions :column="2" size="small" label-placement="left" bordered>
            <NDescriptionsItem :label="t('detail.number')">{{ detail.data.value.number ?? t('draftLabel') }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.date')">{{ fmtDate(detail.data.value.depositDate) }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.from')">{{ detail.data.value.fromAccountName ?? EMPTY_DASH }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.to')">{{ detail.data.value.toAccountName ?? EMPTY_DASH }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.currency')">{{ detail.data.value.currency }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.amount')">
              {{ fmtMoney(detail.data.value.amount, detail.data.value.currency) }}
            </NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.reference')">{{ detail.data.value.reference ?? EMPTY_DASH }}</NDescriptionsItem>
            <NDescriptionsItem :label="t('detail.memo')">{{ detail.data.value.memo ?? EMPTY_DASH }}</NDescriptionsItem>
          </NDescriptions>
          <TResponsiveTable
            :columns="lineColumns"
            :data="detail.data.value.lines"
            :row-key="(r: DepositLineDto) => r.id"
            size="small"
            mobile="scroll"
            :pagination="false"
            :bordered="false"
            :empty-text="t('detail.noLines')"
          />
        </div>
      </TDetailHost>
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NDatePicker, NDescriptions, NDescriptionsItem, NForm, NFormItem, NInput, NInputNumber, NSelect } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
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
  AccountSystemRole,
  createFinanceBridge,
  FinanceDocumentStatus,
  type CreateDepositFundsLineDto,
  type DepositDto,
  type DepositLineDto,
  type UndepositedReceiptDto,
} from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { createFinanceOptionSources } from './options'
import { fmtMoney, fmtDate, tsToIsoDate } from './money'
import {
  buildDepositColumns,
  buildDepositLineColumns,
  buildDepositSearchFields,
  buildUndepositedColumns,
  type DepositRow,
} from './deposit-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.deposits')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const title = 'tnzi.admin.modules.finance.deposits.title'
const depositsTitle = 'tnzi.admin.modules.finance.deposits.depositsTitle'
const columns = buildDepositColumns(t)

// 真实筛选（标准 1）：只声明后端 QueryDto 真的支持的字段。
const searchFields = buildDepositSearchFields(t)
const queueColumns = buildUndepositedColumns(t)
const lineColumns = buildDepositLineColumns(t)

const sections = [
  // Mixed blocks (filter bar + a plain table): the pane owns its scroll.
  { name: 'queue', label: t('tabs.queue'), scroll: true },
  { name: 'deposits', label: t('tabs.deposits') },
]

const canCreate = computed(() => can('finance.document.create'))
const canUpdate = computed(() => can('finance.document.update'))
const canDelete = computed(() => can('finance.document.delete'))

void sources.ensureFundsAccounts()

// ── Undeposited receipts ────────────────────────────────────────
const sourceAccountId = ref<string | null>(null)
const queueAll = ref<UndepositedReceiptDto[]>([])
const queueLoading = ref(false)
const checkedQueueKeys = ref<string[]>([])

/**
 * 候选清单可以是混币的（未限定币种的待存款项科目上什么币都收得到），
 * 而一张存款单只能是一种币 —— 所以币种在这里就分开，而不是让人勾一堆
 * 混币的支票、到点「创建」那一刻才被后端拒绝。
 */
const queueCurrency = ref<string | null>(null)
const queueCurrencies = computed(() => [...new Set(queueAll.value.map((r) => r.currency))].sort())
const currencyOptions = computed(() => queueCurrencies.value.map((c) => ({ label: c, value: c })))
const queueRows = computed(() =>
  queueCurrency.value ? queueAll.value.filter((r) => r.currency === queueCurrency.value) : queueAll.value,
)

/**
 * 队列概览（标准 2）：数据源是**当前币种的全量候选清单**（不分页），所以合计是真数字 ——
 * 跨币种求和再贴上其中一个币种的标签，得到的是一个看着精确的假数字。
 */
const queueKpis = computed(() => ({
  count: queueRows.value.length,
  total: queueRows.value.reduce((sum, r) => sum + (r.amount ?? 0), 0),
  currency: queueCurrency.value ?? undefined,
}))

const checkedTotal = computed(() =>
  queueRows.value
    .filter((r) => checkedQueueKeys.value.includes(r.paymentEntryId))
    .reduce((sum, r) => sum + (r.amount ?? 0), 0),
)

/**
 * 目标账户不能是来源账户本身（后端也拒，这里先把它从下拉里拿掉：
 * 让人选得到一个必然被拒的选项没有意义）。
 */
const destinationOptions = computed(() =>
  sources.fundsAccountOptions.value.filter((o) => o.value !== sourceAccountId.value),
)

async function loadQueue() {
  // 三条退出路径都要守住同一条不变式：勾选集合 ⊆ 屏幕上看得见的行。
  if (!sourceAccountId.value) {
    queueAll.value = []
    queueCurrency.value = null
    checkedQueueKeys.value = []
    return
  }
  queueLoading.value = true
  try {
    queueAll.value = await bridge.deposits.undeposited({ accountId: sourceAccountId.value })
    // 保留当前币种（如果它还在清单里），否则落到第一个 —— 过账/作废后重刷不该
    // 把人从他正在看的那一档币种上弹走。
    const available = queueCurrencies.value
    if (!queueCurrency.value || !available.includes(queueCurrency.value)) {
      queueCurrency.value = available[0] ?? null
    }
    // 刷新后勾选集合里可能留着已经不在清单上的收款（并发下被别人
    // 收走了，或者这一档币种空了导致自动切档）。留着它们，屏幕上的合计
    // 与真正提交的 id 就对不上 —— 而多出来的那几张是看不见的。
    const visible = new Set(queueRows.value.map((r) => r.paymentEntryId))
    checkedQueueKeys.value = checkedQueueKeys.value.filter((k) => visible.has(k))
  } catch (error) {
    queueAll.value = []
    queueCurrency.value = null
    checkedQueueKeys.value = []
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    queueLoading.value = false
  }
}

function onSourceAccountChange() {
  // 换了来源账户，之前勾的那几张已经不在这张清单上了
  checkedQueueKeys.value = []
  queueCurrency.value = null
  void loadQueue()
}

function onCurrencyChange() {
  // 换了币种，之前勾的那几张不在这一档里了
  checkedQueueKeys.value = []
}

function onCheckedChange(keys: Array<string | number>) {
  checkedQueueKeys.value = keys.map(String)
}

/**
 * 默认选中待存款项科目（多数部署只有一个），省掉一次必然的点击。
 *
 * 判据是科目的 `systemRole` 而不是显示名 —— 后者是本地化过的 `code name` 串，
 * 拿正则去匹配它只在英文科目表上成立。
 */
watch(
  () => sources.fundsAccountRoles.value,
  (roles) => {
    if (sourceAccountId.value) return
    const undeposited = roles[AccountSystemRole.UndepositedFunds]
    if (undeposited) {
      sourceAccountId.value = undeposited
      void loadQueue()
    }
  },
  { immediate: true },
)

// ── Deposits list ───────────────────────────────────────────────
const crud = useCrudPage<DepositRow>({
  pageId: 'finance.deposits',
  permission: 'finance.document',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.deposits.fetch(q),
})

// ── Record deposit ──────────────────────────────────────────────
interface FundsLineForm {
  key: number
  accountId: string | null
  amount: number | null
  description: string | null
}

const createDetail = useDetail<{ id: string }>({ mode: 'modal', url: 'create' })
const createForm = reactive<{ toAccountId: string | null; depositDate: number | null; reference: string | null; memo: string | null }>({
  toAccountId: null,
  depositDate: Date.now(),
  reference: null,
  memo: null,
})
const otherFunds = ref<FundsLineForm[]>([])
let fundsSeq = 0
const creating = ref(false)

const isBlankFundsLine = (l: FundsLineForm) => !l.accountId && (l.amount ?? 0) === 0 && !l.description?.trim()
const isValidFundsLine = (l: FundsLineForm) => !!l.accountId && (l.amount ?? 0) > 0

const validFundsLines = computed(() => otherFunds.value.filter(isValidFundsLine))
const fundsTotal = computed(() => validFundsLines.value.reduce((sum, l) => sum + (l.amount ?? 0), 0))

/**
 * 填了一半的行既不能提交，也<b>不能悄悄丢掉</b>：把一条已经选好科目的款项行
 * 静默滤掉，得到的是一张少了这笔钱的存款单，而屏幕上没有任何地方说过它被丢了。
 * 一行都没碰过（点了「添加行」又改主意）则不算数，直接忽略。
 */
const incompleteFundsLines = computed(() =>
  otherFunds.value.filter((l) => !isBlankFundsLine(l) && !isValidFundsLine(l)),
)

const createHint = computed(() => {
  const total = fmtMoney(checkedTotal.value + fundsTotal.value, queueKpis.value.currency)
  const count = String(checkedQueueKeys.value.length)
  return validFundsLines.value.length > 0
    ? t('create.hintWithFunds', { count, funds: String(validFundsLines.value.length), total })
    : t('create.hint', { count, total })
})

// 后端的准入是「至少一张收款或一条其它款项行」—— 这里逐字照搬，不额外收紧：
// 只由银行利息组成的存款单是合法的。额外的一条是「没有填一半的行」，
// 它不是后端规则，而是不允许本页面静默丢掉用户已经填进去的钱。
const canSubmit = computed(
  () =>
    !!createForm.toAccountId &&
    incompleteFundsLines.value.length === 0 &&
    (checkedQueueKeys.value.length > 0 || validFundsLines.value.length > 0),
)

function addFundsLine() {
  otherFunds.value = [...otherFunds.value, { key: ++fundsSeq, accountId: null, amount: null, description: null }]
}

function removeFundsLine(key: number) {
  otherFunds.value = otherFunds.value.filter((l) => l.key !== key)
}

function openCreate() {
  if (!sourceAccountId.value) return
  // 贷记科目只有这个弹窗用得到，而它是另一次科目树请求 ——
  // 大多数人来这一页只是看列表，不该替他们提前付这笔开销。
  void sources.ensureLeafAccounts()
  createForm.toAccountId = null
  createForm.depositDate = Date.now()
  createForm.reference = null
  createForm.memo = null
  otherFunds.value = []
  void createDetail.open('create')
}

async function submitCreate() {
  if (!sourceAccountId.value || !createForm.toAccountId) return
  creating.value = true
  try {
    const funds: CreateDepositFundsLineDto[] = validFundsLines.value.map((l) => ({
      accountId: l.accountId!,
      amount: l.amount!,
      description: l.description?.trim() || null,
    }))
    await bridge.deposits.createDraft({
      fromAccountId: sourceAccountId.value,
      toAccountId: createForm.toAccountId,
      depositDate: tsToIsoDate(createForm.depositDate ?? Date.now()),
      // 币种跟着候选清单那一档走：不发它就等于把每一张存款单都开成本位币，
      // 于是外币收款永远存不进银行。
      currency: queueCurrency.value,
      reference: createForm.reference?.trim() || null,
      memo: createForm.memo?.trim() || null,
      paymentEntryIds: [...checkedQueueKeys.value],
      otherFunds: funds,
    })
    message.success(t('create.success'))
    createDetail.close()
    checkedQueueKeys.value = []
    otherFunds.value = []
    await loadQueue()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    creating.value = false
  }
}

// ── Detail drawer ───────────────────────────────────────────────
const detail = useDetail<DepositDto>({ mode: 'drawer', url: 'detail', loadData: (id) => bridge.deposits.getById(String(id)) })

// ── Post / void / delete ────────────────────────────────────────
async function run(action: () => Promise<unknown>, successKey: string) {
  try {
    await action()
    message.success(t(successKey))
    // 过账/作废/删除都会改变哪些收款还可选，两张表一起刷
    await Promise.all([crud.refresh(), loadQueue()])
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const isDraft = (r: DepositRow) => r.status === FinanceDocumentStatus.Draft
const isPosted = (r: DepositRow) => r.status === FinanceDocumentStatus.Posted

const rowActions: RowAction<DepositRow>[] = [
  { key: 'detail', label: 'actions.detail', onClick: (r) => void detail.open('view', String(r.id ?? '')) },
  {
    key: 'post',
    label: 'actions.post',
    type: 'primary',
    show: (r) => canUpdate.value && isDraft(r),
    confirm: 'confirmPost',
    onClick: (r) => void run(() => bridge.deposits.post(String(r.id ?? '')), 'postSuccess'),
  },
  {
    key: 'void',
    label: 'actions.void',
    type: 'warning',
    show: (r) => canUpdate.value && isPosted(r),
    confirm: 'confirmVoid',
    onClick: (r) => void run(() => bridge.deposits.voidDoc(String(r.id ?? '')), 'voidSuccess'),
  },
  {
    key: 'delete',
    label: 'actions.delete',
    type: 'error',
    show: (r) => canDelete.value && isDraft(r),
    confirm: 'confirmDelete',
    onClick: (r) => void run(() => bridge.deposits.deleteDraft(String(r.id ?? '')), 'deleteSuccess'),
  },
]
</script>

<style scoped>
.fin-dep__queue {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-dep__queue-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.fin-dep__account-select {
  min-width: 240px;
}

.fin-dep__currency-select {
  min-width: 110px;
}

.fin-dep__queue-create {
  margin-left: auto;
}

.fin-dep__detail {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.fin-dep__form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-dep__form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.fin-dep__hint {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}

.fin-dep__full {
  width: 100%;
}

.fin-dep__warn {
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-error);
}

.fin-dep__funds {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-top: 4px;
  border-top: 1px solid var(--tnzi-border);
}

.fin-dep__funds-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.fin-dep__funds-title {
  font-weight: 500;
}

.fin-dep__funds-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.fin-dep__funds-account {
  flex: 2 1 0;
  min-width: 0;
}

.fin-dep__funds-amount {
  flex: 1 1 0;
  min-width: 0;
}

.fin-dep__funds-desc {
  flex: 2 1 0;
  min-width: 0;
}

.fin-dep__form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

@media (max-width: 640px) {
  .fin-dep__form-grid {
    grid-template-columns: 1fr;
  }

  .fin-dep__funds-row {
    flex-wrap: wrap;
  }
}
</style>
