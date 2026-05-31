<template>
  <!--
    PaymentPromotion — promotion/coupon CRUD wired to /admin/promotions.
    Header search + active-status filter, list table with per-row Edit /
    Deactivate. Create + edit modal exposes the most-used fields (code,
    name, type, discount value/type, validity window, usage caps,
    activation flag). Less common knobs (per-user limit / priority /
    stackable / firstSubscriptionOnly) sit in an "advanced" section.
  -->
  <div class="t-promo-page t-stack-page">
    <div class="t-promo-page__header">
      <div class="t-promo-page__title">
        <TSvgIcon icon="mdi:ticket-percent-outline" :size="20" />
        <h2>{{ t('title') }}</h2>
      </div>
      <div class="t-promo-page__toolbar">
        <NInput
          v-model:value="searchText"
          :placeholder="t('filter.search')"
          clearable
          size="medium"
          style="width: 220px"
          @keyup.enter="refresh"
        >
          <template #prefix><TSvgIcon icon="mdi:magnify" :size="14" /></template>
        </NInput>
        <NSelect
          v-model:value="activeFilterRaw"
          :options="activeFilterOptions"
          :placeholder="t('filter.activeAny')"
          clearable
          size="medium"
          style="width: 160px"
          @update:value="onActiveFilterChange"
        />
        <NButton size="small" :loading="loading" @click="refresh">
          <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
          {{ t('actions.refresh') }}
        </NButton>
        <NButton size="small" type="primary" @click="openCreate">
          <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
          {{ t('actions.create') }}
        </NButton>
      </div>
    </div>

    <NCard size="small" :bordered="false" class="t-table-card">
      <NDataTable
        :columns="columns"
        :data="rows"
        :loading="loading"
        :pagination="{
          page: pageIndex,
          pageSize,
          itemCount: totalCount,
          onUpdatePage: (p: number) => { pageIndex = p; refresh() },
        }"
        :bordered="false"
        remote
        size="small"
        :flex-height="true"
      />
    </NCard>

    <NModal v-model:show="modalVisible" preset="card" :title="modalTitle" style="max-width: 720px">
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
        class="t-promo-form"
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
            <NInputNumber v-model:value="form.discountValue" :min="0" :precision="2" style="width: 100%" />
          </NFormItemGi>
          <NFormItemGi span="2 s:1" :label="t('form.maxDiscountAmount')">
            <NInputNumber v-model:value="form.maxDiscountAmount" :min="0" :precision="2" style="width: 100%" clearable />
          </NFormItemGi>

          <NFormItemGi span="2 s:1" :label="t('form.minimumOrderAmount')">
            <NInputNumber v-model:value="form.minimumOrderAmount" :min="0" :precision="2" style="width: 100%" clearable />
          </NFormItemGi>
          <NFormItemGi span="2 s:1" :label="t('form.totalUsageLimit')">
            <NInputNumber v-model:value="form.totalUsageLimit" :min="1" style="width: 100%" clearable />
          </NFormItemGi>

          <NFormItemGi span="2 s:1" :label="t('form.startTime')" required>
            <NDatePicker
              v-model:value="startTimeTs"
              type="datetime"
              style="width: 100%"
              clearable
            />
          </NFormItemGi>
          <NFormItemGi span="2 s:1" :label="t('form.endTime')">
            <NDatePicker
              v-model:value="endTimeTs"
              type="datetime"
              style="width: 100%"
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
  </div>
</template>

<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
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
import type { DataTableColumns } from 'naive-ui'
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

const bridge = createPromotionBridge({ client: useAdminClient() })
const message = useMessage()
const t = (key: string, params?: Record<string, unknown>) =>
  interpolate(translatePageKey('payment.promotions', key), params)

const loading = ref(false)
const saveLoading = ref(false)
const rows = ref<PromotionDto[]>([])
const totalCount = ref(0)
const pageIndex = ref(1)
const pageSize = ref(20)
const searchText = ref('')
const activeFilter = ref<boolean | null>(null)
const activeFilterRaw = ref<string | null>(null)
const activeFilterOptions = [
  { value: 'active', label: t('status.active') },
  { value: 'inactive', label: t('status.inactive') },
]
function onActiveFilterChange(v: string | null): void {
  activeFilter.value = v === 'active' ? true : v === 'inactive' ? false : null
  void refresh()
}

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
const columns: DataTableColumns<PromotionDto> = [
  {
    title: () => t('cols.isActive'),
    key: 'isActive',
    width: 100,
    render: (row) =>
      h(
        NTag,
        { size: 'small', bordered: false, type: row.isValid ? 'success' : row.isActive ? 'warning' : 'default' },
        () => (row.isValid ? t('status.valid') : row.isActive ? t('status.active') : t('status.inactive')),
      ),
  },
  {
    title: () => t('cols.code'),
    key: 'promotionCode',
    width: 180,
    render: (row) => h('code', { style: 'font-family: var(--tnzi-font-mono); font-size: 12px; font-weight: 600;' }, row.promotionCode),
  },
  { title: () => t('cols.name'), key: 'name', ellipsis: { tooltip: true } },
  {
    title: () => t('cols.type'),
    key: 'type',
    width: 120,
    render: (row) => h(NTag, { size: 'tiny', bordered: false, type: 'info' }, () => typeLabel(row.type)),
  },
  {
    title: () => t('cols.discount'),
    key: 'discountValue',
    width: 110,
    align: 'right',
    render: (row) => discountLabel(row.discountValue, row.discountType),
  },
  {
    title: () => t('cols.usage'),
    key: 'usedCount',
    width: 110,
    align: 'right',
    render: (row) => `${row.usedCount} / ${row.totalUsageLimit ?? '∞'}`,
  },
  {
    title: () => t('cols.startTime'),
    key: 'startTime',
    width: 170,
    render: (row) => formatDate(row.startTime),
  },
  {
    title: () => t('cols.endTime'),
    key: 'endTime',
    width: 170,
    render: (row) => formatDate(row.endTime),
  },
  {
    title: () => t('cols.actions'),
    key: 'actions',
    width: 220,
    align: 'right',
    fixed: 'right',
    render: (row) =>
      h('div', { style: 'display: flex; justify-content: flex-end; gap: 4px;' }, [
        h(NButton, { size: 'tiny', tertiary: true, onClick: () => openEdit(row) }, {
          icon: () => h(TSvgIcon, { icon: 'mdi:pencil-outline', size: 12 }),
          default: () => t('actions.edit'),
        }),
        row.isActive
          ? h(
              NPopconfirm,
              { onPositiveClick: () => deactivate(row.id) },
              {
                trigger: () => h(NButton, { size: 'tiny', type: 'warning', tertiary: true }, {
                  icon: () => h(TSvgIcon, { icon: 'mdi:pause', size: 12 }),
                  default: () => t('actions.deactivate'),
                }),
                default: () => t('deactivateConfirm', { code: row.promotionCode }),
              },
            )
          : null,
      ]),
  },
]

// ─── Create/Edit modal ────────────────────────────────────────────
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
    await refresh()
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
    await refresh()
  } catch (e) {
    message.error(t('toast.failed', { error: e instanceof Error ? e.message : String(e) }))
  }
}

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const result = await bridge.getList({
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      isActive: activeFilter.value,
      searchText: searchText.value || null,
    })
    rows.value = result.items
    totalCount.value = result.totalCount
  } catch {
    rows.value = []
    totalCount.value = 0
  } finally {
    loading.value = false
  }
}

onMounted(() => { void refresh() })
</script>

<style scoped>
/* Layout shell from shared `.t-stack-page` + `.t-table-card` utilities. */
.t-promo-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  flex-wrap: wrap;
  padding: 16px 20px;
  background: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  border-radius: 8px;
}
.t-promo-page__title {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-promo-page__title h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}
.t-promo-page__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
</style>
