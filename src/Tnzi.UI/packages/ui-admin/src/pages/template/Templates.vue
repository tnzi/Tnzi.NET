<template>
  <!--
    Templates - the cross-module template library (email, sms, cheque, …).

    Clicking a card renders the template through `bridge.templates.render(id, {})`
    into a deep-linkable preview modal (`?preview=view:<id>`) - that IS the
    preview, so there is no separate Preview button. Clone / Edit / Delete are the
    row actions; FileSystem-source templates ship with the binaries and the
    backend rejects edit + delete on them, so those two hide themselves.
  -->
  <TCardPage
    :state="crud"
    :title="t('title')"
    :title-help="t('titleHelp')"
    :translate="t"
    mode="page"
    :cols="{ xs: 1, sm: 2, lg: 3 }"
    :form-modal-width="860"
    :row-actions="rowActions"
  >
    <!-- A template is a piece of content, so the card shows a slice of the body
         instead of listing its metadata across seven columns. Clicking it opens
         the rendered preview, which is what anyone browsing this library is
         actually after. -->
    <template #card="{ item }">
      <TEntityCard class="tpl-card" clickable @click="openPreview(item as TemplateRow)">
        <div class="tpl-card__head">
          <div class="tpl-card__glyph">
            <TSvgIcon :icon="categoryIcon(item.category)" :size="18" />
          </div>
          <div class="tpl-card__ident">
            <span class="tpl-card__name" :title="item.templateName">{{ item.templateName }}</span>
            <span class="tpl-card__module">{{ item.module }}<template v-if="item.category"> · {{ item.category }}</template></span>
          </div>
          <TSourceBadge :value="item.isReadOnly ? 'FileSystem' : 'Database'" />
        </div>

        <p v-if="item.subjectTemplate" class="tpl-card__subject" :title="item.subjectTemplate">
          {{ item.subjectTemplate }}
        </p>

        <!-- Body slice, plain text: the raw markup is the point of a template
             list, and rendering it here would fight the card layout. -->
        <pre class="tpl-card__body">{{ bodyExcerpt(item) }}</pre>

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
        :translate="t"
      />
    </template>
  </TCardPage>

  <!-- Preview overlay - rendered template HTML, deep-linkable via ?preview=view:<id> -->
  <TDetailHost :state="previewDetail" :title="t('previewTitle')" :width="640" :footer="false" :translate="t">
    <template #default>
      <!-- Sandboxed: the rendered template is author-controlled markup, and
           `v-html` would run it at the admin's origin in an authenticated
           session. See THtmlPreview. -->
      <THtmlPreview
        v-if="previewContent"
        :html="previewContent"
        :height="'60vh'"
        :title="t('previewTitle')"
      />
      <p v-else class="t-template-preview__empty">{{ t('admin.common.noPreview') }}</p>
    </template>
  </TDetailHost>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { TSvgIcon, TSourceBadge } from '@tnzi/ui'
import TCardPage from '../../components/crud/TCardPage.vue'
import TEntityCard from '../../components/data/TEntityCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import THtmlPreview from '../../components/display/THtmlPreview.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/row-actions'
import { createTemplateBridge } from '../../services/bridges/template-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { templateColumns, templateFormSchema, templateFormSections } from './template-config'
import { makePageTranslator } from '../_shared/translate'
import type { TemplateInfoDto } from '@tnzi/core/services/template'

type TemplateRow = TemplateInfoDto & { id: string }

const bridge = createTemplateBridge({ client: useAdminClient() })
const { can } = usePermissionGuard()

const crud = useCrudPage<TemplateInfoDto, string>({
  pageId: 'template.templates',
  permission: 'template.template',
  columns: templateColumns,
  // File-source rows have no DB id (backend returns Guid.Empty); use the
  // module + name as the row key so selection still works.
  rowKey: (r) => {
    const id = String(r.id ?? '')
    return id && id !== '00000000-0000-0000-0000-000000000000'
      ? id
      : `file:${r.module}/${r.category || ''}/${r.templateName}`
  },
  fetchData: (query) => bridge.templates.fetch(query),
  createData: async (data) => bridge.templates.create(data as Partial<TemplateInfoDto>),
  updateData: async (id, data) => bridge.templates.update(id, data as Partial<TemplateInfoDto>),
  deleteData: async (ids) => bridge.templates.delete(ids),
})

// FileSystem-source templates ship with the binaries and the backend rejects
// edit/delete on them. Hide those actions to avoid 4xx.
//
// The list response already projects `subjectTemplate` / `contentTemplate`
// (QueryTemplatesAsync uses `ProjectTo<TemplateEntity, TemplateInfoDto>()`,
// no ignore config), so the edit/view form reads real body content directly
// from the row - no getById hydration needed.
const rowActions: RowAction<TemplateRow>[] = [
  editAction(crud, { show: (row) => !(row as TemplateRow).isReadOnly }),
  // No Preview action: clicking the card IS the preview.
  { key: 'clone', label: 'actions.clone', show: () => can('template.template.create'), onClick: (row) => void handleClone(row) },
  deleteAction(crud, { show: (row) => !(row as TemplateRow).isReadOnly }),
]

// Preview overlay state
const previewDetail = useDetail<TemplateInfoDto>({ mode: 'modal', url: 'preview' })
const previewContent = ref('')

async function openPreview(row: TemplateRow): Promise<void> {
  previewContent.value = ''
  await previewDetail.open('view', row)
  try {
    previewContent.value = await bridge.templates.render(row.id, {})
  } catch {
    previewContent.value = ''
  }
}

async function handleClone(row: TemplateRow): Promise<void> {
  try {
    await bridge.templates.clone(row.id)
    await crud.refresh()
  } catch {
    // Error handling deferred to error boundary / toast in full integration
  }
}

const t = makePageTranslator('template.templates')

/** Category glyph (email / sms / notice …) so the grid is scannable by shape. */
function categoryIcon(category?: string | null): string {
  const c = (category ?? '').toLowerCase()
  if (c.includes('sms')) return 'mdi:message-text-outline'
  if (c.includes('email') || c.includes('mail')) return 'mdi:email-outline'
  if (c.includes('check') || c.includes('cheque')) return 'mdi:checkbook'
  return 'mdi:file-document-outline'
}

/** First few lines of the raw body: enough to recognise the template. */
function bodyExcerpt(row: TemplateInfoDto): string {
  const body = (row.contentTemplate ?? '').trim()
  if (!body) return t('admin.common.noPreview')
  return body.length > 260 ? `${body.slice(0, 260)}…` : body
}
</script>

<style scoped>
.t-template-preview__content {
  max-height: 60vh;
  overflow-y: auto;
  padding: 8px;
  border: 1px solid var(--tnzi-border);
  border-radius: 4px;
  /* Inherit base text colour so dark mode flips with the rest of the
     admin shell instead of staying at the previous hardcoded #999. */
  color: var(--tnzi-base-text);
  background: var(--tnzi-bg-deep);
}
.t-template-preview__empty {
  color: var(--tnzi-base-text-muted);
  margin: 0;
  padding: 24px 8px;
  text-align: center;
}

.tpl-card {
  height: 100%;
}
.tpl-card :deep(.n-card__content) {
  display: flex;
  flex-direction: column;
  gap: 10px;
  height: 100%;
}
.tpl-card__head {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}
.tpl-card__glyph {
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
.tpl-card__ident {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.tpl-card__name {
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.tpl-card__module {
  font-size: 11.5px;
  color: var(--tnzi-base-text-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.tpl-card__subject {
  margin: 0;
  font-size: 12.5px;
  font-weight: 500;
  color: var(--tnzi-base-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
/* The body slice is the card's substance, so it gets the room; a fixed height
   keeps every tile in the grid the same size regardless of body length. */
.tpl-card__body {
  flex: 1 1 auto;
  margin: 0;
  padding: 8px 10px;
  height: 96px;
  overflow: hidden;
  border-radius: var(--tnzi-admin-radius-sm, 6px);
  background: var(--tnzi-bg-deep, #f6f8fa);
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 11px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
</style>
