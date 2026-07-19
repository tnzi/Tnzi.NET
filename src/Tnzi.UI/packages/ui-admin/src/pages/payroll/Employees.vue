<template>
  <TCrudPage :state="crud" :all-columns="columns" :title="title" :row-actions="rowActions" :translate="t">
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="employeeFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :field-renderers="formRenderers"
        :translate="t"
      />
    </template>
  </TCrudPage>

  <!-- Salary assignments management (list + add + delete) for one employee. -->
  <TDetailHost :state="assignDetail" :title="assignTitle" :width="620" :footer="false" :translate="t">
    <div class="pr-assign">
      <TResponsiveTable
        :columns="assignmentColumns"
        :data="assignments"
        :row-actions="assignmentActions"
        :translate="t"
        :bordered="false"
        size="small"
        mobile="scroll"
        :pagination="false"
      />
      <h4 class="pr-assign__subtitle">{{ t('assignments.addTitle') }}</h4>
      <TFormSchemaRenderer
        :schema="assignmentFormSchema"
        :model="assignModel"
        :translate="t"
        :field-renderers="assignRenderers"
      />
      <div class="pr-assign__actions">
        <NButton size="small" type="primary" :loading="addingAssignment" :disabled="!assignModel.structureId" @click="submitAssignment">
          {{ t('assignments.add') }}
        </NButton>
      </div>
    </div>
  </TDetailHost>
</template>

<script setup lang="ts">
import { h, reactive, ref, watch } from 'vue'
import { NButton, type DataTableColumns } from 'naive-ui'
import { formatDateOnly } from '@tnzi/core'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { deleteAction, type RowAction } from '../../headless/rowActions'
import { createPayrollBridge, type SalaryAssignmentDto, type UpdateEmployeeDto } from '../../services/bridges/payroll-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer } from '../_shared/form-schema'
import TUserSelector from '../../components/forms/TUserSelector.vue'
import type { SelectorOption } from '../../components/forms/_selector-factory'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { createPayrollOptionSources } from './options'
import { buildEmployeeColumns, employeeFormSchema, assignmentFormSchema, type EmployeeRow } from './employee-config'
import { fmtAmount, isoDateToLocalTs, tsToIsoDate } from '../finance/money'

const bridge = createPayrollBridge({ client: useAdminClient() })
const client = useAdminClient()
const idBridge = createIdentityBridge({ client })
const t = makePageTranslator('payroll.employees')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createPayrollOptionSources(bridge)

// Linked-user remote search - pick a real account instead of pasting a raw GUID.
const userFetcher = async (keyword: string): Promise<SelectorOption[]> => {
  try {
    const res = await idBridge.users.fetch({ pageIndex: 1, pageSize: 20, searchText: keyword.trim(), sortField: undefined, sortOrder: null, filters: {} })
    return res.items.map((u) => ({ label: u.email ? `${u.userName} (${u.email})` : u.userName, value: u.id }))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    return []
  }
}

const formRenderers = {
  'employee-user': (ctx: { value: unknown; readonly?: boolean; onUpdate: (v: unknown) => void }) =>
    h(TUserSelector, {
      value: (ctx.value as string | null) ?? null,
      fetcher: userFetcher,
      placeholder: t('form.userIdPlaceholder'),
      size: 'small',
      disabled: ctx.readonly,
      'onUpdate:value': (v: string | null) => ctx.onUpdate(v),
    }),
}

const columns = buildEmployeeColumns(t)

const toIso = (v: unknown): string | null =>
  typeof v === 'number' ? tsToIsoDate(v) : typeof v === 'string' && v ? v : null

function toPayload(d: Record<string, unknown>): UpdateEmployeeDto {
  return {
    code: String(d.code ?? '').trim(),
    name: String(d.name ?? '').trim(),
    email: (d.email as string | null) || null,
    phone: (d.phone as string | null) || null,
    hireDate: toIso(d.hireDate),
    terminationDate: toIso(d.terminationDate),
    userId: (d.userId as string | null) || null,
    attributesJson: (d.attributesJson as string | null) || null,
    notes: (d.notes as string | null) || null,
    isActive: d.isActive !== false,
  }
}

// Edit hydration: date-only ISO → local-midnight ts for the pickers.
function toEditModel(row: EmployeeRow): EmployeeRow {
  return {
    ...row,
    hireDate: row.hireDate ? (isoDateToLocalTs(row.hireDate) as unknown as string) : null,
    terminationDate: row.terminationDate ? (isoDateToLocalTs(row.terminationDate) as unknown as string) : null,
  }
}

const crud = useCrudPage<EmployeeRow>({
  pageId: 'payroll.employees',
  permission: 'payroll.employee',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.employees.fetch(q),
  createData: (d) => bridge.employees.create(toPayload(d)),
  updateData: (id, d) => bridge.employees.update(String(id), toPayload(d)),
  deleteData: (ids) => bridge.employees.delete(ids.map(String)),
})

const title = 'tnzi.admin.modules.payroll.employees.title'

async function run(action: () => Promise<unknown>, successKey: string) {
  try {
    await action()
    message.success(t(successKey))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

// ── Salary assignments drawer ───────────────────────────────────
const assignDetail = useDetail<EmployeeRow>({ mode: 'drawer', url: 'assign' })
const assignments = ref<SalaryAssignmentDto[]>([])
const addingAssignment = ref(false)
const assignModel = reactive<Record<string, unknown>>({})
const assignTitle = ref('')

const assignRenderers = {
  'payroll-structure': selectRenderer(() => sources.structureOptions.value, { placeholder: t('form.structurePlaceholder'), clearable: false }),
}

function openAssignments(row: EmployeeRow) {
  assignTitle.value = t('assignments.titleFor', { name: row.name ?? row.code ?? '' })
  void sources.ensureStructures()
  void assignDetail.open('view', row)
}

async function loadAssignments(id: string) {
  assignments.value = []
  try {
    assignments.value = await bridge.employees.assignments(id)
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

watch(
  () => assignDetail.data.value?.id,
  (id) => {
    Object.keys(assignModel).forEach((k) => delete assignModel[k])
    if (id) void loadAssignments(String(id))
  },
)

async function submitAssignment() {
  const employeeId = assignDetail.data.value?.id
  if (!employeeId || !assignModel.structureId) return
  addingAssignment.value = true
  try {
    await bridge.employees.createAssignment(String(employeeId), {
      structureId: String(assignModel.structureId),
      effectiveFrom: toIso(assignModel.effectiveFrom) ?? tsToIsoDate(Date.now()),
      baseAmount: Number(assignModel.baseAmount ?? 0),
      notes: (assignModel.notes as string | null) || null,
    })
    message.success(t('assignments.added'))
    Object.keys(assignModel).forEach((k) => delete assignModel[k])
    await loadAssignments(String(employeeId))
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    addingAssignment.value = false
  }
}

async function deleteAssignment(assignmentId: string) {
  const employeeId = assignDetail.data.value?.id
  if (!employeeId) return
  await run(async () => {
    await bridge.employees.deleteAssignment(String(employeeId), assignmentId)
    await loadAssignments(String(employeeId))
  }, 'assignments.deleted')
}

const assignmentColumns: DataTableColumns<SalaryAssignmentDto> = [
  { key: 'structureName', title: t('assignments.structure'), minWidth: 140, render: (r) => r.structureName || r.structureId },
  { key: 'effectiveFrom', title: t('assignments.effectiveFrom'), width: 120, render: (r) => formatDateOnly(r.effectiveFrom, { utc: true }) },
  { key: 'baseAmount', title: t('assignments.baseAmount'), width: 120, render: (r) => fmtAmount(r.baseAmount) },
]

const assignmentActions: RowAction<SalaryAssignmentDto>[] = [
  { key: 'delete', label: 'assignments.delete', type: 'error', show: () => can('payroll.employee.update'), confirm: 'assignments.confirmDelete', onClick: (r) => void deleteAssignment(r.id) },
]

// ── Row actions ─────────────────────────────────────────────────
const rowActions: RowAction<EmployeeRow>[] = [
  { key: 'edit', show: () => crud.canUpdate, onClick: (row) => crud.openEdit(toEditModel(row)) },
  { key: 'assignments', label: 'actions.assignments', type: 'info', onClick: (row) => openAssignments(row) },
  { key: 'ensureVendor', label: 'actions.ensureVendor', type: 'primary', show: (row) => can('payroll.employee.update') && !row.vendorId, confirm: 'confirmEnsureVendor', onClick: (row) => void run(() => bridge.employees.ensureVendor(String(row.id ?? '')), 'ensureVendorSuccess') },
  deleteAction(crud),
]
</script>

<style scoped>
.pr-assign {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.pr-assign__subtitle {
  margin: 4px 0 0;
  font-size: 13px;
  font-weight: 600;
}

.pr-assign__actions {
  display: flex;
  justify-content: flex-end;
}
</style>
