<template>
  <!--
    Templates page — Phase 3.36
    Admin CRUD for the cross-module template library (Module column shown,
    since these span every consuming module — email, sms, ...).
    Row actions "Preview" and "Clone":
      Preview: renders the template via bridge.templates.render(id, {}) into a
               deep-linkable modal (?preview=view:<id>).
      Clone:   calls bridge.templates.clone(id) then refreshes the table.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="templateColumns"
    :title="t('title')"
    :title-help="t('titleHelp')"
    :translate="t"
    :form-modal-width="760"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="templateFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
      />
    </template>
  </TCrudPage>

  <!-- Preview overlay — rendered template HTML, deep-linkable via ?preview=view:<id> -->
  <TDetailHost :state="previewDetail" :title="t('previewTitle')" :width="640" :footer="false" :translate="t">
    <template #default>
      <div
        v-if="previewContent"
        class="t-template-preview__content"
        v-html="previewContent"
      />
      <p v-else class="t-template-preview__empty">{{ t('admin.common.noPreview') }}</p>
    </template>
  </TDetailHost>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createTemplateBridge } from '../../services/bridges/template-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { templateColumns, templateFormSchema } from './template-config'
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
// from the row — no getById hydration needed.
const rowActions: RowAction<TemplateRow>[] = [
  editAction(crud, { show: (row) => !(row as TemplateRow).isReadOnly }),
  { key: 'preview', label: 'actions.preview', onClick: (row) => void openPreview(row) },
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
</style>
