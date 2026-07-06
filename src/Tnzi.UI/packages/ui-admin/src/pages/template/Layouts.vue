<template>
  <!--
    Layouts page — Phase 3.37
    Admin CRUD for template layout definitions.
    Layouts are the wrapper HTML/content that templates render within.
    Standard CRUD operations: list, create, edit, delete.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="layoutColumns"
    :title="t('title')"
    :translate="t"
    :form-modal-width="760"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="layoutFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createTemplateBridge } from '../../services/bridges/template-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { layoutColumns, layoutFormSchema } from './layout-config'
import { makePageTranslator } from '../_shared/translate'
import type { LayoutInfoDto } from '@tnzi/core/services/template'

const bridge = createTemplateBridge({ client: useAdminClient() })

const crud = useCrudPage<LayoutInfoDto, string>({
  pageId: 'template.layouts',
  columns: layoutColumns,
  rowKey: (r) => {
    const id = String(r.id ?? '')
    return id && id !== '00000000-0000-0000-0000-000000000000'
      ? id
      : `file:${r.module}/${r.category || ''}/${r.layoutName}`
  },
  fetchData: (query) => bridge.layouts.fetch(query),
  createData: async (data) => bridge.layouts.create(data as Partial<LayoutInfoDto>),
  updateData: async (id, data) => bridge.layouts.update(id, data as Partial<LayoutInfoDto>),
  deleteData: async (ids) => bridge.layouts.delete(ids),
})

// FileSystem-source layouts ship with the binaries and backend rejects
// edit/delete on them — suppress those actions for read-only rows.
const rowActions: RowAction<LayoutInfoDto>[] = [
  editAction(crud, { show: (row) => !row.isReadOnly }),
  deleteAction(crud, { show: (row) => !row.isReadOnly }),
]

const t = makePageTranslator('template.layouts')
</script>
