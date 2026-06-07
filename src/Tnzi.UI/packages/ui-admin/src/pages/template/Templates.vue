<template>
  <!--
    Templates page — Phase 3.36
    Admin CRUD for notification/email templates.
    Row actions "Preview" and "Clone":
      Preview: calls bridge.templates.render(id, {}) and displays rendered HTML in modal.
      Clone:   calls bridge.templates.clone(id) then refreshes the table.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="templateColumns"
    :title="t('title')"
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

  <!-- Preview modal -->
  <Modal
    v-if="previewVisible"
    :show="previewVisible"
    :title="t('previewTitle')"
    class="w-640px"
    @update:show="(v: boolean) => { if (!v) previewVisible = false }"
  >
    <div
      v-if="previewContent"
      class="t-template-preview__content"
      v-html="previewContent"
    />
    <p v-else class="t-template-preview__empty">{{ t('admin.common.noPreview') }}</p>
    <div class="t-template-preview__footer">
      <Button @click="previewVisible = false">{{ t('admin.common.close') }}</Button>
    </div>
  </Modal>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { NButton as Button, NModal as Modal } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createTemplateBridge } from '../../services/bridges/template-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { templateColumns, templateFormSchema } from './template-config'
import { translatePageKey } from '../_shared/translate'
import type { TemplateInfoDto, TemplateEntityDto } from '@tnzi/core/services/template'

type TemplateRow = TemplateInfoDto & { id: string }

const bridge = createTemplateBridge({ client: useAdminClient() })

const crud = useCrudPage<TemplateInfoDto, string>({
  pageId: 'template.templates',
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
const rowActions: RowAction<TemplateRow>[] = [
  editAction(crud, { show: (row) => !(row as TemplateRow).isReadOnly }),
  { key: 'preview', label: 'actions.preview', onClick: (row) => void openPreview(row) },
  { key: 'clone', label: 'actions.clone', onClick: (row) => void handleClone(row) },
  deleteAction(crud, { show: (row) => !(row as TemplateRow).isReadOnly }),
]

// TemplateInfoDto (paged list shape) drops the heavy `subjectTemplate` /
// `contentTemplate` fields. When the form modal opens for view / edit we
// need to hydrate those from the full TemplateEntityDto via getById so the
// form schema fields render real content (otherwise the modal looks empty).
watch(
  () => [crud.formModal.visible.value, crud.formModal.mode.value] as const,
  async ([show, mode]) => {
    if (!show) return
    if (mode !== 'edit' && mode !== 'view') return
    const row = crud.formModal.formData.value as (TemplateRow | null)
    if (!row || !row.id) return
    // File-source rows have id = Guid.Empty — skip hydration because
    // there's no DB row to fetch. The form will read whatever info the
    // list response already populated.
    if (row.id === '00000000-0000-0000-0000-000000000000') return
    const merged = row as unknown as Record<string, unknown>
    if (merged.contentTemplate) return // already hydrated
    try {
      const full = await bridge.templates.getById(row.id)
      if (full && crud.formModal.formData.value) {
        crud.formModal.formData.value = {
          ...crud.formModal.formData.value,
          ...full,
        } as TemplateInfoDto
      }
    } catch {
      // Network/permission errors leave the row as-is; the form just shows
      // the partial list fields.
    }
  },
)


// Preview dialog state
const previewVisible = ref(false)
const previewContent = ref('')

async function openPreview(row: TemplateRow): Promise<void> {
  try {
    previewContent.value = await bridge.templates.render(row.id, {})
  } catch {
    previewContent.value = ''
  }
  previewVisible.value = true
}

async function handleClone(row: TemplateRow): Promise<void> {
  try {
    await bridge.templates.clone(row.id)
    await crud.refresh()
  } catch {
    // Error handling deferred to error boundary / toast in full integration
  }
}

onMounted(() => {
  crud.refresh().catch(() => undefined)
})

const t = (key: string) => translatePageKey('template.templates', key)
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
.t-template-preview__footer {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}
</style>
