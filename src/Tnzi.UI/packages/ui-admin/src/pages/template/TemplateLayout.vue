<template>
  <!--
    TemplateLayout page — Phase 3.37
    Admin CRUD for template layout definitions.
    Layouts are the wrapper HTML/content that templates render within.
    Standard CRUD operations: list, create, edit, delete.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="layoutColumns"
    title="Template Layouts"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="layoutFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createTemplateBridge } from '../../services/bridges/template-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { layoutColumns, layoutFormSchema } from './layout-config'
import { translatePageKey } from '../_shared/translate'
import type { LayoutInfoDto } from '@tnzi/core/services/template'

const bridge = createTemplateBridge({ client: useAdminClient() })

const crud = useCrudPage<LayoutInfoDto, string>({
  pageId: 'template.layouts',
  columns: layoutColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.layouts.fetch(query),
  createData: async (data) => bridge.layouts.create(data as Partial<LayoutInfoDto>),
  updateData: async (id, data) => bridge.layouts.update(id, data as Partial<LayoutInfoDto>),
  deleteData: async (ids) => bridge.layouts.delete(ids),
})


onMounted(() => {
  crud.refresh().catch(() => undefined)
})

const t = (key: string) => translatePageKey('template.layouts', key)
</script>
