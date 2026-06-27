<template>
  <!--
    Providers — LLM provider catalogue rendered as a TCardPage grid.

    Each card shows a provider-type glyph, name, default model, an enabled
    badge and a live connection-status badge (idle → testing → ok | error).
    The "Test" button calls bridge.providers.test(id) and surfaces latency on
    success or the error text on failure. Test state is per-card (a reactive
    Map keyed by provider id) so several providers can be probed in parallel.

    Create/edit go through the standard CRUD modal (#form slot). API key is
    tri-state: required on create; on edit a blank entry keeps the existing
    cipher, a non-empty entry rotates it. The toCreateDto / toUpdateDto
    adapters enforce the "blank means keep" rule at the form → DTO boundary.
  -->
  <div class="ai-provider-page">
    <TCardPage
      :state="crud"
      mode="page"
      :title="t('title')"
      :title-help="t('banner')"
      :cols="{ xs: 1, sm: 2, md: 3, lg: 4 }"
      :search-fields="searchFields"
      :search-placeholder="t('search.placeholder')"
      :form-modal-width="760"
      :translate="t"
    >
      <template #card="{ item }">
        <TEntityCard>
          <div class="flex items-center gap-8px mb-6px">
            <span class="ai-provider-card__icon">
              <TSvgIcon :icon="providerIcon(item.providerType || item.name)" :size="18" />
            </span>
            <span class="ai-provider-card__name flex-1">{{ item.name }}</span>
            <!-- Configuration-sourced entries (appsettings AI:Providers) get a
                 source badge — same muted style as the Skills page's 'File' tag. -->
            <NTag
              v-if="isConfigSource(item)"
              size="small"
              type="default"
              :bordered="false"
            >
              {{ t('source.configuration') }}
            </NTag>
            <NTag
              v-if="item.scope === ResourceScope.System"
              size="small"
              type="warning"
              :bordered="false"
            >
              {{ t('badge.system') }}
            </NTag>
            <NTag
              size="small"
              :type="item.isEnabled ? 'success' : 'default'"
              :bordered="false"
            >
              {{ item.isEnabled ? t('badge.enabled') : t('badge.disabled') }}
            </NTag>
          </div>

          <div class="ai-provider-card__type font-mono">{{ item.providerType }}</div>

          <div class="ai-provider-card__model flex items-center gap-4px">
            <TSvgIcon icon="mdi:cube-outline" :size="13" color="var(--tnzi-base-text-muted, #888)" />
            <span>{{ item.defaultModel || t('card.noModel') }}</span>
          </div>

          <!-- Connection test is rejected (400) for Configuration entries —
               hide the status row alongside the Test action. -->
          <div v-if="!isConfigSource(item)" class="flex items-center gap-6px mt-8px">
            <span class="ai-provider-card__status-label text-12px text-muted">{{ t('test.label') }}:</span>
            <NTag
              size="small"
              :type="statusTagType(testStateOf(item.id).status)"
              :bordered="false"
            >
              {{ t(`badge.${testStateOf(item.id).status}`) }}
            </NTag>
            <span
              v-if="testStateOf(item.id).status === 'ok'"
              class="text-12px text-success font-mono"
            >
              ✓ {{ testStateOf(item.id).latency }}ms
            </span>
            <span
              v-else-if="testStateOf(item.id).status === 'error'"
              class="ai-provider-card__err text-12px text-error"
              :title="testStateOf(item.id).error || ''"
            >
              ✗ {{ testStateOf(item.id).error || t('test.failed') }}
            </span>
          </div>

          <!-- Configuration entries are read-only (backend rejects update /
               delete / testConnection with 400) — omit the actions footer
               entirely so no dead buttons render. -->
          <template v-if="!isConfigSource(item)" #actions>
            <NButton
              size="small"
              type="primary"
              ghost
              :loading="testStateOf(item.id).status === 'testing'"
              @click="testOne(item)"
            >
              {{ t('actions.test') }}
            </NButton>
            <NButton size="small" ghost :disabled="isLocked(item)" @click="crud.openEdit(item)">
              {{ t('actions.edit') }}
            </NButton>
            <NPopconfirm :disabled="isLocked(item)" @positive-click="removeOne(item)">
              <template #trigger>
                <NButton size="small" type="error" ghost :disabled="isLocked(item)">{{ t('actions.delete') }}</NButton>
              </template>
              {{ t('deleteConfirm') }}
            </NPopconfirm>
          </template>
        </TEntityCard>
      </template>

      <template #form="{ formData, mode }">
        <TFormSchemaRenderer
          :schema="providerFormSchema"
          :model="(formData ?? {}) as Record<string, unknown>"
          :readonly="mode === 'view'"
          :translate="t"
          :columns="2"
        />
        <p v-if="mode === 'edit'" class="ai-provider-form__hint text-12px text-muted mt-8px">
          {{ t('form.apiKeyHint') }}
        </p>
      </template>
    </TCardPage>
  </div>
</template>

<script setup lang="ts">
import { reactive } from 'vue'
import { NButton, NPopconfirm, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TCardPage from '../../../components/crud/TCardPage.vue'
import TEntityCard from '../../../components/data/TEntityCard.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { useAdminAuthStore } from '../../../stores/useAdminAuthStore'
import { ResourceScope } from '@tnzi/core/enums'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { translatePageKey } from '../../_shared/translate'
import {
  providerFormSchema,
  providerIcon,
  providerSearchFields as searchFields,
} from './provider-config'
import type {
  ProviderDto,
  CreateProviderDto,
  UpdateProviderDto,
} from '@tnzi/core/services/ai'

const t = (key: string) => translatePageKey('ai.providers', key)

const bridge = createAiBridge({ client: useAdminClient() })
const authStore = useAdminAuthStore()

// Mirrors the backend write guard (ProviderService): system providers are locked only for
// tenant-scoped sessions; without tenant context (host admin / MT off) they remain editable.
function isLocked(item: ProviderDto): boolean {
  return item.scope === ResourceScope.System && !!authStore.currentTenantId
}

// Configuration-sourced entries (appsettings AI:Providers, synthetic stable id)
// are read-only: the backend rejects update/delete/testConnection with 400.
// The card shows a source badge instead of the edit/delete/test actions.
function isConfigSource(item: ProviderDto): boolean {
  return item.source === 'Configuration'
}


/**
 * Form → CreateProviderDto adapter. apiKey trimmed; empty becomes null so the
 * backend validator can flag the missing-key error consistently.
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
 * Form → UpdateProviderDto adapter. Tri-state apiKey: a blank entry means
 * "keep existing", so we omit the field entirely from the payload; a non-empty
 * entry rotates the key.
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
  columns: [],
  rowKey: (p) => p.id,
  fetchData: (query) => bridge.providers.fetch(query),
  createData: (data) => bridge.providers.create(toCreateDto(data as Record<string, unknown>)),
  updateData: (id, data) =>
    bridge.providers.update(String(id), toUpdateDto(data as Record<string, unknown>)),
  deleteData: (ids) => bridge.providers.delete(ids.map(String)),
})
crud.refresh().catch(() => undefined)

// --- per-card connection-test state -----------------------------------------
type TestStatus = 'idle' | 'testing' | 'ok' | 'error'
interface TestState {
  status: TestStatus
  latency?: number
  error?: string
}

const testStates = reactive(new Map<string, TestState>())

function testStateOf(id: string): TestState {
  return testStates.get(id) ?? { status: 'idle' }
}

function statusTagType(status: TestStatus): 'default' | 'info' | 'success' | 'error' {
  if (status === 'ok') return 'success'
  if (status === 'error') return 'error'
  if (status === 'testing') return 'info'
  return 'default'
}

async function testOne(item: ProviderDto): Promise<void> {
  if (testStateOf(item.id).status === 'testing') return
  testStates.set(item.id, { status: 'testing' })
  try {
    const result = await bridge.providers.test(item.id)
    if (result.ok) {
      testStates.set(item.id, { status: 'ok', latency: result.latency })
    } else {
      testStates.set(item.id, { status: 'error', error: result.error || t('test.failed') })
    }
  } catch (err) {
    testStates.set(item.id, {
      status: 'error',
      error: err instanceof Error ? err.message : t('test.failed'),
    })
  }
}

async function removeOne(item: ProviderDto): Promise<void> {
  await crud.handleDelete([item.id])
}

defineExpose({ testOne, testStateOf, testStates, toCreateDto, toUpdateDto, isConfigSource })
</script>

<style scoped>
.ai-provider-page {
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  min-height: 0;
}
.ai-provider-card__icon {
  display: inline-flex;
  color: var(--tnzi-primary, #6d5ce7);
}
.ai-provider-card__name {
  font-weight: 500;
  font-size: 14px;
  color: var(--tnzi-base-text);
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-provider-card__type {
  font-size: 11px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 6px;
}
.ai-provider-card__model {
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #888);
  line-height: 1.45;
  min-width: 0;
}
.ai-provider-card__model span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-provider-card__err {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
