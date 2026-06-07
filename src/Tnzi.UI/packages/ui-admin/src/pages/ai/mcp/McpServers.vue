<template>
  <!--
    McpServers — Phase 5 Task 5.11. Entity-driven CRUD over EXTERNAL MCP
    server registrations (AI_McpServerRegistration table). The page lets
    admins register, edit, and test connections to MCP servers that Tnzi
    connects to as a CLIENT.

    SEMANTIC NOTE — this page does NOT manage Tnzi's own self-hosted MCP
    server (the one that exposes Tnzi's tools to external MCP clients). That
    config lives in appsettings.json under McpServerOptions and is intentionally
    not editable from the UI. The header callout reinforces this distinction
    so admins don't conflate the two.

    Follows the Providers canonical pattern (Task 5.8) — same
    test-connection inline panel, same tri-state token UX (blank = keep on
    edit). Custom #header slot replaces the default title block to surface
    the semantic callout next to the page title.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="mcpServerColumns"
    :title="title"
    :title-help="t('banner.body')"
    :title-help-title="t('banner.title')"
    :translate="t"
    :form-modal-width="800"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="mcpServerFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
      />
      <p v-if="mode === 'edit'" class="mcp-servers__hint">
        Leave <strong>Auth Token</strong> blank to keep the existing token. Enter
        a new value to rotate it.
      </p>
      <div
        v-if="mode === 'edit' && (formData as Record<string, unknown>)?.id"
        class="mcp-servers__test"
      >
        <button
          type="button"
          class="mcp-servers__btn"
          :disabled="testBusy"
          @click="onTestConnection((formData as Record<string, unknown>).id as string)"
        >
          {{ testBusy ? 'Testing…' : 'Test Connection' }}
        </button>
        <span
          v-if="testStatus"
          class="mcp-servers__status"
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
import { mcpServerColumns, mcpServerFormSchema } from './mcp-server-config'
import type {
  McpServerRegistrationDto,
  CreateMcpServerRegistrationDto,
  UpdateMcpServerRegistrationDto,
} from '@tnzi/core/services/ai'

const title = 'pageTitle'

const bridge = createAiBridge({ client: useAdminClient() })

/**
 * Form -> CreateMcpServerRegistrationDto adapter. Trims string fields and
 * normalizes blank-strings to null so backend validation handles the
 * required/optional distinction consistently.
 */
function toCreateDto(form: Record<string, unknown>): CreateMcpServerRegistrationDto {
  const authToken = String(form.authToken ?? '').trim()
  return {
    name: String(form.name ?? '').trim(),
    serverUrl: String(form.serverUrl ?? '').trim(),
    transport: String(form.transport ?? '').trim(),
    command: (form.command as string | undefined)?.trim() || null,
    arguments: (form.arguments as string | undefined) || null,
    authType: (form.authType as string | undefined) || null,
    authToken: authToken || null,
    priority: (form.priority as number | undefined) ?? 0,
    isEnabled: (form.isEnabled as boolean | undefined) ?? true,
    description: (form.description as string | undefined) || null,
    tags: (form.tags as string | undefined) || null,
  }
}

/**
 * Form -> UpdateMcpServerRegistrationDto adapter. Tri-state authToken:
 * blank means "keep existing", so we omit the field entirely.
 */
function toUpdateDto(form: Record<string, unknown>): UpdateMcpServerRegistrationDto {
  const authTokenRaw = String(form.authToken ?? '')
  const dto: UpdateMcpServerRegistrationDto = {
    name: (form.name as string | undefined) ?? null,
    serverUrl: (form.serverUrl as string | undefined) ?? null,
    transport: (form.transport as string | undefined) ?? null,
    command: (form.command as string | undefined) ?? null,
    arguments: (form.arguments as string | undefined) ?? null,
    authType: (form.authType as string | undefined) ?? null,
    priority: (form.priority as number | undefined) ?? null,
    isEnabled: (form.isEnabled as boolean | undefined) ?? null,
    description: (form.description as string | undefined) ?? null,
    tags: (form.tags as string | undefined) ?? null,
  }
  if (authTokenRaw.trim().length > 0) {
    dto.authToken = authTokenRaw
  }
  return dto
}

const crud = useCrudPage<McpServerRegistrationDto>({
  pageId: 'ai.mcp',
  columns: mcpServerColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.mcpServers.fetch(query),
  createData: (data) => bridge.mcpServers.create(toCreateDto(data as Record<string, unknown>)),
  updateData: (id, data) =>
    bridge.mcpServers.update(String(id), toUpdateDto(data as Record<string, unknown>)),
  deleteData: (ids) => bridge.mcpServers.delete(ids.map(String)),
})

const rowActions: RowAction<McpServerRegistrationDto>[] = [editAction(crud), deleteAction(crud)]


crud.refresh().catch(() => undefined)

const testBusy = ref(false)
const testStatus = ref<{ kind: 'success' | 'error'; message: string } | null>(null)

async function onTestConnection(id: string) {
  if (!id || testBusy.value) return
  testBusy.value = true
  testStatus.value = null
  try {
    const result = await bridge.mcpServers.test(id)
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

const t = (key: string) => translatePageKey('ai.mcp', key)

defineExpose({ onTestConnection, testBusy, testStatus, toCreateDto, toUpdateDto })
</script>

<style scoped>
.mcp-servers__hint {
  margin: 0.5rem 0 0;
  font-size: 0.8rem;
  color: var(--tnzi-base-text-muted, #888);
}
.mcp-servers__test {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-top: 0.75rem;
}
.mcp-servers__btn {
  padding: 0.4rem 0.9rem;
  cursor: pointer;
}
.mcp-servers__btn:disabled {
  cursor: wait;
  opacity: 0.6;
}
.mcp-servers__status[data-status='error'] {
  color: var(--tnzi-error, #d03050);
}
</style>
