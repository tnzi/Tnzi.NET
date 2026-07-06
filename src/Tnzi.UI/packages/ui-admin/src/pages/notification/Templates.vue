<template>
  <!--
    Templates page — notification-scoped template management.
    Wired to /admin/notification-templates (DefaultNotificationTemplateAdminController
    in Tnzi.Notification). Preview hits /admin/notifications/preview which
    renders via ITemplateRenderService; Test-send hits create-and-send.
    Preview + Send Test are two useDetail overlays (deep-linkable via
    ?preview=view:<id> / ?send-test=view:<id>) hosted by TDetailHost.
  -->
  <!--
    row-actions-max-inline=3: read-only (file-system) rows hide Edit/Delete
    via `show`, leaving exactly Preview + Send Test visible — with the
    default maxInline=2 the auto-width estimator sizes the column for the
    collapsed worst case ([Edit][More]), which is narrower than two fully
    inline buttons, so they wrapped vertically. 3 sizes the column for
    [Edit][Preview][More] and keeps both variants on one line.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="notificationTemplateColumns"
    :title="title"
    :title-help="t('titleHelp')"
    :translate="t"
    :form-modal-width="760"
    :row-actions="rowActions"
    :row-actions-max-inline="3"
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
  </TCrudPage>

  <!-- Preview overlay — pass templateName + editable variables JSON -->
  <TDetailHost :state="previewDetail" :title="previewTitle" :width="720" :footer="false" :translate="t">
    <template #default>
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
    </template>
  </TDetailHost>

  <!-- Send-test overlay — pick channel + recipient + variables, then dispatch -->
  <TDetailHost :state="sendDetail" :title="sendTitle" :width="640" :translate="t">
    <template #default>
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
              { value: NotificationType.Email, label: t('channel.email') },
              { value: NotificationType.Sms, label: t('channel.sms') },
              { value: NotificationType.Push, label: t('channel.push') },
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
      <NAlert type="warning" :show-icon="true" class="mt-12px">
        {{ t('send.warning') }}
      </NAlert>
    </template>
    <template #footer>
      <NSpace justify="end">
        <NButton @click="sendDetail.close()">{{ t('actions.cancel') }}</NButton>
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
  </TDetailHost>
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
  NSelect,
  NSpace,
  useMessage,
} from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import { TSvgIcon } from '@tnzi/ui'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import {
  createNotificationBridge,
  type NotificationTemplatePreviewResult,
} from '../../services/bridges/notification-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { notificationTemplateColumns, notificationTemplateFormSchema } from './template-config'
import { makePageTranslator } from '../_shared/translate'
import { NotificationType } from '@tnzi/core/services/notification'

type TemplateRow = Record<string, unknown>

const title = 'title'
const bridge = createNotificationBridge({ client: useAdminClient() })
const message = useMessage()

/**
 * Templates sub-contract — /admin/notification-templates, backend pins
 * Module="Notification" server-side. Row shape follows TemplateInfoDto
 * on list, TemplateEntityDto on form submit.
 */
const crud = useCrudPage<TemplateRow>({
  pageId: 'notification.templates',
  columns: notificationTemplateColumns,
  rowKey: (r) => {
    const id = String(r.id ?? '')
    if (id && id !== '00000000-0000-0000-0000-000000000000') return id
    const m = r.module ?? ''
    const c = r.category ?? ''
    const n = r.templateName ?? r.code ?? r.name ?? ''
    return `file:${String(m)}/${String(c)}/${String(n)}`
  },
  // 0.2.72+ (C4): bridge now returns `PagedList<T>` directly.
  fetchData: (query) => bridge.templates.fetch(query) as never,
  createData: (data) => bridge.templates.create(data as never) as unknown as Promise<TemplateRow>,
  updateData: (id, data) => bridge.templates.update(String(id), data as never) as unknown as Promise<TemplateRow>,
  deleteData: (ids) => bridge.templates.delete(ids.map(String)),
})

const rowActions: RowAction<TemplateRow>[] = [
  editAction(crud, { show: (row) => !row.isReadOnly }),
  { key: 'preview', label: 'actions.preview', onClick: (row) => void openPreview(row) },
  { key: 'sendTest', label: 'actions.sendTest', onClick: (row) => openSendTest(row) },
  deleteAction(crud, { show: (row) => !row.isReadOnly }),
]

// ─── Shared state ──────────────────────────────────────────────────
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

function templateNameOf(row: TemplateRow | null): string | null {
  if (!row) return null
  return (row.templateName as string | undefined) ?? (row.name as string | undefined) ?? null
}

/**
 * Derive the notification channel from a template row's `category`. Notification
 * templates use `category` to distinguish channels (Email / Sms / Push); the
 * enum now serializes as its member-name string so we send e.g. 'Email'
 * (the backend accepts both string and numeric).
 */
function categoryToNotificationType(category?: unknown): NotificationType {
  const c = String(category ?? '').toLowerCase()
  if (c.includes('sms')) return NotificationType.Sms
  if (c.includes('push')) return NotificationType.Push
  return NotificationType.Email
}

// ─── Preview overlay ───────────────────────────────────────────────
const previewDetail = useDetail<TemplateRow>({ mode: 'modal', url: 'preview' })
const previewLoading = ref(false)
const previewResult = ref<NotificationTemplatePreviewResult | null>(null)
const previewError = ref<string | null>(null)
const previewTitle = computed(() => t('preview.title', { name: templateNameOf(previewDetail.data.value) ?? '' }))

async function openPreview(row: TemplateRow): Promise<void> {
  previewResult.value = null
  previewError.value = null
  await previewDetail.open('view', row)
  await runPreview()
}

async function runPreview(): Promise<void> {
  parseVariables()
  const row = previewDetail.data.value
  previewLoading.value = true
  previewResult.value = null
  previewError.value = null
  try {
    previewResult.value = await bridge.templates.preview({
      templateName: templateNameOf(row),
      type: categoryToNotificationType(row?.category),
      variables: parsedVariables.value,
    })
  } catch (e: unknown) {
    previewError.value = e instanceof Error ? e.message : String(e)
  } finally {
    previewLoading.value = false
  }
}

// ─── Send-test overlay ─────────────────────────────────────────────
const sendDetail = useDetail<TemplateRow>({ mode: 'modal', url: 'send-test' })
const sendLoading = ref(false)
const recipientAddress = ref('')
const sendChannel = ref<NotificationType>(NotificationType.Email)
const sendTitle = computed(() => t('send.title', { name: templateNameOf(sendDetail.data.value) ?? '' }))

function openSendTest(row: TemplateRow): void {
  recipientAddress.value = ''
  sendChannel.value = categoryToNotificationType(row.category)
  void sendDetail.open('view', row)
}

async function runSendTest(): Promise<void> {
  if (!recipientAddress.value) return
  parseVariables()
  sendLoading.value = true
  try {
    await bridge.templates.sendTest({
      templateName: templateNameOf(sendDetail.data.value),
      type: sendChannel.value,
      variables: parsedVariables.value,
      recipientAddress: recipientAddress.value,
    })
    message.success(t('send.success', { addr: recipientAddress.value }))
    sendDetail.close()
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e)
    message.error(t('send.failed', { error: msg }))
  } finally {
    sendLoading.value = false
  }
}

const t = makePageTranslator('notification.templates')
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
