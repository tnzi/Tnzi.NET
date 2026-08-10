<template>
  <TItemPage
    :state="crud"
    :title="t('title')"
    :title-help="t('titleHelp')"
    :translate="t"
    :search-fields="searchFields"
    :detail-width="720"
    :detail-title="detailTitle"
    show-batch
  >
    <template #item="{ item, selected, selectable, toggleSelect }">
      <TItemCard
        :title="item.title || EMPTY_DASH"
        icon="mdi:draw-pen"
        :icon-tone="envelopeStatusTone(item.status)"
        :tags="requestTags(item)"
        :muted="item.status === EnvelopeStatus.Voided"
        :selectable="selectable"
        :checked="selected"
        :selected="selected"
        clickable
        @update:checked="toggleSelect"
        @click="crud.openView(item)"
      >
        <template #meta>
          <div class="sq-meta">
            <span class="sq-meta__item">
              <TSvgIcon icon="mdi:account-multiple-outline" :size="13" />
              {{ t('card.progress', { signed: item.signedCount, total: item.recipientCount }) }}
            </span>
            <span v-if="item.hostEntityType" class="sq-meta__item">
              <TSvgIcon icon="mdi:link-variant" :size="13" />{{ item.hostEntityType }}
            </span>
            <span class="sq-meta__item">
              <TSvgIcon icon="mdi:calendar-clock" :size="13" />
              {{ t('card.expires') }} {{ fmtDate(item.expiresAt) }}
            </span>
            <span v-if="item.completedAt" class="sq-meta__item">
              <TSvgIcon icon="mdi:check-circle-outline" :size="13" />
              {{ t('card.completed') }} {{ fmtDate(item.completedAt) }}
            </span>
          </div>
        </template>

        <template #trailing>
          <NProgress
            type="line"
            :percentage="progressOf(item)"
            :height="6"
            :show-indicator="false"
            class="sq-progress"
          />
        </template>

        <template #actions>
          <TRowActions :row="item" :actions="rowActions" :translate="t" />
        </template>
      </TItemCard>
    </template>

    <template #detail="{ data }">
      <RequestDetail :request-id="(data as EnvelopeListDto | null)?.id" :translate="t" />
    </template>

    <template #form="{ formData }">
      <TFormSchemaRenderer
        :schema="createSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :field-renderers="fieldRenderers"
        :translate="t"
      />
    </template>
  </TItemPage>

  <!--
    The issued links. This overlay is the ONLY moment those tokens exist
    outside the recipients' inboxes: the store keeps hashes, so re-opening the
    request will never show them again and re-sending invalidates them.
  -->
  <TModalShell
    v-model:show="linksShow"
    :title="t('links.title')"
    :width="620"
    @update:show="onLinksToggle"
  >
    <NAlert type="warning" :bordered="false" class="sq-links__warn">
      {{ t('links.warning') }}
    </NAlert>
    <div v-for="link in issuedLinks" :key="link.recipientId" class="sq-link">
      <div class="sq-link__who">
        <strong>{{ link.name }}</strong>
        <span v-if="link.email" class="sq-link__email">{{ link.email }}</span>
      </div>
      <NInputGroup>
        <NInput :value="link.token" readonly size="small" />
        <NButton size="small" @click="copy(link.token)">
          <TSvgIcon icon="mdi:content-copy" :size="15" />
        </NButton>
      </NInputGroup>
    </div>
    <p class="sq-links__hint">{{ t('links.hint') }}</p>
  </TModalShell>
</template>

<script setup lang="ts">
/**
 * Signing requests as document rows.
 *
 * A request is read one at a time - "who still has to sign, when does it
 * expire, did it complete" - so it gets a row card with an inline progress
 * bar rather than a column grid.
 *
 * Two things this page deliberately does NOT offer:
 *
 *   Edit   - a request freezes its template into a snapshot the moment it is
 *            created. Editing it afterwards would move the ground under
 *            signatures already collected.
 *   Delete - a request that went out to real people is evidence. Void records
 *            that it was called off; deleting would erase that it existed.
 *
 * The recipient-facing signing page is NOT part of this package: signers are
 * usually not users of the system, and what that page looks like belongs to
 * the consuming application. `send` therefore hands the operator the raw
 * one-time tokens.
 */
import { computed, h, ref } from 'vue'
import { NAlert, NButton, NInput, NInputGroup, NProgress, NSelect } from 'naive-ui'
import { TModalShell, TSvgIcon } from '@tnzi/ui'
import { formatDateOnly } from '@tnzi/core'
import TItemPage from '../../components/crud/TItemPage.vue'
import TItemCard, { type ItemCardTag } from '../../components/data/TItemCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import RecipientsEditor from './components/RecipientsEditor.vue'
import RequestDetail from './components/RequestDetail.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import type { RowAction } from '../../headless/row-actions'
import { createSigningBridge } from '../../services/bridges/signing-bridge'
import { useAdminClient } from '../../plugin/client'
import { EMPTY_DASH } from '../../utils/placeholders'
import TFormSchemaRenderer, { type FieldRenderer, type FormSchemaItem } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import {
  ENVELOPE_STATUS_OPTIONS,
  envelopeColumns,
  envelopeStatusTone,
} from './signing-config'
import {
  EnvelopeStatus,
  isTerminalEnvelopeStatus,
  type CreateEnvelopeDto,
  type CreateSignerDto,
  type EnvelopeListDto,
  type IssuedSigningLink,
} from '@tnzi/core/services/signing'

const bridge = createSigningBridge({ client: useAdminClient() })
const t = makePageTranslator('signing.requests')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const crud = useCrudPage<EnvelopeListDto, string>({
  pageId: 'signing.requests',
  permission: { create: 'signing.request.create' },
  columns: envelopeColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.requests.fetch(query),
  // No `loadDetailById`: the drawer loads its own record from the id (see
  // RequestDetail). A cold `?detail=view:<id>` still resolves - useDetail
  // retries against `items` once the first page arrives.
  createData: (data) => bridge.requests.create(toCreate(data)),
  // No updateData / deleteData: see the file header. Omitting them is what
  // makes the shell hide the edit and delete affordances.
})

const searchFields = computed(() => [
  {
    key: 'status',
    label: t('filters.status'),
    type: 'select' as const,
    options: ENVELOPE_STATUS_OPTIONS.map((v) => ({ label: v, value: v })),
  },
  { key: 'hostEntityType', label: t('filters.hostEntityType'), type: 'input' as const },
])

const rowActions: RowAction<EnvelopeListDto>[] = [
  {
    key: 'send',
    label: 'actions.send',
    type: 'primary',
    show: (row) => can('signing.request.update') && row.status === EnvelopeStatus.Draft,
    onClick: (row) => void sendRequest(row),
  },
  {
    key: 'resend',
    label: 'actions.resend',
    // Re-issuing invalidates the links already out there, so it asks first.
    confirm: 'actions.resendConfirm',
    show: (row) =>
      can('signing.request.update') &&
      (row.status === EnvelopeStatus.Sent || row.status === EnvelopeStatus.InProgress),
    onClick: (row) => void sendRequest(row),
  },
  {
    key: 'void',
    label: 'actions.void',
    type: 'error',
    confirm: 'actions.voidConfirm',
    show: (row) => can('signing.request.update') && !isTerminalEnvelopeStatus(row.status),
    onClick: (row) => void voidRequest(row),
  },
]

// ── Issued links ──────────────────────────────────────────────────────────

const issuedLinks = ref<IssuedSigningLink[]>([])
const linksShow = ref(false)

async function sendRequest(row: EnvelopeListDto): Promise<void> {
  if (!row.id) return
  try {
    const links = await bridge.requests.send(String(row.id))
    issuedLinks.value = links
    linksShow.value = true
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

/** Drop the tokens from memory when the overlay closes - they are single-use
 *  credentials, not page state worth keeping around. */
function onLinksToggle(show: boolean): void {
  if (!show) issuedLinks.value = []
}

async function copy(token: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(token)
    message.success(t('links.copied'))
  } catch {
    // Clipboard is permission-gated and unavailable over plain http; the token
    // is still selectable in the input, so this is a nudge, not a failure.
    message.warning(t('links.copyFailed'))
  }
}

async function voidRequest(row: EnvelopeListDto): Promise<void> {
  if (!row.id) return
  try {
    await bridge.requests.void(String(row.id))
    message.success(t('actions.voidSuccess'))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

// ── Create ────────────────────────────────────────────────────────────────

const createSchema: FormSchemaItem[] = [
  { key: 'templateId', label: 'form.template', type: 'custom', required: true },
  { key: 'title', label: 'form.title', type: 'input' },
  { key: 'hostEntityType', label: 'form.hostEntityType', type: 'input' },
  { key: 'hostEntityId', label: 'form.hostEntityId', type: 'input' },
  { key: 'isSequential', label: 'form.isSequential', type: 'switch' },
  { key: 'expiresInDays', label: 'form.expiresInDays', type: 'number', min: 1, max: 365 },
  { key: 'recipients', label: 'form.recipients', type: 'recipients' },
]

const templateOptions = ref<{ label: string; value: string }[]>([])

async function loadTemplateOptions(): Promise<void> {
  try {
    const page = await bridge.templates.fetch({
      pageIndex: 1,
      pageSize: 200,
      searchText: '',
      filters: { isActive: true },
    })
    templateOptions.value = (page.items ?? []).map((tpl) => ({
      label: tpl.category ? `${tpl.category} / ${tpl.name}` : tpl.name,
      value: String(tpl.id),
    }))
  } catch {
    // The picker degrades to empty rather than taking the whole list page down
    // with it: templates are needed to CREATE a request, not to read the list,
    // and that is what most visits are here for.
    templateOptions.value = []
  }
}
void loadTemplateOptions()

const fieldRenderers: Record<string, FieldRenderer> = {
  custom: (ctx) =>
    h(NSelect, {
      value: (ctx.value as string) ?? null,
      options: templateOptions.value,
      filterable: true,
      clearable: true,
      placeholder: t('form.templatePlaceholder'),
      'onUpdate:value': (v: string | null) => ctx.onUpdate(v),
    }),
  recipients: (ctx) =>
    h(RecipientsEditor, {
      modelValue: (ctx.value as CreateSignerDto[] | null) ?? null,
      sequential: Boolean(crud.formModal.formData.value?.isSequential ?? true),
      translate: t,
      'onUpdate:modelValue': (v: CreateSignerDto[]) => ctx.onUpdate(v),
    }),
}

function toCreate(data: unknown): CreateEnvelopeDto {
  const src = (data ?? {}) as Record<string, unknown>
  return {
    templateId: String(src.templateId ?? ''),
    title: (src.title as string | null) || null,
    hostEntityType: (src.hostEntityType as string | null) || null,
    hostEntityId: (src.hostEntityId as string | null) || null,
    isSequential: src.isSequential === undefined ? true : Boolean(src.isSequential),
    expiresInDays: Number(src.expiresInDays ?? 30),
    recipients: (src.recipients as CreateSignerDto[] | undefined) ?? [],
  }
}

// ── Presentation ──────────────────────────────────────────────────────────

const detailTitle = (row: EnvelopeListDto): string => row.title ?? ''

const fmtDate = (v?: string | null): string => formatDateOnly(v, { utc: true }) || EMPTY_DASH

function progressOf(row: EnvelopeListDto): number {
  if (!row.recipientCount) return 0
  return Math.round((row.signedCount / row.recipientCount) * 100)
}

function requestTags(row: EnvelopeListDto): ItemCardTag[] {
  return [{ label: String(row.status), type: envelopeStatusTone(row.status) }]
}
</script>

<style scoped>
.sq-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 16px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.sq-meta__item {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}
.sq-progress {
  width: 120px;
}
.sq-links__warn {
  margin-bottom: 12px;
}
.sq-link {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 8px 0;
  border-bottom: 1px solid var(--tnzi-border);
}
.sq-link__who {
  display: flex;
  align-items: baseline;
  gap: 8px;
  font-size: 13px;
}
.sq-link__email {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
}
.sq-links__hint {
  margin: 12px 0 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
</style>
