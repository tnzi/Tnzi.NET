<template>
  <!--
    Providers — Phase 5 Task 5.8. Entity-driven CRUD over LLM providers
    (AI_Provider table) plus a per-row "Test Connection" action surfaced
    inside the edit form.

    Follows the Knowledge canonical pattern (Task 5.10). Test status is
    exposed via a small inline panel in the #form slot rather than a Naive UI
    message provider — ui-admin doesn't currently wrap the shell with
    NMessageProvider, so useMessage() is unavailable. Inline status keeps
    the page test-friendly and avoids an unmounted-provider warn.

    API key tri-state UX (Stage 3a backend semantics):
      - Create: apiKey is required.
      - Update: blank (undefined / empty after trim) = keep existing cipher;
        non-empty = rotate. Setting it to literal '' to clear the key is a
        rare admin operation and is achieved by the dedicated cipher-clear
        flow on the backend, not surfaced here.

    The form's apiKey field always renders empty when opening edit mode;
    the form-shape -> DTO adapters (toCreateDto / toUpdateDto) handle the
    "blank means keep" rule at the boundary.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="providerColumns"
    :title="title"
    :translate="t"
    :form-modal-width="760"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="providerFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
      />
      <p v-if="mode === 'edit'" class="providers__hint">
        Leave <strong>API Key</strong> blank to keep the existing key. Enter a new
        value to rotate it.
      </p>
      <div
        v-if="mode === 'edit' && (formData as Record<string, unknown>)?.id"
        class="providers__test"
      >
        <button
          type="button"
          class="providers__btn"
          :disabled="testBusy"
          @click="onTestConnection((formData as Record<string, unknown>).id as string)"
        >
          {{ testBusy ? 'Testing…' : 'Test Connection' }}
        </button>
        <span
          v-if="testStatus"
          class="providers__status"
          :data-status="testStatus.kind"
        >
          {{ testStatus.message }}
        </span>
      </div>
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../../headless/rowActions'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { translatePageKey } from '../../_shared/translate'
import { providerColumns, providerFormSchema } from './provider-config'
import type {
  ProviderDto,
  CreateProviderDto,
  UpdateProviderDto,
} from '@tnzi/core/services/ai'

const title = 'title'

const bridge = createAiBridge({ client: useAdminClient() })

/**
 * Form -> CreateProviderDto adapter. apiKey trimmed; empty becomes null so
 * the backend validator can flag the missing-key error consistently.
 */
function toCreateDto(form: Record<string, unknown>): CreateProviderDto {
  const apiKey = String(form.apiKey ?? '').trim()
  return {
    name: String(form.name ?? '').trim(),
    providerType: String(form.providerType ?? '').trim(),
    endpoint: (form.endpoint as string | undefined)?.trim() || null,
    apiKey: apiKey || null,
    defaultModel: (form.defaultModel as string | undefined)?.trim() || null,
    priority: (form.priority as number | undefined) ?? 0,
    isEnabled: (form.isEnabled as boolean | undefined) ?? true,
    description: (form.description as string | undefined) || null,
  }
}

/**
 * Form -> UpdateProviderDto adapter. Tri-state apiKey: blank entry means
 * "keep existing", so we omit the field entirely from the payload.
 */
function toUpdateDto(form: Record<string, unknown>): UpdateProviderDto {
  const apiKeyRaw = String(form.apiKey ?? '')
  const dto: UpdateProviderDto = {
    name: (form.name as string | undefined) ?? null,
    providerType: (form.providerType as string | undefined) ?? null,
    endpoint: (form.endpoint as string | undefined) ?? null,
    defaultModel: (form.defaultModel as string | undefined) ?? null,
    priority: (form.priority as number | undefined) ?? null,
    isEnabled: (form.isEnabled as boolean | undefined) ?? null,
    description: (form.description as string | undefined) ?? null,
  }
  if (apiKeyRaw.trim().length > 0) {
    dto.apiKey = apiKeyRaw
  }
  return dto
}

const crud = useCrudPage<ProviderDto>({
  pageId: 'ai.providers',
  columns: providerColumns,
  rowKey: (p) => p.id,
  fetchData: (query) => bridge.providers.fetch(query),
  createData: (data) => bridge.providers.create(toCreateDto(data as Record<string, unknown>)),
  updateData: (id, data) =>
    bridge.providers.update(String(id), toUpdateDto(data as Record<string, unknown>)),
  deleteData: (ids) => bridge.providers.delete(ids.map(String)),
})

const rowActions: RowAction<ProviderDto>[] = [editAction(crud), deleteAction(crud)]


crud.refresh().catch(() => undefined)

const testBusy = ref(false)
const testStatus = ref<{ kind: 'success' | 'error'; message: string } | null>(null)

async function onTestConnection(id: string) {
  if (!id || testBusy.value) return
  testBusy.value = true
  testStatus.value = null
  try {
    const result = await bridge.providers.test(id)
    if (result.ok) {
      testStatus.value = {
        kind: 'success',
        message: `Connection OK (${result.latency}ms)`,
      }
    } else {
      testStatus.value = {
        kind: 'error',
        message: result.error || 'Connection failed',
      }
    }
  } catch (err) {
    testStatus.value = {
      kind: 'error',
      message: err instanceof Error ? err.message : 'Connection test failed',
    }
  } finally {
    testBusy.value = false
  }
}

const t = (key: string) => translatePageKey('ai.providers', key)

defineExpose({ onTestConnection, testBusy, testStatus, toCreateDto, toUpdateDto })
</script>

<style scoped>
.providers__hint {
  margin: 0.5rem 0 0;
  font-size: 0.8rem;
  color: var(--tnzi-base-text-muted, #888);
}
.providers__test {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 0.75rem;
}
.providers__btn {
  padding: 0.4rem 0.9rem;
  cursor: pointer;
}
.providers__btn:disabled {
  cursor: wait;
  opacity: 0.6;
}
.providers__status[data-status='error'] {
  color: var(--tnzi-error, #d03050);
}
</style>
