<template>
  <NSpin :show="loading">
    <div v-if="request" class="sd">
    <TDescriptions :items="facts" :columns="2" />

    <section class="sd__block">
      <h4 class="sd__title">{{ t('detail.recipients') }}</h4>
      <ol class="sd__signers">
        <li v-for="signer in request.recipients" :key="signer.id" class="sd-signer">
          <span class="sd-signer__order">{{ signer.order }}</span>
          <div class="sd-signer__who">
            <span class="sd-signer__name">{{ signer.name }}</span>
            <span class="sd-signer__role">{{ signer.role }}</span>
            <span v-if="signer.email" class="sd-signer__email">{{ signer.email }}</span>
          </div>
          <div class="sd-signer__trail">
            <TStatusBadge
              :value="String(signer.status)"
              :type="recipientStatusTone(signer.status)"
              :label="String(signer.status)"
            />
            <!-- The one timestamp that matters for this signer's current
                 state. Showing all four turns the row into a log nobody
                 reads; the drawer answers "where is this person up to". -->
            <span class="sd-signer__when">{{ signerWhen(signer) }}</span>
          </div>
          <p v-if="signer.declineReason" class="sd-signer__reason">
            <TSvgIcon icon="mdi:comment-alert-outline" :size="13" />{{ signer.declineReason }}
          </p>
        </li>
      </ol>
    </section>

    <section v-if="request.finalPdfFileId" class="sd__block">
      <h4 class="sd__title">{{ t('detail.output') }}</h4>
      <div class="sd__files">
        <TFileLink :file-id="request.finalPdfFileId" kind="download">
          <TSvgIcon icon="mdi:file-pdf-box" :size="16" />{{ t('detail.finalPdf') }}
        </TFileLink>
        <!--
          Completed but no certificate = certificate generation failed. The
          signed document itself is still valid and still says so, so this is
          a note rather than an error state.
        -->
        <TFileLink
          v-if="request.completionCertificateFileId"
          :file-id="request.completionCertificateFileId"
          kind="download"
        >
          <TSvgIcon icon="mdi:certificate-outline" :size="16" />{{ t('detail.certificate') }}
        </TFileLink>
        <span v-else class="sd__no-cert">
          <TSvgIcon icon="mdi:information-outline" :size="13" />{{ t('detail.noCertificate') }}
        </span>
      </div>
      <p v-if="request.sha256" class="sd__hash">
        <span class="sd__hash-label">{{ t('detail.sha256') }}</span>
        <code>{{ request.sha256 }}</code>
      </p>
      </section>
    </div>
  </NSpin>
</template>

<script setup lang="ts">
/**
 * Read-only view of one signing request.
 *
 * Answers three questions in order: what is this, who still has to act, and -
 * once sealed - where is the evidence. The hash is shown in full rather than
 * truncated: its whole purpose is to be compared against another copy of the
 * document, and a truncated hash cannot do that.
 *
 * ★ It loads its own record from the id rather than taking the list row.
 *   The paged list projects `EnvelopeListDto` (counts, no per-recipient
 *   detail) while this panel needs `EnvelopeDto` - two genuinely different
 *   shapes, not a subset. Threading the detail through the page's row type
 *   would mean casting one into the other, which is exactly the kind of lie
 *   that survives typecheck and fails at runtime.
 */
import { computed, ref, watch } from 'vue'
import { NSpin } from 'naive-ui'
import { TDescriptions, TSvgIcon } from '@tnzi/ui'
import { formatDateOnly, formatDateTime } from '@tnzi/core'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'
import TFileLink from '../../../components/display/TFileLink.vue'
import { EMPTY_DASH } from '../../../utils/placeholders'
import { createSigningBridge } from '../../../services/bridges/signing-bridge'
import { useAdminClient } from '../../../plugin/client'
import { recipientStatusTone } from '../signing-config'
import { SigningRecipientStatus, type EnvelopeDto, type SignerDto } from '@tnzi/core/services/signing'

const props = defineProps<{
  requestId?: string | null
  translate: (key: string) => string
}>()

const t = (key: string): string => props.translate(key)

const bridge = createSigningBridge({ client: useAdminClient() })
const request = ref<EnvelopeDto | null>(null)
const loading = ref(false)

// A request sequence number, so a slow response for a row the operator has
// already navigated away from cannot overwrite the one now on screen.
let sequence = 0

watch(
  () => props.requestId,
  async (id) => {
    const ticket = ++sequence
    request.value = null
    if (!id) return
    loading.value = true
    try {
      const loaded = await bridge.requests.getById(id)
      if (ticket === sequence) request.value = loaded
    } catch {
      // Leaves `request` null, which renders nothing. Rethrowing here would
      // surface as an unhandled rejection from inside a watcher - noise in the
      // console and no better an answer for the operator.
      if (ticket === sequence) request.value = null
    } finally {
      if (ticket === sequence) loading.value = false
    }
  },
  { immediate: true },
)

const facts = computed(() => {
  const r = request.value
  if (!r) return []
  return [
    { label: t('columns.status'), value: String(r.status) },
    { label: t('form.isSequential'), value: r.isSequential ? t('detail.sequential') : t('detail.parallel') },
    { label: t('columns.expiresAt'), value: formatDateOnly(r.expiresAt, { utc: true }) || EMPTY_DASH },
    { label: t('form.completedAt'), value: formatDateTime(r.completedAt) || EMPTY_DASH },
    { label: t('form.hostEntityType'), value: r.hostEntityType || EMPTY_DASH },
    { label: t('form.hostEntityId'), value: r.hostEntityId || EMPTY_DASH },
  ]
})

function signerWhen(signer: SignerDto): string {
  const stamp =
    signer.status === SigningRecipientStatus.Signed
      ? signer.signedAt
      : signer.status === SigningRecipientStatus.Declined
        ? signer.declinedAt
        : signer.status === SigningRecipientStatus.Viewed
          ? signer.viewedAt
          : signer.sentAt
  return formatDateTime(stamp) || EMPTY_DASH
}
</script>

<style scoped>
.sd {
  display: flex;
  flex-direction: column;
  gap: 20px;
}
.sd__block {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.sd__title {
  margin: 0;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--tnzi-base-text-muted);
}
.sd__signers {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.sd-signer {
  display: grid;
  grid-template-columns: 20px 1fr auto;
  align-items: center;
  gap: 4px 10px;
  padding: 8px 10px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-sm, 6px);
}
.sd-signer__order {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.sd-signer__who {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px 8px;
  min-width: 0;
}
.sd-signer__name {
  font-size: 13px;
  font-weight: 600;
}
.sd-signer__role,
.sd-signer__email {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.sd-signer__trail {
  display: flex;
  align-items: center;
  gap: 8px;
}
.sd-signer__when {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.sd-signer__reason {
  grid-column: 2 / -1;
  display: flex;
  align-items: center;
  gap: 4px;
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-warning, #f0a020);
}
.sd__files {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 16px;
}
.sd__no-cert {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.sd__hash {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 6px;
  margin: 0;
  font-size: 12px;
}
.sd__hash-label {
  color: var(--tnzi-base-text-muted);
}
.sd__hash code {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 11px;
  overflow-wrap: anywhere;
}

@media (max-width: 767px) {
  .sd-signer {
    grid-template-columns: 20px 1fr;
  }
  .sd-signer__trail {
    grid-column: 2 / -1;
  }
}
</style>
