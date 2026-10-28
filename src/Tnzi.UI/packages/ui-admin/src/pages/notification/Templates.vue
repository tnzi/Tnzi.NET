<template>
  <!--
    Templates page - notification-scoped template management.
    Wired to /admin/notification-templates (DefaultNotificationTemplateAdminController
    in Tnzi.Notification). Preview hits /admin/notifications/preview which
    renders via ITemplateRenderService; Test-send hits create-and-send.
    Preview + Send Test are two useDetail overlays (deep-linkable via
    ?preview=view:<id> / ?send-test=view:<id>) hosted by TDetailHost.
  -->
  <!--
    row-actions-max-inline=3: read-only (file-system) rows hide Edit/Delete
    via `show`, leaving exactly Preview + Send Test visible - with the
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

  <!-- Preview overlay - pass templateName + editable variables JSON -->
  <TDetailHost :state="previewDetail" :title="previewTitle" :width="720" :footer="false" :translate="t">
    <template #default>
      <NForm label-placement="top" :show-feedback="false">
        <NFormItem :label="t('preview.variables')">
          <NInput
            v-model:value="variablesJson"
            type="textarea"
            :rows="4"
            placeholder="{&quot;name&quot;:&quot;Alice&quot;,&quot;code&quot;:&quot;123456&quot;}"
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
            Plain-text content gets escaped via {{ }} interpolation. HTML
            content (the template engine flags it via NotificationPreviewDto.IsHtml)
            goes through a sandboxed frame rather than `v-html`: the rendered
            Razor output is author-controlled markup, and whoever can edit a
            template should not thereby get script execution in a super-admin's
            session. See THtmlPreview.
          -->
          <THtmlPreview
            v-if="previewResult?.isHtml"
            :html="previewResult.content || ''"
            :height="360"
            :title="t('preview.frameLabel')"
          />
          <div v-else class="t-notif-preview__body">{{ previewResult?.content || t('preview.empty') }}</div>
        </NCard>
      </div>
    </template>
  </TDetailHost>

  <!-- Send-test overlay - pick channel + recipient + variables, then dispatch -->
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
import THtmlPreview from '../../components/display/THtmlPreview.vue'
import { TSvgIcon } from '@tnzi/ui'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/row-actions'
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
const { can } = usePermissionGuard()

/**
 * Templates sub-contract - /admin/notification-templates, backend pins
 * Module="Notification" server-side. Row shape follows TemplateInfoDto
 * on list, TemplateEntityDto on form submit.
 */
const crud = useCrudPage<TemplateRow>({
  pageId: 'notification.templates',
  // Writing a template is running code in the server process (stored Razor is compiled and
  // executed on every send and preview), so the backend stacks the Template module's
  // code-execution codes on create/update. Mirror both gates so the buttons do not show
  // for a user the backend will refuse.
  permission: {
    create: ['notification.template.create', 'template.template.create'],
    update: ['notification.template.update', 'template.template.update'],
    delete: 'notification.template.delete',
  },
  // A template left alone on the switch is created active; the switch says so.
  createDefaults: { isActive: true },
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
  // Preview and test-send run on the notifications endpoints, which check the
  // message codes, not the template codes this page is reached with.
  { key: 'preview', label: 'actions.preview', show: () => can('notification.message.view'), onClick: (row) => void openPreview(row) },
  { key: 'sendTest', label: 'actions.sendTest', show: () => can('notification.message.create'), onClick: (row) => openSendTest(row) },
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
 * The channel a template is written for. Its own `type` (TemplateType) names
 * it for an Email or Sms template, whatever the category it is filed under. A
 * template whose type names no channel (Generic is the store default, and what
 * a file template reports) falls back to its category, which is how the
 * shipped notification templates are organised (Email / Sms / Push folders).
 * The enum serializes as its member-name string, so this sends e.g. 'Sms'.
 */
function templateChannel(row: TemplateRow | null): NotificationType {
  const type = String(row?.type ?? '')
  if (type === 'Sms') return NotificationType.Sms
  if (type === 'Email') return NotificationType.Email
  const c = String(row?.category ?? '').toLowerCase()
  if (c.includes('sms')) return NotificationType.Sms
  if (c.includes('push')) return NotificationType.Push
  return NotificationType.Email
}

/**
 * The category sent with the preview and the test send. The server finds a
 * template by name AND category, and without a category it looks under the
 * channel name (Email / Sms / Push), where notification templates live by
 * convention. A template filed under another category is only found when the
 * category goes along, which is also what a real send of it has to do; one
 * filed under its channel name goes without, as real sends do, so the test
 * message carries the same category a real one does.
 */
function templateCategoryFor(row: TemplateRow | null, channel: NotificationType): string | null {
  const c = typeof row?.category === 'string' ? row.category.trim() : ''
  if (!c || c.toLowerCase() === String(channel).toLowerCase()) return null
  return c
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
    const channel = templateChannel(row)
    previewResult.value = await bridge.templates.preview({
      templateName: templateNameOf(row),
      category: templateCategoryFor(row, channel),
      type: channel,
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
  sendChannel.value = templateChannel(row)
  void sendDetail.open('view', row)
}

async function runSendTest(): Promise<void> {
  if (!recipientAddress.value) return
  parseVariables()
  sendLoading.value = true
  try {
    await bridge.templates.sendTest({
      templateName: templateNameOf(sendDetail.data.value),
      category: templateCategoryFor(sendDetail.data.value, sendChannel.value),
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
