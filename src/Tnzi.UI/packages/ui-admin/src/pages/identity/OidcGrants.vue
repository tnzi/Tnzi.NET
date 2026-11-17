<template>
  <!--
    OpenID Connect grants: which person authorized which application. Read +
    revoke only - a grant comes from the user signing in or consenting, so
    there is no create or edit. Revoking stops the application's refresh
    tokens; its current access token runs out within its short lifetime.

    A grant reads as a document (application, person, scopes, status, date),
    and nobody compares those across rows, so each grant is a row card. The
    person is shown by name and email; the user id stays on the row for
    support (hover title + the "Copy user ID" action).
  -->
  <TItemPage
    :state="crud"
    :title="title"
    :translate="t"
    :show-default-search="false"
    :search-fields="oidcGrantSearchFields"
    :row-actions="rowActions"
  >
    <template #item="{ item }">
      <TItemCard
        :title="grantClientLabel(item)"
        icon="mdi:shield-key-outline"
        :icon-tone="item.status === 'valid' ? 'success' : 'default'"
        :tags="grantTags(item)"
        :meta="grantMeta(item)"
        :muted="item.status !== 'valid'"
      >
        <template #actions>
          <TRowActions :row="item" :actions="rowActions" :translate="t" />
        </template>
      </TItemCard>
    </template>
  </TItemPage>
</template>

<script setup lang="ts">
import TItemPage from '../../components/crud/TItemPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TItemCard, { type ItemCardMeta, type ItemCardTag } from '../../components/data/TItemCard.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { deleteAction, type RowAction } from '../../headless/row-actions'
import { createOidcBridge } from '../../services/bridges/oidc-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { formatDateTime } from '@tnzi/core'
import type { OidcGrantDto } from '@tnzi/core/services/oidc'
import {
  grantClientLabel,
  grantPersonLabel,
  grantScopesLabel,
  oidcGrantColumns,
  oidcGrantSearchFields,
} from './oidc-client-config'

const title = 'title'
const t = makePageTranslator('identity.oidcGrants')
const message = useSafeMessage()
const bridge = createOidcBridge({ client: useAdminClient() })

const crud = useCrudPage<OidcGrantDto>({
  pageId: 'identity.oidcGrants',
  // Only view + delete exist in the catalogue: a grant is never created or
  // edited by an administrator.
  permission: { delete: 'identity.oidcGrant.delete' },
  columns: oidcGrantColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.grants.fetch(query),
  deleteData: (ids) => bridge.grants.delete(ids.map(String)),
})

function grantTags(row: OidcGrantDto): ItemCardTag[] {
  return row.status === 'valid'
    ? [{ label: t('status.valid'), type: 'success' }]
    : [{ label: t('status.revoked') }]
}

function grantMeta(row: OidcGrantDto): ItemCardMeta[] {
  const person = grantPersonLabel(row)
  return [
    { icon: 'mdi:account-outline', text: person, title: `${person}\n${t('columns.userId')}: ${row.userId}` },
    { icon: 'mdi:key-outline', text: grantScopesLabel(row) },
    { icon: 'mdi:calendar-check-outline', text: formatDateTime(row.creationDate) },
  ]
}

async function copyUserId(row: OidcGrantDto): Promise<void> {
  try {
    await navigator.clipboard.writeText(row.userId)
    message.success(t('actions.userIdCopied'))
  } catch {
    // Clipboard is permission-gated and unavailable over plain http.
    message.warning(`${t('columns.userId')}: ${row.userId}`)
  }
}

// Labelled "Revoke": the row stays as a revoked record; what goes away is the
// application's ability to refresh its tokens.
const rowActions: RowAction<OidcGrantDto>[] = [
  deleteAction(crud, { label: 'actions.revoke', show: (row) => row.status === 'valid' }),
  { key: 'copyUserId', label: 'actions.copyUserId', onClick: (row) => void copyUserId(row) },
]
</script>

