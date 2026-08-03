<template>
  <TCardPage
    :state="crud"
    :title="title"
    :translate="t"
    mode="page"
    :cols="{ xs: 1, sm: 2, lg: 3, xl: 4 }"
    :form-modal-width="720"
  >
    <template #card="{ item }">
      <TEntityCard class="feat-card">
        <div class="feat-card__head">
          <div class="feat-card__glyph" :class="`feat-card__glyph--${valueTypeTone(item.valueType)}`">
            <TSvgIcon :icon="valueTypeIcon(item.valueType)" :size="18" />
          </div>
          <div class="feat-card__ident">
            <span class="feat-card__name" :title="item.displayName || item.name">
              {{ item.displayName || item.name }}
            </span>
            <span class="feat-card__code" :title="item.name">{{ item.name }}</span>
          </div>
          <TStatusBadge
            :value="Boolean(item.isEnabled)"
            :mapping="{
              true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
              false: { type: 'warning', labelKey: 'admin.shared.status.disabled' },
            }"
          />
        </div>

        <p class="feat-card__desc">{{ item.description || EMPTY_DASH }}</p>

        <div class="feat-card__facts">
          <span class="feat-card__fact">
            <span class="feat-card__fact-label">{{ t('columns.valueType') }}</span>
            <code class="feat-card__mono">{{ item.valueType || EMPTY_DASH }}</code>
          </span>
          <span class="feat-card__fact">
            <span class="feat-card__fact-label">{{ t('columns.defaultValue') }}</span>
            <code class="feat-card__mono">{{ item.defaultValue || EMPTY_DASH }}</code>
          </span>
        </div>

        <div class="feat-card__foot">
          <NTag v-if="item.group" size="small" round :bordered="false">{{ item.group }}</NTag>
          <NTag v-if="item.parentName" size="small" round :bordered="false" type="info">
            {{ item.parentName }}
          </NTag>
          <TSourceBadge class="feat-card__source" :value="String(item.source ?? 'Database')" />
        </div>

        <template v-if="!item.isReadOnly" #actions>
          <TRowActions :row="item" :actions="rowActions" :translate="t" />
        </template>
      </TEntityCard>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="featureFormSchema"
        :sections="featureFormSections"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view' || ((formData as FeatureDto | null)?.isReadOnly ?? false)"
        :translate="t"
      />
    </template>
  </TCardPage>
</template>

<script setup lang="ts">
/**
 * Feature definitions - a card grid, not a table.
 *
 * A feature definition is read as a THING ("what is it, what does it default
 * to, who owns it"), not compared column-by-column against its neighbours. As a
 * table it spent seven columns restating one short record and still hid the
 * description entirely. As cards each definition is one legible unit: display
 * name over its code, the description in full, type/default as a fact pair, and
 * group / parent / source as chips along the footer.
 */
import { NTag } from 'naive-ui'
import { TSvgIcon, TSourceBadge } from '@tnzi/ui'
import TCardPage from '../../components/crud/TCardPage.vue'
import TEntityCard from '../../components/data/TEntityCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TFormSchemaRenderer from '../_shared/form-schema'
import { EMPTY_DASH } from '../../utils/placeholders'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/row-actions'
import { createSystemBridge, type FeatureDto } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { featureColumns, featureFormSchema, featureFormSections } from './feature-config'

const bridge = createSystemBridge({ client: useAdminClient() })

const crud = useCrudPage<FeatureDto>({
  pageId: 'system.features',
  permission: 'feature',
  columns: featureColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.features.fetch(q),
  createData: (d) => bridge.features.create(d as never),
  updateData: (id, d) => bridge.features.update(String(id), d as never),
  deleteData: (ids) => bridge.features.delete(ids.map(String)),
})

// Code-source rows (`isReadOnly`) ship from IFeatureDefinitionProvider - backend
// rejects edit/delete on them. Hide both actions so users don't click into a
// confusing server error.
const rowActions: RowAction<FeatureDto>[] = [
  editAction(crud, { show: (row) => !row.isReadOnly }),
  deleteAction(crud, { show: (row) => !row.isReadOnly }),
]

/** Glyph + tint per value type, so the grid is scannable by shape as well as text. */
function valueTypeIcon(type?: string): string {
  switch (type) {
    case 'Boolean': return 'mdi:toggle-switch-outline'
    case 'Integer': return 'mdi:numeric'
    default: return 'mdi:format-text'
  }
}
function valueTypeTone(type?: string): string {
  switch (type) {
    case 'Boolean': return 'success'
    case 'Integer': return 'info'
    default: return 'default'
  }
}

const title = 'title'
const t = makePageTranslator('system.features')
</script>

<style scoped>
.feat-card {
  height: 100%;
}
.feat-card :deep(.n-card__content) {
  display: flex;
  flex-direction: column;
  gap: 10px;
  height: 100%;
}
.feat-card__head {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}
.feat-card__glyph {
  width: 34px;
  height: 34px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: rgb(23 38 60 / 0.06);
  color: var(--tnzi-base-text-muted);
}
.feat-card__glyph--success { background: rgb(var(--tnzi-success-rgb) / 0.12); color: var(--tnzi-success); }
.feat-card__glyph--info { background: rgb(var(--tnzi-info-rgb) / 0.12); color: var(--tnzi-info); }
.feat-card__ident {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.feat-card__name {
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.feat-card__code {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 11.5px;
  color: var(--tnzi-base-text-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.feat-card__desc {
  margin: 0;
  font-size: 12.5px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  /* Keeps every card the same height whether or not it carries a description. */
  min-height: 2.6em;
}
.feat-card__facts {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 16px;
}
.feat-card__fact {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  font-size: 12px;
}
.feat-card__fact-label {
  color: var(--tnzi-base-text-muted);
}
.feat-card__mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 11.5px;
  padding: 1px 6px;
  border-radius: 4px;
  background: var(--tnzi-bg-deep, #f6f8fa);
  color: var(--tnzi-base-text);
  max-width: 140px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.feat-card__foot {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: auto;
  padding-top: 8px;
  border-top: 1px solid var(--tnzi-border);
}
.feat-card__source {
  margin-left: auto;
}
</style>
