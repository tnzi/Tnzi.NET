<template>
  <TTabsPage
    v-model:section="section"
    :sections="sections"
    :title="tp('title')"
    :help="tp('help')"
    :translate="tp"
    scroll="fill"
  >
    <template #actions>
      <NButton tertiary size="small" @click="reload">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="16" /></template>
        {{ tp('actions.refresh') }}
      </NButton>
    </template>

    <!-- ── Collections: who to chase, worst first ── -->
    <template #collections>
      <div class="flex flex-col gap-12px flex-1 min-h-0 h-full">
        <div class="flex flex-wrap items-center gap-8px">
          <NRadioGroup v-model:value="partyType" size="small" @update:value="loadDunning">
            <NRadioButton :value="FinancePartyType.Customer">{{ tp('party.customers') }}</NRadioButton>
            <NRadioButton :value="FinancePartyType.Vendor">{{ tp('party.vendors') }}</NRadioButton>
          </NRadioGroup>
          <span class="ml-auto text-12px text-muted">{{ tp('collections.count', { n: candidates.length }) }}</span>
        </div>

        <NSpin :show="loadingDunning" class="fin-stmt__spin">
          <TEmpty v-if="!loadingDunning && candidates.length === 0" :text="tp('collections.empty')" />
          <div v-else class="flex flex-col gap-8px flex-1 min-h-0 overflow-y-auto">
            <!-- One row per party: the level, the money, and the two things you
                 would actually do next. A worklist that only lists names makes
                 you go somewhere else to act, which is why nobody uses it. -->
            <div v-for="row in candidates" :key="row.partyId" class="fin-stmt__row flex flex-col gap-8px p-[10px_12px]">
              <div class="flex flex-wrap items-center gap-8px">
                <NTag :type="levelTone(row.level)" size="small" :bordered="false">
                  {{ tp(`dunning.${lowerFirst(row.level)}`) }}
                </NTag>
                <button type="button" class="fin-stmt__partybtn" @click="openStatementFor(row.partyId, row.partyName)">
                  {{ row.partyName || tp('collections.unnamed') }}
                </button>
                <span class="ml-auto text-12px text-muted">{{ tp('collections.daysPastDue', { n: row.oldestOverdueDays }) }}</span>
              </div>

              <div class="flex flex-wrap items-center gap-16px">
                <TAgingBar :buckets="row.buckets" :currency="currency" class="flex-[1_1_260px] min-w-200px" />
                <div class="flex gap-20px">
                  <div class="flex flex-col gap-2px">
                    <span class="fin-stmt__amountlabel text-11px uppercase text-muted">{{ tp('collections.overdue') }}</span>
                    <TMoney :value="row.overdue" :currency="currency" tone="auto" />
                  </div>
                  <div class="flex flex-col gap-2px">
                    <span class="fin-stmt__amountlabel text-11px uppercase text-muted">{{ tp('collections.balance') }}</span>
                    <TMoney :value="row.openBalance" :currency="currency" />
                  </div>
                </div>
                <div class="flex gap-6px">
                  <NButton size="tiny" tertiary @click="openStatementFor(row.partyId, row.partyName)">
                    {{ tp('actions.viewStatement') }}
                  </NButton>
                  <NButton size="tiny" tertiary @click="download(row.partyId)">
                    <template #icon><TSvgIcon icon="mdi:printer-outline" :size="14" /></template>
                    {{ tp('actions.print') }}
                  </NButton>
                </div>
              </div>
            </div>
          </div>
        </NSpin>
      </div>
    </template>

    <!-- ── Statement: one party, one period ── -->
    <template #statement>
      <div class="flex flex-col gap-12px flex-1 min-h-0 h-full">
        <div class="flex flex-wrap items-center gap-8px">
          <NRadioGroup v-model:value="partyType" size="small" @update:value="onPartyTypeChange">
            <NRadioButton :value="FinancePartyType.Customer">{{ tp('party.customers') }}</NRadioButton>
            <NRadioButton :value="FinancePartyType.Vendor">{{ tp('party.vendors') }}</NRadioButton>
          </NRadioGroup>

          <TPartySelect
            v-model="partyId"
            :bridge="bridge"
            :kind="partyType === FinancePartyType.Customer ? 'customer' : 'vendor'"
            :placeholder="tp('statement.pickParty')"
            class="flex-[1_1_220px] min-w-220px"
            @update:model-value="loadStatement"
          />

          <NRadioGroup v-model:value="style" size="small" @update:value="loadStatement">
            <NRadioButton value="OpenItem">{{ tp('style.openItem') }}</NRadioButton>
            <NRadioButton value="Activity">{{ tp('style.activity') }}</NRadioButton>
          </NRadioGroup>

          <NDatePicker
            v-if="style === 'Activity'"
            v-model:value="rangeTs"
            type="daterange"
            size="small"
            clearable
            class="w-240px max-w-full"
            @update:value="loadStatement"
          />
          <NDatePicker
            v-else
            v-model:value="asOfTs"
            type="date"
            size="small"
            clearable
            class="w-150px max-w-full"
            @update:value="loadStatement"
          />

          <NButton
            size="small"
            tertiary
            :disabled="!partyId || !statement"
            @click="download(partyId!)"
          >
            <template #icon><TSvgIcon icon="mdi:printer-outline" :size="16" /></template>
            {{ tp('actions.print') }}
          </NButton>
        </div>

        <NAlert v-if="renderError" type="warning" closable class="flex-none" @close="renderError = ''">
          {{ renderError }}
        </NAlert>

        <NSpin :show="loadingStatement" class="fin-stmt__spin">
          <TEmpty v-if="!partyId" :text="tp('statement.pickPartyHint')" />
          <TEmpty v-else-if="!loadingStatement && !statement" :text="tp('statement.empty')" />
          <div v-else-if="statement" class="fin-stmt__doc flex flex-col gap-12px flex-1 min-h-0 overflow-y-auto">
            <!-- The three numbers that decide what happens next, above the detail. -->
            <TKpiRow cols="1 s:3">
              <TKpiCard
                :label="tp('statement.amountDue')"
                :value="fmt(statement.closingBalance)"
                :animated="false"
                icon="mdi:scale-balance"
              />
              <TKpiCard
                :label="tp('statement.overdue')"
                :value="fmt(statement.overdue)"
                :animated="false"
                icon="mdi:clock-alert-outline"
                :tone="statement.overdue > 0 ? 'error' : 'success'"
              />
              <TKpiCard
                :label="tp('statement.level')"
                :value="tp(`dunning.${lowerFirst(statement.dunningLevel)}`)"
                icon="mdi:bell-alert-outline"
                :tone="statement.dunningLevel === 'None' ? 'success' : 'warning'"
              />
            </TKpiRow>

            <TAgingBar :buckets="statement.buckets" :currency="statement.currency" class="flex-[1_1_260px] min-w-200px" />

            <TResponsiveTable
              :columns="lineColumns"
              :data="statement.lines"
              :row-key="rowKey"
              :pagination="false"
              :bordered="false"
              size="small"
              mobile="cards"
            />
          </div>
        </NSpin>
      </div>
    </template>
  </TTabsPage>
</template>

<script setup lang="ts">
/**
 * Statements and collections.
 *
 * Two tabs because there are two jobs: **Collections** answers "who do I chase
 * today" (a ranked worklist with the statement one click away), **Statement**
 * answers "what exactly do they owe" for one party. Splitting them into two
 * pages would mean copying the party picker and the aging strip twice; merging
 * them into one list would bury the worklist under a form.
 *
 * The figures come from the same calculation as the aging report, so a
 * statement you post out ties to your own books penny for penny.
 */
import { computed, h, onMounted, ref } from 'vue'
import {
  NAlert, NButton, NDatePicker, NRadioButton, NRadioGroup, NSpin, NTag,
  type DataTableColumns,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TTabsPage, { type TabSection } from '../../components/layout/TTabsPage.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import { TEmpty } from '@tnzi/ui'
import TMoney from '../../components/finance/TMoney.vue'
import TAgingBar from '../../components/finance/TAgingBar.vue'
import TPartySelect from '../../components/finance/TPartySelect.vue'
import { useAdminClient } from '../../plugin/client'
import { useTabTitle } from '../../headless/useTabTitle'
import { makePageTranslator } from '../_shared/translate'
import { formatMoney, formatAccountingDate, tsToIsoDate, isoDateToLocalTs } from '../../utils/finance-format'
import { downloadBlob } from '@tnzi/core'
import {
  createFinanceBridge,
  FinancePartyType,
  type CustomerStatementDto,
  type DunningCandidateDto,
  type StatementLineDto,
} from '../../services/bridges/finance-bridge'

const bridge = createFinanceBridge({ client: useAdminClient() })
const tp = makePageTranslator('finance.statements')
useTabTitle(() => tp('title'))

const section = ref<'collections' | 'statement'>('collections')
const sections = computed((): TabSection[] => [
  { name: 'collections', label: tp('tabs.collections'), scroll: true },
  { name: 'statement', label: tp('tabs.statement'), scroll: true },
])

const partyType = ref<FinancePartyType>(FinancePartyType.Customer)
const partyId = ref<string | null>(null)
const style = ref<'OpenItem' | 'Activity'>('OpenItem')
const asOfTs = ref<number | null>(Date.now())
const rangeTs = ref<[number, number] | null>(null)

const candidates = ref<DunningCandidateDto[]>([])
const statement = ref<CustomerStatementDto | null>(null)
const loadingDunning = ref(false)
const loadingStatement = ref(false)
const renderError = ref('')

/** Collections rows are cross-party, so they are shown in the base currency. */
const currency = computed(() => statement.value?.currency ?? undefined)

const fmt = (amount?: number | null) => formatMoney(amount, { currency: statement.value?.currency })

function lowerFirst(value: string) {
  return value.charAt(0).toLowerCase() + value.slice(1)
}

function levelTone(level: string) {
  if (level === 'FinalNotice') return 'error' as const
  if (level === 'Overdue') return 'warning' as const
  if (level === 'Reminder') return 'info' as const
  return 'default' as const
}

const rowKey = (row: StatementLineDto) => `${row.docType}:${row.docId}`

const lineColumns = computed<DataTableColumns<StatementLineDto>>(() => [
  {
    title: tp('columns.date'),
    key: 'docDate',
    width: 120,
    render: (row) => formatAccountingDate(row.docDate),
  },
  {
    title: tp('columns.document'),
    key: 'number',
    render: (row) => row.number || row.docType,
  },
  {
    title: tp('columns.due'),
    key: 'dueDate',
    width: 150,
    render: (row) =>
      row.dueDate
        ? h(
            'span',
            { class: row.overdueDays > 0 ? 'fin-stmt__overdue' : '' },
            row.overdueDays > 0
              ? `${formatAccountingDate(row.dueDate)} (${row.overdueDays}d)`
              : formatAccountingDate(row.dueDate),
          )
        : '',
  },
  {
    title: tp('columns.charges'),
    key: 'charge',
    align: 'right',
    width: 130,
    render: (row) => (row.charge === 0 ? '' : h(TMoney, { value: row.charge, currency: statement.value?.currency })),
  },
  {
    title: tp('columns.payments'),
    key: 'payment',
    align: 'right',
    width: 130,
    render: (row) => (row.payment === 0 ? '' : h(TMoney, { value: row.payment, currency: statement.value?.currency })),
  },
  {
    // Open Item shows what is still outstanding; Activity shows the running balance.
    title: style.value === 'OpenItem' ? tp('columns.outstanding') : tp('columns.balance'),
    key: 'balance',
    align: 'right',
    width: 140,
    render: (row) => h(TMoney, { value: row.balance, currency: statement.value?.currency }),
  },
])

async function loadDunning() {
  loadingDunning.value = true
  try {
    candidates.value = await bridge.statements.dunning(partyType.value)
  } finally {
    loadingDunning.value = false
  }
}

function buildQuery() {
  return style.value === 'Activity'
    ? {
        style: style.value,
        from: rangeTs.value ? tsToIsoDate(rangeTs.value[0]) : undefined,
        to: rangeTs.value ? tsToIsoDate(rangeTs.value[1]) : undefined,
      }
    : { style: style.value, to: asOfTs.value ? tsToIsoDate(asOfTs.value) : undefined }
}

async function loadStatement() {
  if (!partyId.value) {
    statement.value = null
    return
  }
  loadingStatement.value = true
  try {
    statement.value = await bridge.statements.get(partyType.value, partyId.value, buildQuery())
  } finally {
    loadingStatement.value = false
  }
}

function onPartyTypeChange() {
  // A vendor id is meaningless once the switch flips to customers.
  partyId.value = null
  statement.value = null
  void loadDunning()
}

function openStatementFor(id: string, _name?: string | null) {
  partyId.value = id
  section.value = 'statement'
  void loadStatement()
}

async function download(id: string) {
  renderError.value = ''
  try {
    const blob = await bridge.statements.download(partyType.value, id, buildQuery())
    downloadBlob(blob, `statement-${id}.html`)
  } catch (error) {
    // 501 = no IStatementRenderer registered. Say so plainly instead of a toast
    // that reads like a server fault: the deployment just did not load the
    // optional Tnzi.Finance.Documents module.
    renderError.value = error instanceof Error ? error.message : tp('statement.renderFailed')
  }
}

function reload() {
  void loadDunning()
  void loadStatement()
}

onMounted(() => {
  rangeTs.value = [isoDateToLocalTs(monthsAgo(1)), Date.now()]
  void loadDunning()
})

function monthsAgo(n: number) {
  const d = new Date()
  d.setMonth(d.getMonth() - n)
  return d.toISOString().slice(0, 10)
}
</script>

<style scoped>
/* NSpin 渲染两层 block 包裹，会切断 flex 链——unocss 够不到子组件内部，只能 :deep。 */
.fin-stmt__spin,
.fin-stmt__spin :deep(.n-spin-content) {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

/* 语义化 BEM：带边框的工作台行。 */
.fin-stmt__row {
  border: 1px solid var(--tnzi-border, #efeff5);
  border-radius: 6px;
}

/* 往来方名字是真按钮（键盘可达），带 hover——不是琐碎布局类。 */
.fin-stmt__partybtn {
  background: none;
  border: 0;
  padding: 0;
  font: inherit;
  font-weight: 600;
  color: var(--tnzi-primary, #18a058);
  cursor: pointer;
}

.fin-stmt__partybtn:hover { text-decoration: underline; }

/* unocss 无 tracking-* 原子类（实测不生成规则），按 house 做法留 scoped。 */
.fin-stmt__amountlabel { letter-spacing: 0.04em; }

/* 逾期到期日着色：类由列 render 打在单元格内，需要 :deep 穿透。 */
.fin-stmt__doc :deep(.fin-stmt__overdue) { color: var(--tnzi-error, #d03050); }
</style>
