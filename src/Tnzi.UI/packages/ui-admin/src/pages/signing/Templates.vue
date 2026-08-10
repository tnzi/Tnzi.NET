<template>
  <TCardPage
    :state="crud"
    :title="t('title')"
    :title-help="t('titleHelp')"
    :translate="t"
    mode="page"
    :cols="{ xs: 1, sm: 2, lg: 3 }"
    :form-modal-width="900"
    :row-actions="rowActions"
  >
    <template #card="{ item }">
      <TEntityCard class="st-card" clickable @click="void openTemplate(item as TemplateRow)">
        <div class="st-card__head">
          <div class="st-card__glyph">
            <TSvgIcon :icon="sourceIcon(item.source)" :size="18" />
          </div>
          <div class="st-card__ident">
            <span class="st-card__name" :title="item.name">{{ item.name }}</span>
            <span class="st-card__meta">
              {{ item.category || t('card.uncategorized') }} · v{{ item.version }}
            </span>
          </div>
          <TStatusBadge
            :value="item.isActive ? 'active' : 'inactive'"
            :type="item.isActive ? 'success' : 'default'"
            :label="item.isActive ? t('card.active') : t('card.inactive')"
          />
        </div>

        <div class="st-card__facts">
          <span class="st-card__fact">
            <TSvgIcon icon="mdi:file-document-outline" :size="13" />
            {{ t('card.pages', { count: item.pageCount }) }}
          </span>
          <span class="st-card__fact">
            <TSvgIcon icon="mdi:form-textbox" :size="13" />
            {{ t('card.fields', { count: item.fieldCount }) }}
          </span>
          <!-- A template with zero fields renders a document nobody can sign.
               It is a legal state while you are building one, so this is a
               warning on the tile rather than a validation error on save. -->
          <span v-if="!item.fieldCount" class="st-card__warn">
            <TSvgIcon icon="mdi:alert-outline" :size="13" />
            {{ t('card.noFields') }}
          </span>
          <span v-if="item.requiresWetSignature" class="st-card__fact">
            <TSvgIcon icon="mdi:fountain-pen-tip" :size="13" />
            {{ t('card.wetSignature') }}
          </span>
        </div>

        <template #actions>
          <TRowActions :row="(item as TemplateRow)" :actions="rowActions" :translate="t" />
        </template>
      </TEntityCard>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="templateFormSchema"
        :sections="templateFormSections"
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
 * Signing templates as tiles, not a grid.
 *
 * A template is a *thing* you recognise by name ("Retainer agreement",
 * "NDA"), so the list answers "which templates do we have and are they ready
 * to send" - field count, page count, active state - rather than inviting a
 * column-by-column comparison.
 *
 * Clicking a tile opens it for editing, which is also where the placed fields
 * live. There is no separate preview: what a template *looks like* only
 * becomes concrete once a request merges real values into it.
 */
import { h } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import TCardPage from '../../components/crud/TCardPage.vue'
import TEntityCard from '../../components/data/TEntityCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TemplateFieldsEditor from './components/TemplateFieldsEditor.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { deleteAction, type RowAction } from '../../headless/row-actions'
import { createSigningBridge } from '../../services/bridges/signing-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { type FieldRenderer } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { templateFormSchema, templateFormSections, templateColumns } from './signing-config'
import {
  TemplateSource,
  type CreateEnvelopeTemplateDto,
  type EnvelopeTemplateListDto,
  type TemplateFieldInputDto,
} from '@tnzi/core/services/signing'

type TemplateRow = EnvelopeTemplateListDto

const bridge = createSigningBridge({ client: useAdminClient() })
const t = makePageTranslator('signing.templates')
const message = useSafeMessage()

const crud = useCrudPage<EnvelopeTemplateListDto, string>({
  pageId: 'signing.templates',
  permission: 'signing.template',
  columns: templateColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.templates.fetch(query),
  // Deep-link restore (`?detail=edit:<id>`) resolves through the full template
  // for the same reason `openTemplate` hydrates: the list row has no fields.
  loadDetailById: (id) => bridge.templates.getById(id),
  createData: (data) => bridge.templates.create(toWrite(data)),
  updateData: (id, data) => bridge.templates.update(String(id), toWrite(data)),
  deleteData: (ids) => bridge.templates.delete(ids.map(String)),
})

const rowActions: RowAction<TemplateRow>[] = [
  { key: 'edit', label: 'actions.edit', show: () => crud.canUpdate !== false, onClick: (row) => void openTemplate(row) },
  // Delete returns 409 once any request has referenced the template; the page
  // does not try to predict that (the list row cannot know), it surfaces the
  // backend's answer and suggests deactivating instead.
  deleteAction(crud),
]

/**
 * ★ Hydrate before opening the editor.
 *
 * The paged list projects `EnvelopeTemplateListDto`, which carries NO fields
 * and no body. Opening the editor straight off a list row and pressing Save
 * would post `fields: []`, and the backend rebuilds the field set wholesale -
 * so every placed field on that template would be silently deleted.
 */
async function openTemplate(row: TemplateRow): Promise<void> {
  try {
    const full = await bridge.templates.getById(String(row.id))
    crud.formModal.open('edit', full as unknown as EnvelopeTemplateListDto)
  } catch (e) {
    // Deliberately does NOT fall back to opening with the list row: that is the
    // exact path that would post an empty field set and wipe the template.
    message.error(e instanceof Error ? e.message : String(e))
  }
}

const fieldRenderers: Record<string, FieldRenderer> = {
  custom: (ctx) =>
    h(TemplateFieldsEditor, {
      modelValue: (ctx.value as TemplateFieldInputDto[] | null) ?? [],
      readonly: ctx.readonly,
      translate: t,
      'onUpdate:modelValue': (v: TemplateFieldInputDto[]) => ctx.onUpdate(v),
    }),
}

/**
 * Project the form model onto the write DTO.
 *
 * Explicit rather than spreading the model: the edit form is seeded from the
 * full template, which also carries read-only projections (`version`,
 * `fieldCount`, `creationTime`). Sending those back would be noise at best and
 * a stale-version write at worst.
 */
function toWrite(data: unknown): CreateEnvelopeTemplateDto {
  const src = (data ?? {}) as Record<string, unknown>
  return {
    name: String(src.name ?? ''),
    category: (src.category as string | null) ?? null,
    source: (src.source as TemplateSource) ?? TemplateSource.Composed,
    hostEntityTypes: (src.hostEntityTypes as string | null) ?? null,
    bodyTemplate: (src.bodyTemplate as string | null) ?? null,
    sourceFileId: (src.sourceFileId as string | null) ?? null,
    sourceFileName: (src.sourceFileName as string | null) ?? null,
    renderedPdfFileId: (src.renderedPdfFileId as string | null) ?? null,
    pageCount: Number(src.pageCount ?? 1),
    requiresWetSignature: Boolean(src.requiresWetSignature),
    isActive: src.isActive === undefined ? true : Boolean(src.isActive),
    fields: (src.fields as TemplateFieldInputDto[] | undefined) ?? [],
  }
}

function sourceIcon(source?: TemplateSource): string {
  return source === TemplateSource.Uploaded ? 'mdi:file-upload-outline' : 'mdi:text-box-outline'
}
</script>

<style scoped>
.st-card {
  height: 100%;
}
.st-card :deep(.n-card__content) {
  display: flex;
  flex-direction: column;
  gap: 10px;
  height: 100%;
}
.st-card__head {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}
.st-card__glyph {
  width: 34px;
  height: 34px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: var(--tnzi-bg-deep);
  color: var(--tnzi-base-text-muted);
}
.st-card__ident {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.st-card__name {
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.st-card__meta {
  font-size: 11.5px;
  color: var(--tnzi-base-text-muted);
}
.st-card__facts {
  flex: 1 1 auto;
  display: flex;
  flex-wrap: wrap;
  gap: 6px 14px;
  align-content: flex-start;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.st-card__fact,
.st-card__warn {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}
.st-card__warn {
  color: var(--tnzi-warning, #f0a020);
}
</style>
