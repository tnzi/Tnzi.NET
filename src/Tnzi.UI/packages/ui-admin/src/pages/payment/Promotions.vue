<template>
  <!--
    Promotions — promotion/coupon CRUD wired to /admin/promotions via the
    standard useCrudPage + TCrudPage pattern. Create/edit render through
    TFormSchemaRenderer (promotion-config `promotionFormSchema`); the datetime
    fields use a `promoDate` field renderer (ISO string ⇄ picker timestamp).
    Deactivate is a declarative RowAction confirm.

    The backend `PromotionQueryDto` has no free-text field, so the default
    keyword box is disabled; an active/inactive filter sits in the toolbar.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :show-default-search="false"
    :title="t('title')"
    :translate="t"
    :row-actions="rowActions"
    :form-modal-width="720"
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
      <NButton v-if="crud.canCreate" size="small" type="primary" tertiary class="t-list-shell__action" @click="openCreate">
        <template #icon><TSvgIcon icon="mdi:plus" :size="16" /></template>
        {{ t('actions.create') }}
      </NButton>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="promotionFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
        :field-renderers="{ promoDate: datetimeRenderer }"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { h, ref } from 'vue'
import { NButton, NDatePicker, NSelect } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { useAdminClient } from '../../plugin/client'
import {
  createPromotionBridge,
  DiscountType,
  PromotionType,
  type PromotionDto,
} from '../../services/bridges/promotion-bridge'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, type RowAction } from '../../headless/rowActions'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TFormSchemaRenderer, { type FieldRenderer } from '../_shared/form-schema'
import { buildPromotionColumns, promotionFormSchema } from './promotion-config'

const t = makePageTranslator('payment.promotions')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const bridge = createPromotionBridge({ client: useAdminClient() })

const columns = buildPromotionColumns(t)

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

const crud = useCrudPage<PromotionDto>({
  pageId: 'payment.promotions',
  permission: 'payment.promotion',
  columns,
  rowKey: (r) => r.id,
  fetchData: async (q) => {
    const isActive = (q.filters.isActive as boolean | null | undefined) ?? null
    const r = await bridge.getList({ pageIndex: q.pageIndex, pageSize: q.pageSize, isActive })
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
  createData: (data) => bridge.create(data),
  updateData: (id, data) => bridge.update(String(id), data),
})

// Custom create trigger — seed the required enum defaults so the create form
// opens with a valid Type / DiscountType selection (TCrudPage's default
// openCreate would open an empty `{}` and the required enum selects can't
// self-default).
function openCreate(): void {
  crud.formModal.open('create', {
    type: PromotionType.PercentageDiscount,
    discountType: DiscountType.Percentage,
    discountValue: 0,
    stackable: false,
    priority: 0,
  } as PromotionDto)
}

// `startTime` / `endTime` are ISO strings on PromotionDto; the picker works in
// epoch millis, so convert both ways here.
const datetimeRenderer: FieldRenderer = (ctx) =>
  h(NDatePicker, {
    type: 'datetime',
    value: ctx.value ? new Date(ctx.value as string).getTime() : null,
    clearable: true,
    disabled: ctx.readonly,
    class: 'w-full',
    'onUpdate:value': (v: number | null) => ctx.onUpdate(v ? new Date(v).toISOString() : null),
  })

async function deactivate(id: string): Promise<void> {
  try {
    await bridge.deactivate(id)
    message.success(t('toast.deactivated'))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

const rowActions: RowAction<PromotionDto>[] = [
  editAction(crud),
  {
    key: 'deactivate',
    label: 'actions.deactivate',
    type: 'warning',
    icon: 'mdi:pause',
    show: (r) => can('payment.promotion.update') && r.isActive,
    confirm: (r) => t('deactivateConfirm', { code: r.promotionCode }),
    onClick: (r) => void deactivate(r.id),
  },
]
</script>
