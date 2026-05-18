<template>
  <!--
    NotificationTemplate page — Phase 3.25 / 2026-04-14 unstub
    Wired to /admin/notification-templates (DefaultNotificationTemplateAdminController
    in Tnzi.Notification). The backend pins Module="Notification" so the
    bridge only forwards TemplateDto shapes. Preview still delegates to
    api.preview() with a synthesized request (no id-based preview route).
  -->
  <TCrudPage
    :state="crud"
    :all-columns="notificationTemplateColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="notificationTemplateFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
    <template #rowActions="{ row }">
      <TRowActions :row="row" :state="crud" :translate="t">
        <template #prepend>
          <NButton size="small" ghost @click="openPreview(row as Record<string, unknown>)">Preview</NButton>
        </template>
      </TRowActions>
    </template>
  </TCrudPage>

  <!-- Preview modal -->
  <NModal v-model:show="previewVisible" preset="card" title="Template Preview" style="max-width: 640px">
    <div v-if="previewLoading" style="text-align: center; padding: 24px;">Loading…</div>
    <!-- eslint-disable-next-line vue/no-v-html -->
    <div v-else v-html="previewContent" style="white-space: pre-wrap;" />
  </NModal>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NButton, NModal } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createNotificationBridge } from '../../services/bridges/notification-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { notificationTemplateColumns, notificationTemplateFormSchema } from './notification-template-config'
import { translatePageKey } from '../_shared/translate'

const title = 'title'
const bridge = createNotificationBridge({ client: useAdminClient() })

/**
 * Templates sub-contract — /admin/notification-templates, backend pins
 * Module="Notification" server-side. Row shape follows TemplateInfoDto
 * on list, TemplateEntityDto on form submit.
 */
const crud = useCrudPage<Record<string, unknown>>({
  pageId: 'notification.templates',
  columns: notificationTemplateColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.templates.fetch(query) as unknown as Promise<{
    items: Record<string, unknown>[]
    totalCount: number
    pageIndex: number
    pageSize: number
  }>,
  createData: (data) => bridge.templates.create(data as never) as unknown as Promise<Record<string, unknown>>,
  updateData: (id, data) => bridge.templates.update(String(id), data as never) as unknown as Promise<Record<string, unknown>>,
  deleteData: (ids) => bridge.templates.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

// ---- Preview modal ----
const previewVisible = ref(false)
const previewLoading = ref(false)
const previewContent = ref('')

// Static sample variables — Phase 6 can add a proper variables editor.
const SAMPLE_VARS: Record<string, unknown> = {
  name: 'Sample User',
  code: '123456',
  link: 'https://example.com',
}

async function openPreview(row: Record<string, unknown>): Promise<void> {
  previewVisible.value = true
  previewLoading.value = true
  previewContent.value = ''
  try {
    previewContent.value = await bridge.templates.preview(String(row.id ?? ''), SAMPLE_VARS)
  } catch {
    previewContent.value = '(Preview not available — backend endpoint not yet implemented)'
  } finally {
    previewLoading.value = false
  }
}

const t = (key: string) => translatePageKey('notification.templates', key)
</script>
