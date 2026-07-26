<template>
  <TCardPage
    :state="crud"
    :title="title"
    :translate="t"
    mode="page"
    :cols="{ xs: 1, sm: 2, lg: 3, xl: 4 }"
    :search-fields="tenantSearchFields"
    :form-modal-width="680"
  >
    <template #card="{ item }">
      <TEntityCard class="tenant-card" :class="{ 'tenant-card--off': item.isEnabled === false }">
        <div class="tenant-card__head">
          <TAvatar :name="item.name" :size="40" shape="rounded" icon="mdi:domain" />
          <div class="tenant-card__ident">
            <span class="tenant-card__name" :title="item.name">{{ item.name }}</span>
            <code class="tenant-card__code">{{ item.code || EMPTY_DASH }}</code>
          </div>
          <TStatusBadge
            :value="item.isEnabled ?? false"
            :mapping="{
              true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
              false: { type: 'warning', labelKey: 'admin.shared.status.disabled' },
            }"
          />
        </div>

        <p class="tenant-card__remark">{{ item.remark || EMPTY_DASH }}</p>

        <div class="tenant-card__foot">
          <span class="tenant-card__fact">
            <TSvgIcon icon="mdi:calendar-plus" :size="13" />
            <TRelativeTime :value="item.creationTime" />
          </span>
          <!-- Expiry is the one date that changes what an admin does next, so it
               is called out (and turns amber once it has passed) instead of
               sitting in a column beside five other timestamps. -->
          <span class="tenant-card__fact" :class="{ 'tenant-card__fact--warn': isExpired(item) }">
            <TSvgIcon icon="mdi:calendar-remove-outline" :size="13" />
            <template v-if="item.expiredAt">
              <TRelativeTime :value="item.expiredAt" />
            </template>
            <template v-else>{{ t('neverExpires') }}</template>
          </span>
        </div>

        <template #actions>
          <TRowActions :row="item" :actions="rowActions" :translate="t" />
        </template>
      </TEntityCard>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="tenantFormSchema"
        :sections="tenantFormSections"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>
  </TCardPage>
</template>

<script setup lang="ts">
/**
 * Tenants as cards.
 *
 * A deployment has a handful of tenants, each an organisation with a face and a
 * lifecycle, and the questions asked of the list are "who is on here, are they
 * live, when do they lapse". Six table columns answered none of those at a
 * glance and buried the remark. Cards give each tenant its own block with the
 * name, code, live state and expiry together.
 */
import { TAvatar, TRelativeTime, TSvgIcon } from '@tnzi/ui'
import TCardPage from '../../components/crud/TCardPage.vue'
import TEntityCard from '../../components/data/TEntityCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { EMPTY_DASH } from '../../utils/placeholders'
import TFormSchemaRenderer from '../_shared/form-schema'
import {
  tenantColumns,
  tenantFormSchema,
  tenantFormSections,
  tenantSearchFields,
} from './tenant-config'
import { makePageTranslator } from '../_shared/translate'
import type { TenantDto } from '@tnzi/core/services/identity'

const title = 'title'
const bridge = createIdentityBridge({ client: useAdminClient() })

const crud = useCrudPage<TenantDto, string>({
  pageId: 'identity.tenants',
  permission: 'tenant',
  columns: tenantColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.tenants.fetch(query),
  createData: (data) => bridge.tenants.create(data as never),
  updateData: (id, data) => bridge.tenants.update(id, data as never),
  deleteData: (ids) => bridge.tenants.delete(ids),
})

const rowActions: RowAction<TenantDto>[] = [editAction(crud), deleteAction(crud)]

/** A lapsed tenant still renders; it just stops reading as a live one. */
function isExpired(row: TenantDto): boolean {
  if (!row.expiredAt) return false
  const ts = Date.parse(String(row.expiredAt))
  return Number.isFinite(ts) && ts < Date.now()
}

const t = makePageTranslator('identity.tenants')
</script>

<style scoped>
.tenant-card {
  height: 100%;
}
.tenant-card :deep(.n-card__content) {
  display: flex;
  flex-direction: column;
  gap: 10px;
  height: 100%;
}
/* A disabled tenant reads as retired without disappearing from the grid. */
.tenant-card--off {
  opacity: 0.72;
}
.tenant-card__head {
  display: flex;
  align-items: center;
  gap: 10px;
}
.tenant-card__ident {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.tenant-card__name {
  font-size: 14.5px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.tenant-card__code {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 11.5px;
  color: var(--tnzi-base-text-muted);
}
.tenant-card__remark {
  margin: 0;
  font-size: 12.5px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  min-height: 2.6em;
}
.tenant-card__foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  flex-wrap: wrap;
  margin-top: auto;
  padding-top: 8px;
  border-top: 1px solid var(--tnzi-border);
}
.tenant-card__fact {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.tenant-card__fact--warn {
  color: var(--tnzi-warning);
}
</style>
