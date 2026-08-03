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
          <TAvatar :name="item.name" :size="40" shape="rounded" icon="mdi:account-tie-outline" />
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

        <!-- How to reach them: the two fields anyone opening a party record
             actually wants, which the table had pushed behind three ID columns. -->
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
        :schema="customerFormSchema"
        :sections="customerFormSections"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :field-renderers="fieldRenderers"
        :translate="t"
      />
    </template>
  </TCardPage>
</template>

<script setup lang="ts">
/**
 * Customers as cards.
 *
 * A customer is an organisation you deal with, and the list exists to find one
 * and open it. The table gave equal column weight to code, name, email, phone,
 * currency, terms and status, so nothing led and the contact details (the fields
 * people actually come here for) were the first to truncate. Cards put the name
 * and status on top, the two ways to reach them in the middle, and the billing
 * terms along the footer; the whole card opens the work surface.
 */
import { onMounted } from 'vue'
import { NTag } from 'naive-ui'
import { TAvatar, TSvgIcon } from '@tnzi/ui'
import TCardPage from '../../components/crud/TCardPage.vue'
import TEntityCard from '../../components/data/TEntityCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { EMPTY_DASH } from '../../utils/placeholders'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/row-actions'
import { createFinanceBridge, type UpdateCustomerDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import { useRouter } from 'vue-router'
import TFormSchemaRenderer, { selectRenderer } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { createFinanceOptionSources } from './options'
import {
  buildCustomerColumns,
  customerFormSchema,
  customerFormSections,
  type CustomerRow,
} from './customer-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const router = useRouter()
const t = makePageTranslator('finance.customers')
const sources = createFinanceOptionSources(bridge)

// Columns still drive export + the mobile card fallback's field list, so the
// definition stays even though this page no longer renders a table.
const columns = buildCustomerColumns(t, openDetail)

function toPayload(d: Record<string, unknown>): UpdateCustomerDto {
  return {
    name: String(d.name ?? '').trim(),
    code: (d.code as string | null) || null,
    email: (d.email as string | null) || null,
    phone: (d.phone as string | null) || null,
    billingAddress: (d.billingAddress as string | null) || null,
    shippingAddress: (d.shippingAddress as string | null) || null,
    currency: typeof d.currency === 'string' && d.currency.trim() ? d.currency.trim().toUpperCase() : null,
    paymentTermsDays: d.paymentTermsDays == null ? null : Number(d.paymentTermsDays),
    defaultTaxCodeId: (d.defaultTaxCodeId as string | null) || null,
    notes: (d.notes as string | null) || null,
    isActive: d.isActive !== false,
  }
}

const crud = useCrudPage<CustomerRow>({
  pageId: 'finance.customers',
  permission: 'finance.customer',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.customers.fetch(q),
  createData: (d) => bridge.customers.create(toPayload(d)),
  updateData: (id, d) => bridge.customers.update(String(id), toPayload(d)),
  deleteData: (ids) => bridge.customers.delete(ids.map(String)),
})

/** Drill into the work surface - the list answers "which one", the page answers everything else. */
function openDetail(r: CustomerRow): void {
  if (!r.id) return
  void router.push({ name: 'finance.customers.detail', params: { id: String(r.id) } })
}

/** "Net 30" reads as terms; a bare number reads as an id. */
function termsLabel(row: CustomerRow): string {
  return row.paymentTermsDays == null
    ? t('admin.shared.card.noTerms')
    : t('admin.shared.card.netDays', { days: row.paymentTermsDays })
}

const title = 'tnzi.admin.modules.finance.customers.title'
const rowActions: RowAction<CustomerRow>[] = [
  // No Open action: the whole card already opens the work surface.
  editAction(crud),
  deleteAction(crud),
]

// Default tax code - lazily-loaded tax-code options via the shared factory.
const fieldRenderers = {
  'finance-tax-code': selectRenderer(() => sources.taxCodeOptions.value, { placeholder: t('form.taxCodePlaceholder') }),
}

onMounted(() => {
  void sources.ensureTaxCodes()
})
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
