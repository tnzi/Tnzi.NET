<template>
  <!--
    ProviderConfig — Phase 5 Task 5.8. Entity-driven CRUD over LLM providers
    (AI_Provider table) plus a per-row "Test Connection" action surfaced
    inside the edit form.

    Follows the KbManager canonical pattern (Task 5.10). Test status is
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
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="providerFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
      <p v-if="mode === 'edit'" class="provider-config__hint">
        Leave <strong>API Key</strong> blank to keep the existing key. Enter a new
        value to rotate it.
      </p>
      <div
        v-if="mode === 'edit' && (formData as Record<string, unknown>)?.id"
        class="provider-config__test"
      >
        <button
          type="button"
          class="provider-config__btn"
          :disabled="testBusy"
          @click="onTestConnection((formData as Record<string, unknown>).id as string)"
        >
          {{ testBusy ? 'Testing…' : 'Test Connection' }}
        </button>
        <span
          v-if="testStatus"
          class="provider-config__status"
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
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { translatePageKey } from '../../_shared/translate'
import { providerColumns, providerFormSchema } from './provider-config-schema'
import type {
  ProviderDto,
  CreateProviderDto,
  UpdateProviderDto,
} from '@tnzi/core/services/ai'

const title = 'LLM Providers'

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
.provider-config__hint {
  margin: 0.5rem 0 0;
  font-size: 0.8rem;
  color: var(--n-text-color-3, #888);
}
.provider-config__test {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 0.75rem;
}
.provider-config__btn {
  padding: 0.4rem 0.9rem;
  cursor: pointer;
}
.provider-config__btn:disabled {
  cursor: wait;
  opacity: 0.6;
}
.provider-config__status[data-status='error'] {
  color: var(--n-error-color, #d03050);
}
</style>
