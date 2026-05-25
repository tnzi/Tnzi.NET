<template>
  <!--
    NotificationTemplate page — Phase C2 2026-05-20 enhanced
    Adds variables editor + real-templateName preview + test-send actions.
    Wired to /admin/notification-templates (DefaultNotificationTemplateAdminController
    in Tnzi.Notification). Preview hits /admin/notifications/preview which
    renders via ITemplateRenderService; Test-send hits create-and-send.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="notificationTemplateColumns"
    :title="title"
    :translate="t"
    :form-modal-width="760"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="notificationTemplateFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
      />
    </template>
    <template #rowActions="{ row }">
      <TRowActions
        :row="row as Record<string, unknown>"
        :state="crud"
        :translate="t"
        :show-edit="!((row as Record<string, unknown>).isReadOnly)"
        :show-delete="!((row as Record<string, unknown>).isReadOnly)"
        :more-options="[
          { key: 'preview', label: t('actions.preview') },
          { key: 'sendTest', label: t('actions.sendTest') },
        ]"
        :on-more-select="onMoreSelect"
      />
    </template>
  </TCrudPage>

  <!-- Preview modal — pass templateName + editable variables JSON -->
  <NModal v-model:show="previewVisible" preset="card" :title="previewTitle" style="max-width: 720px">
    <NForm label-placement="top" :show-feedback="false">
      <NFormItem :label="t('preview.variables')">
        <NInput
          v-model:value="variablesJson"
          type="textarea"
          :rows="4"
          placeholder='{"name":"Alice","code":"123456"}'
          @blur="parseVariables"
        />
      </NFormItem>
      <NButton type="primary" :loading="previewLoading" size="small" @click="runPreview">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="14" /></template>
        {{ t('actions.preview') }}
      </NButton>
    </NForm>
    <NDivider />
    <div v-if="previewLoading" class="t-notif-preview__placeholder">
      {{ t('preview.loading') }}
    </div>
    <div v-else-if="previewError" class="t-notif-preview__error">
      {{ previewError }}
    </div>
    <div v-else>
      <div v-if="previewResult?.subject" class="t-notif-preview__subject">
        <span class="t-notif-preview__label">{{ t('preview.subject') }}:</span>
        <span>{{ previewResult.subject }}</span>
      </div>
      <NCard size="small" :bordered="false" class="t-notif-preview__content">
        <!--
          Plain-text content gets escaped via {{ }} interpolation; HTML
          content (the template engine flags it via NotificationPreviewDto.IsHtml)
          gets v-html so the rendered Razor output displays correctly. This
          matches the backend contract — admins decide trust via the template
          channel (email = HTML; sms / push = plain text).
        -->
        <!-- eslint-disable-next-line vue/no-v-html -->
        <div v-if="previewResult?.isHtml" class="t-notif-preview__body" v-html="previewResult.content || t('preview.empty')" />
        <div v-else class="t-notif-preview__body">{{ previewResult?.content || t('preview.empty') }}</div>
      </NCard>
    </div>
  </NModal>

  <!-- Send-test modal — pick channel + recipient + variables, then dispatch -->
  <NModal v-model:show="sendVisible" preset="card" :title="sendTitle" style="max-width: 640px">
    <NForm label-placement="top" :show-feedback="false">
      <NFormItem :label="t('send.recipient')" required>
        <NInput
          v-model:value="recipientAddress"
          :placeholder="t('send.recipientPlaceholder')"
        />
      </NFormItem>
      <NFormItem :label="t('send.channel')">
        <NSelect
          v-model:value="sendChannel"
          :options="[
            { value: 1, label: t('channel.email') },
            { value: 2, label: t('channel.sms') },
            { value: 3, label: t('channel.push') },
          ]"
        />
      </NFormItem>
      <NFormItem :label="t('preview.variables')">
        <NInput
          v-model:value="variablesJson"
          type="textarea"
          :rows="4"
          @blur="parseVariables"
        />
      </NFormItem>
    </NForm>
    <NAlert type="warning" :show-icon="true" style="margin-top: 12px">
      {{ t('send.warning') }}
    </NAlert>
    <template #footer>
      <NSpace justify="end">
        <NButton @click="sendVisible = false">{{ t('actions.cancel') }}</NButton>
        <NButton
          type="primary"
          :loading="sendLoading"
          :disabled="!recipientAddress"
          @click="runSendTest"
        >
          {{ t('actions.send') }}
        </NButton>
      </NSpace>
    </template>
  </NModal>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  NAlert,
  NButton,
  NCard,
  NDivider,
  NForm,
  NFormItem,
  NInput,
  NModal,
  NSelect,
  NSpace,
  useMessage,
} from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { TSvgIcon } from '@tnzi/ui'
import { useCrudPage } from '../../headless/useCrudPage'
import {
  createNotificationBridge,
  type NotificationTemplatePreviewResult,
} from '../../services/bridges/notification-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { notificationTemplateColumns, notificationTemplateFormSchema } from './notification-template-config'
import { translatePageKey } from '../_shared/translate'

const title = 'title'
const bridge = createNotificationBridge({ client: useAdminClient() })
const message = useMessage()

/**
 * Templates sub-contract — /admin/notification-templates, backend pins
 * Module="Notification" server-side. Row shape follows TemplateInfoDto
 * on list, TemplateEntityDto on form submit.
 */
const crud = useCrudPage<Record<string, unknown>>({
  pageId: 'notification.templates',
  columns: notificationTemplateColumns,
  rowKey: (r) => {
    const id = String(r.id ?? '')
    if (id && id !== '00000000-0000-0000-0000-000000000000') return id
    const m = (r as Record<string, unknown>).module ?? ''
    const c = (r as Record<string, unknown>).category ?? ''
    const n = (r as Record<string, unknown>).templateName ?? r.code ?? r.name ?? ''
    return `file:${String(m)}/${String(c)}/${String(n)}`
  },
  // 0.2.72+ (C4): bridge now returns `PagedList<T>` directly.
  fetchData: (query) => bridge.templates.fetch(query) as never,
  createData: (data) => bridge.templates.create(data as never) as unknown as Promise<Record<string, unknown>>,
  updateData: (id, data) => bridge.templates.update(String(id), data as never) as unknown as Promise<Record<string, unknown>>,
  deleteData: (ids) => bridge.templates.delete(ids.map(String)),
})

crud.refresh().catch(() => undefined)

// ─── Shared state ──────────────────────────────────────────────────
const activeRow = ref<Record<string, unknown> | null>(null)
const variablesJson = ref('{"name":"Sample User","code":"123456","link":"https://example.com"}')
const parsedVariables = ref<Record<string, unknown>>({})

function parseVariables(): void {
  try {
    const raw = variablesJson.value.trim()
    parsedVariables.value = raw ? (JSON.parse(raw) as Record<string, unknown>) : {}
  } catch {
    parsedVariables.value = {}
  }
}
parseVariables()

const currentTemplateName = computed(() => {
  const row = activeRow.value
  if (!row) return null
  return (row.templateName as string | undefined) ?? (row.name as string | undefined) ?? null
})

const previewTitle = computed(() => t('preview.title', { name: currentTemplateName.value ?? '' }))
const sendTitle = computed(() => t('send.title', { name: currentTemplateName.value ?? '' }))

// ─── Preview modal ─────────────────────────────────────────────────
const previewVisible = ref(false)
const previewLoading = ref(false)
const previewResult = ref<NotificationTemplatePreviewResult | null>(null)
const previewError = ref<string | null>(null)

async function openPreview(row: Record<string, unknown>): Promise<void> {
  activeRow.value = row
  previewVisible.value = true
  previewResult.value = null
  previewError.value = null
  await runPreview()
}

async function runPreview(): Promise<void> {
  parseVariables()
  previewLoading.value = true
  previewResult.value = null
  previewError.value = null
  try {
    previewResult.value = await bridge.templates.preview({
      templateName: currentTemplateName.value,
      type: 1,
      variables: parsedVariables.value,
    })
  } catch (e: unknown) {
    previewError.value = e instanceof Error ? e.message : String(e)
  } finally {
    previewLoading.value = false
  }
}

// ─── Send-test modal ───────────────────────────────────────────────
const sendVisible = ref(false)
const sendLoading = ref(false)
const recipientAddress = ref('')
const sendChannel = ref(1)

function openSendTest(row: Record<string, unknown>): void {
  activeRow.value = row
  recipientAddress.value = ''
  sendChannel.value = 1
  sendVisible.value = true
}

async function runSendTest(): Promise<void> {
  if (!recipientAddress.value) return
  parseVariables()
  sendLoading.value = true
  try {
    await bridge.templates.sendTest({
      templateName: currentTemplateName.value,
      type: sendChannel.value,
      variables: parsedVariables.value,
      recipientAddress: recipientAddress.value,
    })
    message.success(t('send.success', { addr: recipientAddress.value }))
    sendVisible.value = false
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e)
    message.error(t('send.failed', { error: msg }))
  } finally {
    sendLoading.value = false
  }
}

function onMoreSelect(key: string, row: Record<string, unknown>): void {
  if (key === 'preview') void openPreview(row)
  else if (key === 'sendTest') openSendTest(row)
}

const t = (key: string, params?: Record<string, unknown>) => {
  const raw = translatePageKey('notification.templates', key)
  if (!params) return raw
  return Object.entries(params).reduce(
    (acc, [k, v]) => acc.replace(new RegExp(`\\{${k}\\}`, 'g'), String(v ?? '')),
    raw,
  )
}
</script>

<style scoped>
.t-notif-preview__placeholder {
  text-align: center;
  padding: 24px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-notif-preview__error {
  padding: 12px;
  border: 1px solid var(--tnzi-error, #d03050);
  border-radius: 6px;
  color: var(--tnzi-error, #d03050);
  font-size: 13px;
}
.t-notif-preview__subject {
  font-weight: 500;
  margin-bottom: 8px;
}
.t-notif-preview__label {
  color: var(--tnzi-base-text-muted, #888);
  margin-right: 8px;
}
.t-notif-preview__content {
  max-height: 360px;
  overflow: auto;
}
.t-notif-preview__body {
  white-space: pre-wrap;
  font-family: var(--tnzi-font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
  line-height: 1.6;
}
</style>
