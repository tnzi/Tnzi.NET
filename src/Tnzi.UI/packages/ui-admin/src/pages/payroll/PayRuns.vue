<template>
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :title="title"
    :row-actions="rowActions"
    :translate="t"
    :detail-width="720"
    :detail-title="detailTitle"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="payRunFormSchema"
        :sections="payRunFormSections"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :field-renderers="fieldRenderers"
        :translate="t"
      />
    </template>

    <!-- Read-only detail: run summary + its payslips. -->
    <template #detail>
      <template v-if="viewedRun">
        <NDescriptions :column="2" size="small" bordered class="pr-run-detail__meta">
          <NDescriptionsItem :label="t('columns.status')">{{ t(`status.${enumKey(viewedRun.status)}`) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.source')">{{ t(`source.${enumKey(viewedRun.source)}`) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.period')">{{ formatDateOnly(viewedRun.periodStart, { utc: true }) }} ~ {{ formatDateOnly(viewedRun.periodEnd, { utc: true }) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.payDate')">{{ formatDateOnly(viewedRun.payDate, { utc: true }) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('detail.grossTotal')">{{ fmtAmount(viewedRun.grossTotal) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('detail.deductionTotal')">{{ fmtAmount(viewedRun.deductionTotal) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('detail.employerCostTotal')">{{ fmtAmount(viewedRun.employerCostTotal) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.netTotal')">{{ fmtAmount(viewedRun.netTotal) }}</NDescriptionsItem>
        </NDescriptions>
        <div v-if="(viewedRun.errorCount ?? 0) > 0" class="pr-run-detail__error">
          {{ t('detail.errorCount', { count: viewedRun.errorCount }) }}
        </div>
        <h4 class="pr-run-detail__subtitle">{{ t('detail.payslips') }}</h4>
        <TResponsiveTable
          :columns="payslipColumns"
          :data="runPayslips"
          :row-actions="payslipRowActions"
          :translate="t"
          :bordered="false"
          size="small"
          mobile="scroll"
          :pagination="false"
        />
      </template>
    </template>
  </TCrudPage>

  <!-- Single payslip: lines + (Calculated only) worked-days correction. -->
  <TDetailHost :state="payslipDetail" :title="payslipTitle" :width="640" :footer="false" :translate="t">
    <div v-if="payslipDetail.data.value" class="pr-payslip">
      <NDescriptions :column="2" size="small" bordered>
        <NDescriptionsItem :label="t('payslip.employee')">{{ payslipDetail.data.value.employeeName }}</NDescriptionsItem>
        <NDescriptionsItem :label="t('payslip.baseAmount')">{{ fmtAmount(payslipDetail.data.value.baseAmount) }}</NDescriptionsItem>
        <NDescriptionsItem :label="t('payslip.grossPay')">{{ fmtAmount(payslipDetail.data.value.grossPay) }}</NDescriptionsItem>
        <NDescriptionsItem :label="t('payslip.totalDeductions')">{{ fmtAmount(payslipDetail.data.value.totalDeductions) }}</NDescriptionsItem>
        <NDescriptionsItem :label="t('payslip.netPay')">{{ fmtAmount(payslipDetail.data.value.netPay) }}</NDescriptionsItem>
        <NDescriptionsItem :label="t('payslip.paymentStatus')">{{ t(`payslip.status.${enumKey(payslipDetail.data.value.paymentStatus)}`) }}</NDescriptionsItem>
      </NDescriptions>

      <div v-if="payslipDetail.data.value.calculationError" class="pr-payslip__error">
        {{ payslipDetail.data.value.calculationError }}
      </div>

      <!-- Worked-days correction: only while the run is Calculated. -->
      <div v-if="canEditInputs" class="pr-payslip__inputs">
        <span class="pr-payslip__inputs-label">{{ t('payslip.workedDays') }}</span>
        <NInputNumber v-model:value="workedDays" size="small" :min="0" :show-button="false" class="pr-payslip__inputs-field" />
        <NButton size="small" type="primary" :loading="recalculating" @click="submitWorkedDays">{{ t('payslip.recalc') }}</NButton>
      </div>

      <h4 class="pr-payslip__subtitle">{{ t('payslip.lines') }}</h4>
      <TResponsiveTable
        :columns="lineColumns"
        :data="payslipDetail.data.value.lines"
        :translate="t"
        :bordered="false"
        size="small"
        mobile="scroll"
        :pagination="false"
      />
    </div>
  </TDetailHost>

  <!-- Pay: funds account / method / date / optional employee subset. -->
  <TDetailHost :state="payDetail" :title="t('pay.title')" :width="560" :footer="false" :translate="t">
    <div class="pr-pay">
      <div class="pr-pay__field">
        <span class="pr-pay__label">{{ t('pay.account') }}</span>
        <NSelect v-model:value="payModel.paymentAccountId" :options="sources.cashAccountOptions.value" filterable :placeholder="t('pay.accountPlaceholder')" size="small" />
      </div>
      <div class="pr-pay__field">
        <span class="pr-pay__label">{{ t('pay.date') }}</span>
        <NDatePicker v-model:value="payModel.paymentDate" type="date" size="small" class="pr-pay__grow" />
      </div>
      <div class="pr-pay__field">
        <span class="pr-pay__label">{{ t('pay.method') }}</span>
        <NInput v-model:value="payModel.paymentMethod" :placeholder="t('pay.methodPlaceholder')" size="small" />
      </div>
      <div class="pr-pay__field">
        <span class="pr-pay__label">{{ t('pay.reference') }}</span>
        <NInput v-model:value="payModel.reference" :placeholder="t('pay.referencePlaceholder')" size="small" />
      </div>
      <div class="pr-pay__field">
        <span class="pr-pay__label">{{ t('pay.employees') }}</span>
        <NSelect
          v-model:value="payModel.employeeIds"
          multiple
          :options="payableOptions"
          filterable
          :placeholder="t('pay.employeesPlaceholder')"
          size="small"
        />
      </div>
      <div class="pr-pay__actions">
        <NButton size="small" @click="payDetail.close()">{{ t('pay.cancel') }}</NButton>
        <NButton size="small" type="primary" :loading="paying" :disabled="!payModel.paymentAccountId" @click="submitPay">{{ t('pay.submit') }}</NButton>
      </div>
    </div>
  </TDetailHost>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, reactive, ref } from 'vue'
import { NButton, NDatePicker, NDescriptions, NDescriptionsItem, NInput, NInputNumber, NSelect, type DataTableColumns } from 'naive-ui'
import { formatDateOnly } from '@tnzi/core'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { viewAction, type RowAction } from '../../headless/row-actions'
import {
  createPayrollBridge,
  PayRunStatus,
  type PayRunDto,
  type PayslipDto,
  type PayslipLineDto,
  type PayslipListDto,
} from '../../services/bridges/payroll-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { createPayrollOptionSources } from './options'
import { enumKey } from './setup-config'
import { amountCell, fmtAmount, isoDateToLocalTs, tsToIsoDate } from '../finance/money'
import {
  buildPayRunColumns,
  payRunFormSchema,
  payRunFormSections,
  toPayRunPayload,
  type PayRunRow,
} from './pay-run-config'

const bridge = createPayrollBridge({ client: useAdminClient() })
const t = makePageTranslator('payroll.payRuns')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createPayrollOptionSources(bridge)

const columns = buildPayRunColumns(t)

const toIso = (v: unknown): string => (typeof v === 'number' ? tsToIsoDate(v) : typeof v === 'string' && v ? v : tsToIsoDate(Date.now()))

const crud = useCrudPage<PayRunRow>({
  pageId: 'payroll.payRuns',
  permission: 'payroll.run',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.runs.fetch(q),
  loadDetailById: (id) => bridge.runs.getById(id),
  createData: (d) => bridge.runs.createDraft(toPayRunPayload(d, toIso)),
  updateData: (id, d) => bridge.runs.updateDraft(String(id), toPayRunPayload(d, toIso)),
  deleteData: async (ids) => {
    for (const id of ids) await bridge.runs.deleteDraft(String(id))
  },
  onView: (row) => void loadRunDetail(row),
})

const title = 'tnzi.admin.modules.payroll.payRuns.title'
const detailTitle = (r: PayRunRow) => r.number ?? t('draftLabel')

const fieldRenderers = {
  'payroll-structure': selectRenderer(() => sources.structureOptions.value, { placeholder: t('form.structurePlaceholder') }),
}

async function run(action: () => Promise<unknown>, successKey: string) {
  try {
    await action()
    message.success(t(successKey))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

// ── Run detail (#detail slot) ───────────────────────────────────
const viewedRun = ref<PayRunDto | null>(null)
const runPayslips = ref<PayslipListDto[]>([])

async function loadRunDetail(row: PayRunRow) {
  viewedRun.value = null
  runPayslips.value = []
  const id = String(row.id ?? '')
  try {
    viewedRun.value = await bridge.runs.getById(id)
    runPayslips.value = await bridge.runs.payslips(id)
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const payslipColumns: DataTableColumns<PayslipListDto> = [
  { key: 'employeeName', title: t('payslip.employee'), minWidth: 140, render: (r) => r.employeeName || r.employeeCode },
  { key: 'grossPay', title: t('payslip.grossPay'), width: 110, render: (r) => amountCell(fmtAmount(r.grossPay)) },
  { key: 'netPay', title: t('payslip.netPay'), width: 110, render: (r) => amountCell(fmtAmount(r.netPay), true) },
  { key: 'paymentStatus', title: t('payslip.paymentStatus'), width: 100, render: (r) => t(`payslip.status.${enumKey(r.paymentStatus)}`) },
  { key: 'calculationError', title: t('payslip.error'), minWidth: 120, render: (r) => r.calculationError ?? EMPTY_DASH },
]

const payslipRowActions: RowAction<PayslipListDto>[] = [
  { key: 'detail', label: 'payslip.view', type: 'info', onClick: (r) => void openPayslip(r.id) },
]

// ── Single payslip drawer ───────────────────────────────────────
const currentRunId = ref('')
const payslipDetail = useDetail<PayslipDto>({
  mode: 'drawer',
  loadData: (payslipId) => bridge.runs.payslip(currentRunId.value, String(payslipId)),
})
const workedDays = ref<number | null>(null)
const recalculating = ref(false)

const payslipTitle = computed(() => payslipDetail.data.value?.employeeName ?? t('payslip.title'))
const canEditInputs = computed(() =>
  can('payroll.run.update') && viewedRun.value?.status === PayRunStatus.Calculated && !!payslipDetail.data.value,
)

async function openPayslip(payslipId: string) {
  currentRunId.value = String(viewedRun.value?.id ?? '')
  await payslipDetail.open('view', payslipId)
  workedDays.value = payslipDetail.data.value?.workedDays ?? null
}

async function submitWorkedDays() {
  const slip = payslipDetail.data.value
  if (!slip) return
  recalculating.value = true
  try {
    const updated = await bridge.runs.updatePayslipInputs(currentRunId.value, slip.id, { workedDays: Number(workedDays.value ?? 0) })
    message.success(t('payslip.recalcSuccess'))
    payslipDetail.data.value = updated
    if (viewedRun.value) await loadRunDetail({ id: viewedRun.value.id })
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    recalculating.value = false
  }
}

const lineColumns: DataTableColumns<PayslipLineDto> = [
  { key: 'sequence', title: t('payslip.seq'), width: 60, render: (r) => String(r.sequence) },
  { key: 'componentName', title: t('payslip.component'), minWidth: 150, render: (r) => `${r.componentCode} · ${r.componentName}` },
  { key: 'componentType', title: t('payslip.type'), width: 150, render: (r) => t(`componentType.${enumKey(r.componentType)}`) },
  { key: 'amount', title: t('payslip.amount'), width: 120, render: (r) => amountCell(fmtAmount(r.amount)) },
  { key: 'ytdAmount', title: t('payslip.ytd'), width: 120, render: (r) => amountCell(fmtAmount(r.ytdAmount)) },
]

// ── Pay drawer ──────────────────────────────────────────────────
const payDetail = useDetail<{ id: string }>({ mode: 'drawer' })
const payRunId = ref('')
const payPayslips = ref<PayslipListDto[]>([])
const paying = ref(false)
const payModel = reactive<{ paymentAccountId: string | null; paymentDate: number | null; paymentMethod: string | null; reference: string | null; employeeIds: string[] }>({
  paymentAccountId: null,
  paymentDate: Date.now(),
  paymentMethod: null,
  reference: null,
  employeeIds: [],
})

const payableOptions = computed(() =>
  payPayslips.value.map((p) => ({ label: p.employeeName || p.employeeCode, value: p.employeeId })),
)

async function openPay(row: PayRunRow) {
  payRunId.value = String(row.id ?? '')
  payModel.paymentAccountId = null
  payModel.paymentDate = Date.now()
  payModel.paymentMethod = null
  payModel.reference = null
  payModel.employeeIds = []
  payPayslips.value = []
  void sources.ensureCashAccounts()
  try {
    const slips = await bridge.runs.payslips(payRunId.value)
    payPayslips.value = slips.filter((s) => s.paymentStatus !== 'Paid')
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
  await payDetail.open('view', { id: payRunId.value })
}

async function submitPay() {
  if (!payModel.paymentAccountId) return
  paying.value = true
  try {
    await bridge.runs.pay(payRunId.value, {
      employeeIds: payModel.employeeIds.length ? payModel.employeeIds : null,
      paymentAccountId: payModel.paymentAccountId,
      paymentDate: toIso(payModel.paymentDate),
      paymentMethod: payModel.paymentMethod || null,
      reference: payModel.reference || null,
    })
    message.success(t('pay.success'))
    payDetail.close()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    paying.value = false
  }
}

// ── Row actions (status-conditional lifecycle) ──────────────────
function toEditModel(row: PayRunRow): PayRunRow {
  return {
    ...row,
    periodStart: row.periodStart ? (isoDateToLocalTs(row.periodStart) as unknown as string) : undefined,
    periodEnd: row.periodEnd ? (isoDateToLocalTs(row.periodEnd) as unknown as string) : undefined,
    payDate: row.payDate ? (isoDateToLocalTs(row.payDate) as unknown as string) : undefined,
  }
}

const isDraft = (r: PayRunRow) => r.status === PayRunStatus.Draft
const isCalculated = (r: PayRunRow) => r.status === PayRunStatus.Calculated
const isPayable = (r: PayRunRow) => r.status === PayRunStatus.Posted || r.status === PayRunStatus.PartiallyPaid
const isVoidable = (r: PayRunRow) => r.status === PayRunStatus.Posted || r.status === PayRunStatus.PartiallyPaid || r.status === PayRunStatus.Paid

const rowActions: RowAction<PayRunRow>[] = [
  viewAction(crud),
  { key: 'edit', show: (row) => crud.canUpdate && isDraft(row), onClick: (row) => crud.openEdit(toEditModel(row)) },
  { key: 'calculate', label: 'actions.calculate', type: 'primary', show: (row) => can('payroll.run.update') && (isDraft(row) || isCalculated(row)), onClick: (row) => void run(() => bridge.runs.calculate(String(row.id ?? '')), 'calculateSuccess') },
  { key: 'post', label: 'actions.post', type: 'primary', show: (row) => can('payroll.run.update') && isCalculated(row) && (row.errorCount ?? 0) === 0, confirm: 'confirmPost', onClick: (row) => void run(() => bridge.runs.post(String(row.id ?? '')), 'postSuccess') },
  { key: 'pay', label: 'actions.pay', type: 'info', show: (row) => can('payroll.run.update') && isPayable(row), onClick: (row) => void openPay(row) },
  { key: 'void', label: 'actions.void', type: 'warning', show: (row) => can('payroll.run.update') && isVoidable(row), confirm: 'confirmVoid', onClick: (row) => void run(() => bridge.runs.voidRun(String(row.id ?? '')), 'voidSuccess') },
  { key: 'delete', label: 'actions.delete', type: 'error', show: (row) => crud.canDelete && isDraft(row), confirm: 'confirmDelete', onClick: (row) => void run(() => bridge.runs.deleteDraft(String(row.id ?? '')), 'deleteSuccess') },
]
</script>

<style scoped>
.pr-run-detail__meta {
  margin-bottom: 12px;
}

.pr-run-detail__subtitle,
.pr-payslip__subtitle {
  margin: 12px 0 8px;
  font-size: 13px;
  font-weight: 600;
}

.pr-run-detail__error,
.pr-payslip__error {
  color: var(--tnzi-error, #d03050);
  font-size: 13px;
  margin: 8px 0;
}

.pr-payslip {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.pr-payslip__inputs {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 8px;
}

.pr-payslip__inputs-label {
  font-size: 13px;
}

.pr-payslip__inputs-field {
  width: 140px;
}

.pr-pay {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.pr-pay__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.pr-pay__label {
  font-size: 13px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.65));
}

.pr-pay__grow {
  width: 100%;
}

.pr-pay__actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 8px;
}
</style>
