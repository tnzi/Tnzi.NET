<template>
  <!--
    OpenID Connect client registry (Identity_OidcClient, shipped by the optional
    Tnzi.Identity.OpenIdConnect module): the applications allowed to sign users
    in with this platform's accounts.

    ★ Disabling a client is not a soft toggle: it revokes every grant and refresh
    token under it, and enabling it again does not bring them back.

    A client is a thing with a name and a few facts (type, consent, scopes), and
    nobody compares those across clients, so the registry is a tile grid. A tile
    opens the client: the edit form for whoever may change it, read-only for the
    rest. Enable / disable / rotate / delete sit on the tile.
  -->
  <TCardPage
    :state="crud"
    mode="page"
    :min-col-width="300"
    :cols="{ xs: 1, sm: 2, md: 3, lg: 4 }"
    :title="title"
    :translate="t"
    :search-placeholder="t('searchPlaceholder')"
    :form-modal-width="720"
    :row-actions="rowActions"
  >
    <template #card="{ item }">
      <TEntityCard clickable @click="openClient(item)">
        <div class="oidc-client-card__head">
          <span class="oidc-client-card__glyph">
            <TSvgIcon :icon="clientIcon(item)" :size="18" />
          </span>
          <span class="oidc-client-card__name" :title="clientName(item)">{{ clientName(item) }}</span>
          <TStatusBadge :value="item.isEnabled" :mapping="OIDC_CLIENT_ENABLED_BADGE" />
        </div>

        <div class="oidc-client-card__id font-mono" :title="item.clientId">{{ item.clientId }}</div>

        <div class="oidc-client-card__chips">
          <NTag size="small" type="info" :bordered="false">{{ t(`card.clientTypes.${item.clientType}`) }}</NTag>
          <NTag size="small" :bordered="false">{{ t(`card.consentTypes.${item.consentType}`) }}</NTag>
        </div>

        <div class="oidc-client-card__chips" :aria-label="t('columns.scopes')">
          <NTag v-for="scope in item.scopes ?? []" :key="scope" size="small" :bordered="false" class="font-mono">
            {{ scope }}
          </NTag>
          <span v-if="!item.scopes?.length" class="oidc-client-card__muted">{{ EMPTY_DASH }}</span>
        </div>

        <div class="oidc-client-card__muted">
          {{ t('columns.creationTime') }} {{ formatDateTime(item.creationTime) }}
        </div>

        <template #actions>
          <TRowActions :row="item" :actions="rowActions" :translate="t" />
        </template>
      </TEntityCard>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="oidcClientFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
      />
    </template>
  </TCardPage>

  <!--
    The client secret. This overlay is the ONLY moment it exists in plaintext:
    the server keeps a hash, so a lost secret can only be rotated.
  -->
  <TModalShell v-model:show="secretShow" :title="t('secret.title')" :width="560" @update:show="onSecretToggle">
    <NAlert type="warning" :bordered="false" class="mb-3">{{ t('secret.warning') }}</NAlert>
    <div class="flex flex-col gap-2">
      <div class="text-xs text-[var(--tnzi-base-text-muted)]">{{ t('columns.clientId') }}</div>
      <NInputGroup>
        <NInput :value="issued?.client.clientId ?? ''" readonly size="small" />
        <NButton size="small" @click="copy(issued?.client.clientId ?? '')">
          <TSvgIcon icon="mdi:content-copy" :size="15" />
        </NButton>
      </NInputGroup>
      <div class="text-xs text-[var(--tnzi-base-text-muted)]">{{ t('secret.label') }}</div>
      <NInputGroup>
        <NInput :value="issued?.clientSecret ?? ''" readonly size="small" />
        <NButton size="small" @click="copy(issued?.clientSecret ?? '')">
          <TSvgIcon icon="mdi:content-copy" :size="15" />
        </NButton>
      </NInputGroup>
    </div>
  </TModalShell>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NAlert, NButton, NInput, NInputGroup, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TModalShell from '../../components/overlay/TModalShell.vue'
import TCardPage from '../../components/crud/TCardPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TEntityCard from '../../components/data/TEntityCard.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { EMPTY_DASH } from '../../utils/placeholders'
import { formatDateTime } from '@tnzi/core'
import { useCrudPage } from '../../headless/useCrudPage'
import { deleteAction, type RowAction } from '../../headless/row-actions'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { createOidcBridge } from '../../services/bridges/oidc-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { OidcClientType, type OidcClientDto, type OidcClientSecretDto } from '@tnzi/core/services/oidc'
import {
  OIDC_CLIENT_ENABLED_BADGE,
  oidcClientColumns,
  oidcClientCreateDefaults,
  oidcClientFormSchema,
  toCreateOidcClientDto,
  toOidcClientFormModel,
  toUpdateOidcClientDto,
  type OidcClientFormModel,
} from './oidc-client-config'

const title = 'title'
const t = makePageTranslator('identity.oidcClients')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const bridge = createOidcBridge({ client: useAdminClient() })

const issued = ref<OidcClientSecretDto | null>(null)
const secretShow = ref(false)

function showSecret(result: OidcClientSecretDto): void {
  // Public clients have no secret; nothing to show, nothing to lose.
  if (!result.clientSecret) return
  issued.value = result
  secretShow.value = true
}

const crud = useCrudPage<OidcClientDto>({
  pageId: 'identity.oidcClients',
  permission: 'identity.oidcClient',
  columns: oidcClientColumns,
  rowKey: (r) => String(r.id ?? ''),
  createDefaults: oidcClientCreateDefaults as Partial<OidcClientDto>,
  // Redirect URIs and scopes are lists on the wire and text in the form.
  loadEditRecord: async (row) => toOidcClientFormModel(row) as OidcClientDto,
  fetchData: (query) => bridge.clients.fetch(query),
  createData: async (data) => {
    const result = await bridge.clients.create(toCreateOidcClientDto(data as OidcClientFormModel))
    showSecret(result)
    return result.client
  },
  updateData: (id, data) => bridge.clients.update(String(id), toUpdateOidcClientDto(data as OidcClientFormModel)),
  deleteData: (ids) => bridge.clients.delete(ids.map(String)),
})

async function setEnabled(row: OidcClientDto, enabled: boolean): Promise<void> {
  try {
    await bridge.clients.setEnabled(row.id, enabled)
    message.success(t(enabled ? 'actions.enableSuccess' : 'actions.disableSuccess'))
    await crud.refresh()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

async function rotateSecret(row: OidcClientDto): Promise<void> {
  try {
    showSecret(await bridge.clients.rotateSecret(row.id))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

/** Opening a tile is editing it; whoever may not edit reads the same form read-only. */
function openClient(row: OidcClientDto): void {
  if (crud.canUpdate) crud.openEdit(row)
  else crud.openView(row)
}

function clientName(row: OidcClientDto): string {
  return row.displayName || row.clientId
}

function clientIcon(row: OidcClientDto): string {
  return row.clientType === OidcClientType.Public ? 'mdi:cellphone-link' : 'mdi:server-security'
}

// No Edit action: the tile itself opens the client (C12 rule 3).
const rowActions: RowAction<OidcClientDto>[] = [
  {
    key: 'enable',
    label: 'actions.enable',
    show: (row) => can('identity.oidcClient.update') && !row.isEnabled,
    onClick: (row) => void setEnabled(row, true),
  },
  {
    key: 'disable',
    label: 'actions.disable',
    type: 'warning',
    confirm: 'actions.disableConfirm',
    show: (row) => can('identity.oidcClient.update') && row.isEnabled,
    onClick: (row) => void setEnabled(row, false),
  },
  {
    key: 'rotateSecret',
    label: 'actions.rotateSecret',
    confirm: 'actions.rotateSecretConfirm',
    show: (row) => can('identity.oidcClient.update') && row.clientType === OidcClientType.Confidential,
    onClick: (row) => void rotateSecret(row),
  },
  deleteAction(crud),
]

/** Drop the secret from memory when the overlay closes: it is a credential, not page state. */
function onSecretToggle(show: boolean): void {
  if (!show) issued.value = null
}

async function copy(value: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value)
    message.success(t('secret.copied'))
  } catch {
    // Clipboard is permission-gated and unavailable over plain http; the value
    // stays selectable in the input, so this is a nudge, not a failure.
    message.warning(t('secret.copyFailed'))
  }
}
</script>

<style scoped>
.oidc-client-card__head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
}
.oidc-client-card__glyph {
  display: inline-flex;
  color: var(--tnzi-primary, #6d5ce7);
}
.oidc-client-card__name {
  flex: 1;
  min-width: 0;
  font-weight: 500;
  font-size: 14px;
  color: var(--tnzi-base-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.oidc-client-card__id {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 8px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.oidc-client-card__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-bottom: 8px;
}
.oidc-client-card__muted {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
</style>
