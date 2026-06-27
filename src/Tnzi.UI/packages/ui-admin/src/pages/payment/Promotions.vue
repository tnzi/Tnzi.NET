<template>
  <!--
    Promotions — promotion/coupon CRUD wired to /admin/promotions.
    List chrome delegated to TListShell + TTableRenderer (useCrudPage fetch-only).
    Create / edit / deactivate still use a custom NModal + NForm (kept intact)
    because the form has complex datetime pickers, create-vs-edit field
    differences, and enum selectors that don't map cleanly to TFormSchemaRenderer.
  -->
  <TListShell
    :state="crud"
    :title="t('title')"
    :show-batch="false"
    :search-placeholder="t('filter.search')"
    :translate="t"
  >
    <template #toolbarLeft>
      <NSelect
        v-model:value="activeFilterRaw"
        :options="activeFilterOptions"
        :placeholder="t('filter.activeAny')"
        clearable
        size="small"
        class="w-160px"
        @update:value="onActiveFilterChange"
      />
    </template>
    <template #primary>
      <NButton size="small" type="primary" tertiary class="t-list-shell__action" @click="openCreate">
        <template #icon><TSvgIcon icon="mdi:plus" :size="16" /></template>
        {{ t('actions.create') }}
      </NButton>
    </template>
    <template #renderer>
      <TTableRenderer :state="crud" :show-selection="false" :translate="t" />
    </template>
  </TListShell>

  <!-- Custom create/edit modal — kept exactly as before; only refresh() →
       crud.refresh() after success (see submitForm / deactivate below). -->
  <NModal v-model:show="modalVisible" preset="card" :title="modalTitle" class="max-w-720px">
    <!-- 2-column grid layout — paired fields share a row so the modal
         fits without scroll, related concepts (discount type+value,
         max-discount+min-order, total-limit+isActive, start+end time)
         sit beside each other. `:show-feedback="false"` is replaced by
         the form's natural feedback gap so vertical rhythm comes back. -->
    <NForm
      :label-width="120"
      label-placement="left"
      require-mark-placement="right-hanging"
      :show-feedback="false"
    >
      <!-- NGrid `:y-gap="18"` gives explicit vertical rhythm between
           rows because `:show-feedback="false"` strips the default
           24px feedback line that would otherwise space items apart.
           Scoped styles can't reach into the teleported modal so we
           rely on NGrid's built-in gap props. -->
      <NGrid :cols="2" :x-gap="20" :y-gap="18" responsive="screen" item-responsive>
        <NFormItemGi span="2 s:1" :label="t('form.promotionCode')" required>
          <NInput v-model:value="form.promotionCode" :disabled="editMode" placeholder="WELCOME10" />
        </NFormItemGi>
        <NFormItemGi span="2 s:1" :label="t('form.name')" required>
          <NInput v-model:value="form.name" />
        </NFormItemGi>

        <NFormItemGi :span="2" :label="t('form.description')">
          <NInput v-model:value="form.description" type="textarea" :rows="2" />
        </NFormItemGi>

        <NFormItemGi span="2 s:1" :label="t('form.type')" required>
          <NSelect
            v-model:value="form.type"
            :options="[
              { value: 1, label: t('type.percentageDiscount') },
              { value: 2, label: t('type.fixedAmountDiscount') },
              { value: 3, label: t('type.firstSubscription') },
              { value: 4, label: t('type.limitedTime') },
              { value: 5, label: t('type.thresholdDiscount') },
            ]"
          />
        </NFormItemGi>
        <NFormItemGi span="2 s:1" :label="t('form.discountType')" required>
          <NSelect
            v-model:value="form.discountType"
            :options="[
              { value: 1, label: t('discountType.percentage') },
              { value: 2, label: t('discountType.fixedAmount') },
            ]"
          />
        </NFormItemGi>

        <NFormItemGi span="2 s:1" :label="t('form.discountValue')" required>
          <NInputNumber v-model:value="form.discountValue" :min="0" :precision="2" class="w-full" />
        </NFormItemGi>
        <NFormItemGi span="2 s:1" :label="t('form.maxDiscountAmount')">
          <NInputNumber v-model:value="form.maxDiscountAmount" :min="0" :precision="2" class="w-full" clearable />
        </NFormItemGi>

        <NFormItemGi span="2 s:1" :label="t('form.minimumOrderAmount')">
          <NInputNumber v-model:value="form.minimumOrderAmount" :min="0" :precision="2" class="w-full" clearable />
        </NFormItemGi>
        <NFormItemGi span="2 s:1" :label="t('form.totalUsageLimit')">
          <NInputNumber v-model:value="form.totalUsageLimit" :min="1" class="w-full" clearable />
        </NFormItemGi>

        <NFormItemGi span="2 s:1" :label="t('form.startTime')" required>
          <NDatePicker
            v-model:value="startTimeTs"
            type="datetime"
            class="w-full"
            clearable
          />
        </NFormItemGi>
        <NFormItemGi span="2 s:1" :label="t('form.endTime')">
          <NDatePicker
            v-model:value="endTimeTs"
            type="datetime"
            class="w-full"
            clearable
          />
        </NFormItemGi>

        <NFormItemGi v-if="editMode" span="2 s:1" :label="t('form.isActive')">
          <NSwitch v-model:value="form.isActive" />
        </NFormItemGi>
      </NGrid>
    </NForm>
    <template #footer>
      <NSpace justify="end">
        <NButton @click="modalVisible = false">{{ t('actions.cancel') }}</NButton>
        <NButton
          type="primary"
          :loading="saveLoading"
          :disabled="!form.promotionCode || !form.name || form.discountValue == null"
          @click="submitForm"
        >
          {{ t('actions.save') }}
        </NButton>
      </NSpace>
    </template>
  </NModal>
</template>

<script setup lang="ts">
import { computed, h, reactive, ref } from 'vue'
import {
  NButton,
  NDatePicker,
  NForm,
  NFormItemGi,
  NGrid,
  NInput,
  NInputNumber,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NSwitch,
  NTag,
  useMessage,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime as formatDate } from '@tnzi/core'
import { useAdminClient } from '../../plugin/client'
import {
  createPromotionBridge,
  DiscountType,
  PromotionType,
  type CreatePromotionDto,
  type PromotionDto,
} from '../../services/bridges/promotion-bridge'
import { interpolate, translatePageKey } from '../_shared/translate'
import { useCrudPage } from '../../headless/useCrudPage'
import type { ColumnDef } from '../../headless/useColumnSettings'
import TListShell from '../../components/crud/TListShell.vue'
import TTableRenderer from '../../components/crud/renderers/TTableRenderer.vue'

const bridge = createPromotionBridge({ client: useAdminClient() })
const message = useMessage()
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('payment.promotions', key), params)

// ─── Active-filter select (drives crud.setFilters) ───────────────────
const activeFilterRaw = ref<string | null>(null)
const activeFilterOptions = [
  { value: 'active', label: t('status.active') },
  { value: 'inactive', label: t('status.inactive') },
]
function onActiveFilterChange(v: string | null): void {
  const isActive: boolean | null = v === 'active' ? true : v === 'inactive' ? false : null
  crud.setFilters({ isActive })
  void crud.refresh()
}

// ─── Helper label functions ───────────────────────────────────────────
function discountLabel(value: number, type: number, currency = 'USD'): string {
  switch (type) {
    case DiscountType.Percentage: return `${value}%`
    case DiscountType.Fixed: {
      try { return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(value) }
      catch { return `${value.toFixed(2)} ${currency}` }
    }
    default: return String(value)
  }
}
function typeLabel(n: number): string {
  switch (n) {
    case PromotionType.PercentageDiscount: return t('type.percentageDiscount')
    case PromotionType.FixedAmountDiscount: return t('type.fixedAmountDiscount')
    case PromotionType.FirstSubscription: return t('type.firstSubscription')
    case PromotionType.LimitedTime: return t('type.limitedTime')
    case PromotionType.ThresholdDiscount: return t('type.thresholdDiscount')
    default: return String(n)
  }
}

// ─── Column definitions (ColumnDef[]) ────────────────────────────────
// Note: `title` is a plain string (t('cols.x')) — ColumnDef does not accept
// a thunk. `render` receives `Record<string, unknown>`; cast to PromotionDto
// inside each renderer to preserve existing VNode logic.
const tableColumns: ColumnDef[] = [
  {
    key: 'isActive',
    title: t('cols.isActive'),
    width: 100,
    render: (row) => {
      const r = row as unknown as PromotionDto
      return h(
        NTag,
        { size: 'small', bordered: false, type: r.isValid ? 'success' : r.isActive ? 'warning' : 'default' },
        () => (r.isValid ? t('status.valid') : r.isActive ? t('status.active') : t('status.inactive')),
      )
    },
  },
  {
    key: 'promotionCode',
    title: t('cols.code'),
    minWidth: 140,
    render: (row) => {
      const r = row as unknown as PromotionDto
      return h('code', { class: 'tnzi-mono text-12px font-600' }, r.promotionCode)
    },
  },
  {
    key: 'name',
    title: t('cols.name'),
    ellipsis: { tooltip: true },
  },
  {
    key: 'type',
    title: t('cols.type'),
    width: 120,
    render: (row) => {
      const r = row as unknown as PromotionDto
      return h(NTag, { size: 'tiny', bordered: false, type: 'info' }, () => typeLabel(r.type))
    },
  },
  {
    key: 'discountValue',
    title: t('cols.discount'),
    width: 110,
    align: 'right',
    render: (row) => {
      const r = row as unknown as PromotionDto
      return discountLabel(r.discountValue, r.discountType)
    },
  },
  {
    key: 'usedCount',
    title: t('cols.usage'),
    width: 110,
    align: 'right',
    render: (row) => {
      const r = row as unknown as PromotionDto
      return `${r.usedCount} / ${r.totalUsageLimit ?? '∞'}`
    },
  },
  {
    key: 'startTime',
    title: t('cols.startTime'),
    width: 170,
    render: (row) => {
      const r = row as unknown as PromotionDto
      return formatDate(r.startTime)
    },
  },
  {
    key: 'endTime',
    title: t('cols.endTime'),
    width: 170,
    render: (row) => {
      const r = row as unknown as PromotionDto
      return formatDate(r.endTime)
    },
  },
  {
    key: 'actions',
    title: t('cols.actions'),
    width: 220,
    fixed: 'right',
    align: 'right',
    render: (row) => {
      const r = row as unknown as PromotionDto
      return h('div', { class: 'flex justify-end gap-4px' }, [
        h(NButton, { size: 'tiny', tertiary: true, onClick: () => openEdit(r) }, {
          icon: () => h(TSvgIcon, { icon: 'mdi:pencil-outline', size: 12 }),
          default: () => t('actions.edit'),
        }),
        r.isActive
          ? h(
              NPopconfirm,
              { onPositiveClick: () => deactivate(r.id) },
              {
                trigger: () => h(NButton, { size: 'tiny', type: 'warning', tertiary: true }, {
                  icon: () => h(TSvgIcon, { icon: 'mdi:pause', size: 12 }),
                  default: () => t('actions.deactivate'),
                }),
                default: () => t('deactivateConfirm', { code: r.promotionCode }),
              },
            )
          : null,
      ])
    },
  },
]

// ─── Fetch-only useCrudPage (no create/update/delete callbacks) ───────
// Create/edit/deactivate are handled by the page's own NModal + bridge calls.
// The active-filter is passed via crud.setFilters({ isActive }) in
// onActiveFilterChange, then read from q.filters inside fetchData.
const crud = useCrudPage<PromotionDto>({
  pageId: 'payment.promotions',
  columns: tableColumns,
  rowKey: (r) => r.id,
  fetchData: async (q) => {
    const isActive = (q.filters.isActive as boolean | null | undefined) ?? null
    const r = await bridge.getList({
      pageIndex: q.pageIndex,
      pageSize: q.pageSize,
      isActive,
      searchText: q.searchText || null,
    })
    const pageSize = q.pageSize
    return {
      items: r.items,
      totalCount: r.totalCount,
      pageIndex: q.pageIndex,
      pageSize,
      totalPages: Math.max(1, Math.ceil(r.totalCount / pageSize)),
      hasPreviousPage: q.pageIndex > 1,
      hasNextPage: q.pageIndex * pageSize < r.totalCount,
    }
  },
  // no createData/updateData/deleteData — create/edit/deactivate are handled
  // by the page's own modal + bridge calls (then crud.refresh() is called).
})
crud.refresh().catch(() => undefined)

// ─── Create/Edit modal ────────────────────────────────────────────────
const saveLoading = ref(false)
const modalVisible = ref(false)
const editMode = ref(false)
const editId = ref<string | null>(null)
const modalTitle = computed(() => (editMode.value ? t('modal.edit') : t('modal.create')))

// Enum defaults mirror backend Tnzi.Payment.Metadata:
//   PromotionType.PercentageDiscount = 1, DiscountType.Percentage = 1
const form = reactive<CreatePromotionDto & { isActive?: boolean }>({
  promotionCode: '',
  name: '',
  description: undefined,
  type: PromotionType.PercentageDiscount,
  discountValue: 0,
  discountType: DiscountType.Percentage,
  maxDiscountAmount: undefined,
  minimumOrderAmount: undefined,
  startTime: new Date().toISOString(),
  endTime: undefined,
  totalUsageLimit: undefined,
  isActive: true,
})

const startTimeTs = computed({
  get: () => (form.startTime ? new Date(form.startTime).getTime() : null),
  set: (v: number | null) => { form.startTime = v ? new Date(v).toISOString() : new Date().toISOString() },
})
const endTimeTs = computed({
  get: () => (form.endTime ? new Date(form.endTime).getTime() : null),
  set: (v: number | null) => { form.endTime = v ? new Date(v).toISOString() : undefined },
})

function resetForm(): void {
  form.promotionCode = ''
  form.name = ''
  form.description = undefined
  form.type = PromotionType.PercentageDiscount
  form.discountValue = 0
  form.discountType = DiscountType.Percentage
  form.maxDiscountAmount = undefined
  form.minimumOrderAmount = undefined
  form.startTime = new Date().toISOString()
  form.endTime = undefined
  form.totalUsageLimit = undefined
  form.isActive = true
}

function openCreate(): void {
  resetForm()
  editMode.value = false
  editId.value = null
  modalVisible.value = true
}

function openEdit(row: PromotionDto): void {
  resetForm()
  editMode.value = true
  editId.value = row.id
  form.promotionCode = row.promotionCode
  form.name = row.name
  form.description = row.description ?? undefined
  form.type = row.type
  form.discountValue = row.discountValue
  form.discountType = row.discountType
  form.maxDiscountAmount = row.maxDiscountAmount ?? undefined
  form.minimumOrderAmount = row.minimumOrderAmount ?? undefined
  form.startTime = row.startTime
  form.endTime = row.endTime ?? undefined
  form.totalUsageLimit = row.totalUsageLimit ?? undefined
  form.isActive = row.isActive
  modalVisible.value = true
}

async function submitForm(): Promise<void> {
  saveLoading.value = true
  try {
    if (editMode.value && editId.value) {
      await bridge.update(editId.value, {
        name: form.name,
        description: form.description,
        discountValue: form.discountValue,
        discountType: form.discountType,
        maxDiscountAmount: form.maxDiscountAmount,
        minimumOrderAmount: form.minimumOrderAmount,
        startTime: form.startTime,
        endTime: form.endTime,
        totalUsageLimit: form.totalUsageLimit,
        isActive: form.isActive,
      })
      message.success(t('toast.updated'))
    } else {
      // CreatePromotionDto has no isActive field — new promotions are
      // implicitly active. Strip it from the payload to avoid sending
      // a property the C# DTO will silently ignore.
      const { isActive: _ignored, ...createPayload } = form
      void _ignored
      await bridge.create(createPayload)
      message.success(t('toast.created'))
    }
    modalVisible.value = false
    await crud.refresh()
  } catch (e) {
    message.error(t('toast.failed', { error: e instanceof Error ? e.message : String(e) }))
  } finally {
    saveLoading.value = false
  }
}

async function deactivate(id: string): Promise<void> {
  try {
    await bridge.deactivate(id)
    message.success(t('toast.deactivated'))
    await crud.refresh()
  } catch (e) {
    message.error(t('toast.failed', { error: e instanceof Error ? e.message : String(e) }))
  }
}
</script>
