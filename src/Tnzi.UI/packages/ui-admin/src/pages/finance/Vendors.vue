<template>
  <TCardPage
    :state="crud"
    :title="title"
    :row-actions="rowActions"
    :translate="t"
    mode="page"
    :cols="{ xs: 1, sm: 2, lg: 3, xl: 4 }"
    :form-modal-width="760"
  >
    <template #card="{ item }">
      <TEntityCard
        class="party-card"
        :class="{ 'party-card--off': item.isActive === false }"
        clickable
        @click="openDetail(item)"
      >
        <div class="party-card__head">
          <TAvatar :name="item.name" :size="40" shape="rounded" icon="mdi:truck-outline" />
          <div class="party-card__ident">
            <span class="party-card__name" :title="item.name">{{ item.name || EMPTY_DASH }}</span>
            <code v-if="item.code" class="party-card__code">{{ item.code }}</code>
          </div>
          <TStatusBadge
            :value="item.isActive ?? true"
            :mapping="{
              true: { type: 'success', labelKey: 'admin.shared.status.active' },
              false: { type: 'default', labelKey: 'admin.shared.status.inactive' },
            }"
          />
        </div>

        <div class="party-card__contact">
          <span class="party-card__line" :title="item.email ?? ''">
            <TSvgIcon icon="mdi:email-outline" :size="13" />{{ item.email || EMPTY_DASH }}
          </span>
          <span class="party-card__line">
            <TSvgIcon icon="mdi:phone-outline" :size="13" />{{ item.phone || EMPTY_DASH }}
          </span>
        </div>

        <div class="party-card__foot">
          <span class="party-card__term">
            <TSvgIcon icon="mdi:cash-clock" :size="13" />{{ termsLabel(item) }}
          </span>
          <NTag v-if="item.currency" size="small" round :bordered="false">{{ item.currency }}</NTag>
        </div>

        <template #actions>
          <TRowActions :row="item" :actions="rowActions" :translate="t" />
        </template>
      </TEntityCard>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="vendorFormSchema"
        :sections="vendorFormSections"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>
  </TCardPage>
</template>

<script setup lang="ts">
/**
 * Vendors as cards, mirroring the Customers page exactly.
 *
 * The two party lists answer the same question with the same fields, so they
 * get the same shape (and share the `.party-card` rules) rather than drifting
 * into two different-looking pages over the same data.
 */
import { NTag } from 'naive-ui'
import { TAvatar, TSvgIcon } from '@tnzi/ui'
import TCardPage from '../../components/crud/TCardPage.vue'
import TEntityCard from '../../components/data/TEntityCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { EMPTY_DASH } from '../../utils/placeholders'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createFinanceBridge, type UpdateVendorDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import { useRouter } from 'vue-router'
import TFormSchemaRenderer from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import {
  buildVendorColumns,
  vendorFormSchema,
  vendorFormSections,
  type VendorRow,
} from './vendor-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const router = useRouter()
const t = makePageTranslator('finance.vendors')

// Columns still drive export + the mobile card fallback's field list.
const columns = buildVendorColumns(t, openDetail)

function toPayload(d: Record<string, unknown>): UpdateVendorDto {
  return {
    name: String(d.name ?? '').trim(),
    code: (d.code as string | null) || null,
    email: (d.email as string | null) || null,
    phone: (d.phone as string | null) || null,
    address: (d.address as string | null) || null,
    currency: typeof d.currency === 'string' && d.currency.trim() ? d.currency.trim().toUpperCase() : null,
    paymentTermsDays: d.paymentTermsDays == null ? null : Number(d.paymentTermsDays),
    notes: (d.notes as string | null) || null,
    isActive: d.isActive !== false,
  }
}

const crud = useCrudPage<VendorRow>({
  pageId: 'finance.vendors',
  permission: 'finance.vendor',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.vendors.fetch(q),
  createData: (d) => bridge.vendors.create(toPayload(d)),
  updateData: (id, d) => bridge.vendors.update(String(id), toPayload(d)),
  deleteData: (ids) => bridge.vendors.delete(ids.map(String)),
})

/** Drill into the work surface - the list answers "which one", the page answers everything else. */
function openDetail(r: VendorRow): void {
  if (!r.id) return
  void router.push({ name: 'finance.vendors.detail', params: { id: String(r.id) } })
}

/** "Net 30" reads as terms; a bare number reads as an id. */
function termsLabel(row: VendorRow): string {
  return row.paymentTermsDays == null
    ? t('admin.shared.card.noTerms')
    : t('admin.shared.card.netDays', { days: row.paymentTermsDays })
}

const title = 'tnzi.admin.modules.finance.vendors.title'
const rowActions: RowAction<VendorRow>[] = [
  // No Open action: the whole card already opens the work surface.
  editAction(crud),
  deleteAction(crud),
]
</script>

<style scoped>
.party-card {
  height: 100%;
}
.party-card :deep(.n-card__content) {
  display: flex;
  flex-direction: column;
  gap: 10px;
  height: 100%;
}
/* An inactive party stays listed but stops reading as someone you trade with. */
.party-card--off {
  opacity: 0.7;
}
.party-card__head {
  display: flex;
  align-items: center;
  gap: 10px;
}
.party-card__ident {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.party-card__name {
  font-size: 14.5px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.party-card__code {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 11.5px;
  color: var(--tnzi-base-text-muted);
}
.party-card__contact {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}
.party-card__line {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  font-size: 12.5px;
  color: var(--tnzi-base-text-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.party-card__foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: auto;
  padding-top: 8px;
  border-top: 1px solid var(--tnzi-border);
}
.party-card__term {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
</style>
