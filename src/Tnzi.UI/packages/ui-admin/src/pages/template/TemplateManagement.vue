<template>
  <!--
    TemplateManagement page — Phase 3.36
    Admin CRUD for notification/email templates.
    Row actions "Preview" and "Clone":
      Preview: calls bridge.templates.render(id, {}) and displays rendered HTML in modal.
      Clone:   calls bridge.templates.clone(id) then refreshes the table.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="templateColumns"
    title="Templates"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="templateFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>

    <template #rowActions="{ row }">
      <TRowActions :row="row" :state="crud" :translate="t">
        <template #prepend>
          <Button size="small" type="info" @click="openPreview(row as TemplateRow)">Preview</Button>
          <Button size="small" type="default" @click="handleClone(row as TemplateRow)">Clone</Button>
        </template>
      </TRowActions>
    </template>
  </TCrudPage>

  <!-- Preview modal -->
  <Modal
    v-if="previewVisible"
    :show="previewVisible"
    title="Template Preview"
    style="width: 640px;"
    @update:show="(v: boolean) => { if (!v) previewVisible = false }"
  >
    <div
      v-if="previewContent"
      class="template-preview-content"
      style="max-height: 60vh; overflow-y: auto; padding: 8px; border: 1px solid #e0e0e0; border-radius: 4px;"
      v-html="previewContent"
    />
    <p v-else style="color: #999;">No preview content available.</p>
    <div style="display: flex; justify-content: flex-end; margin-top: 16px;">
      <Button @click="previewVisible = false">Close</Button>
    </div>
  </Modal>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { NButton as Button, NModal as Modal } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createTemplateBridge } from '../../services/bridges/template-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { templateColumns, templateFormSchema } from './template-config'
import { translatePageKey } from '../_shared/translate'
import type { TemplateInfoDto } from '@tnzi/core/services/template'

type TemplateRow = TemplateInfoDto & { id: string }

const bridge = createTemplateBridge({ client: useAdminClient() })

const crud = useCrudPage<TemplateInfoDto, string>({
  pageId: 'template.templates',
  columns: templateColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.templates.fetch(query),
  createData: async (data) => bridge.templates.create(data as Partial<TemplateInfoDto>),
  updateData: async (id, data) => bridge.templates.update(id, data as Partial<TemplateInfoDto>),
  deleteData: async (ids) => bridge.templates.delete(ids),
})


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
